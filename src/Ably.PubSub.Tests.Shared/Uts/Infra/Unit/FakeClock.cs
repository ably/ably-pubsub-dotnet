using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Transport.States.Connection;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' fake timers: <c>enable_fake_timers()</c>, <c>ADVANCE_TIME(ms)</c> and the fake
    /// <c>NOW()</c>. One clock serves every client a test builds after enabling it, through two hooks:
    /// <c>ClientOptions.NowFunc</c> for every clock read, and <c>ClientOptions.TimerScheduler</c> for every
    /// timer and delay the SDK starts (connection-state timers, channel attach/detach timers, the heartbeat
    /// monitor, the ping timeout, RTL13b reattach, every <c>TimeoutAfter</c>). The mocks take it too, so
    /// a <c>respond_with_delay</c> or an HTTP timeout runs on fake time.
    ///
    /// Nothing scheduled here ever waits on the real clock: a timer fires only when
    /// <see cref="AdvanceAsync"/> moves time past it. An advance fires due timers in time order, drains
    /// the SDK's queues after each one (so work a timer queues runs, and timers it starts are seen), and
    /// repeats until nothing is due within the advance: cascaded work runs to quiescence. A queue that
    /// stays busy on a fake delay started since the queues were last idle counts as parked on it, so the
    /// advance can fire it (see <see cref="UtsWait.DrainOrParked"/>).
    ///
    /// <para>A <c>Schedule</c> with <c>Timeout.InfiniteTimeSpan</c> never fires (no entry is added), as with
    /// the real <c>System.Threading.Timer</c>; any other negative delay is due at once. A callback that throws
    /// doesn't stop the advance: it is recorded in <see cref="CallbackErrors"/> and the advance fails at the
    /// caller's line once everything due has fired.</para>
    /// </summary>
    public sealed class FakeClock : ITimerScheduler
    {
        /// <summary>
        /// How long a queue must stay busy, with a fresh fake delay pending (see <see cref="HasDelaySinceIdle"/>),
        /// before a drain treats it as parked on that delay. Real SDK work drains in microseconds; the window only
        /// has to outlast a JIT or GC stall that happens to coincide with a fresh delay, and every timer an
        /// advance fires while the SDK is truly parked costs one window of real time, so it stays short.
        /// </summary>
        internal static readonly TimeSpan ParkedWindow = TimeSpan.FromMilliseconds(250);

        private readonly object _lock = new object();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Func<bool>> _idleChecks = new List<Func<bool>>();
        private readonly List<Exception> _callbackErrors = new List<Exception>();
        private long _sequence;
        private long _lastIdleSequence;
        private DateTimeOffset _now;

        /// <param name="start">Fake "now" when enabled. Defaults to a fixed instant so runs are repeatable.</param>
        public FakeClock(DateTimeOffset? start = null)
        {
            _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        /// <summary>The specs' <c>NOW()</c> / <c>current_fake_time</c>.</summary>
        public DateTimeOffset Now
        {
            get
            {
                lock (_lock)
                {
                    return _now;
                }
            }
        }

        /// <summary>The fake "now" in Unix milliseconds, the specs' <c>now_millis</c>.</summary>
        public long NowMillis => Now.ToUnixTimeMilliseconds();

        /// <summary>The number of timers and delays waiting to fire.</summary>
        public int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>The <c>ClientOptions.NowFunc</c> value.</summary>
        public Func<DateTimeOffset> NowFunc => () => Now;

        /// <summary>
        /// Every exception a fired callback threw, in order. An advance keeps firing after one throws, then
        /// fails at the caller's <c>ADVANCE_TIME</c> line with the first of them (see <see cref="AdvanceAsync(TimeSpan, string, int)"/>).
        /// </summary>
        public IReadOnlyList<Exception> CallbackErrors
        {
            get
            {
                lock (_lock)
                {
                    return _callbackErrors.ToList();
                }
            }
        }

        /// <summary>
        /// The sequence number the next timer or delay will get. A drain reads it before it checks the queues, so
        /// a delay created while the checks run still counts as "since idle" (<see cref="MarkIdle"/>).
        /// </summary>
        internal long NextSequence
        {
            get
            {
                lock (_lock)
                {
                    return _sequence;
                }
            }
        }

        /// <summary>
        /// True while the clock holds a pending <see cref="ITimerScheduler.Delay"/> (not a <c>Schedule</c>
        /// callback) created since a drain last saw every queue idle: the only kind of entry a busy queue can be
        /// awaiting (a <c>TimeoutAfter</c>, a mock HTTP request timeout or <c>respond_with_delay</c>). The heartbeat
        /// monitor's long-lived delay, and any delay already pending the last time everything was idle, don't
        /// count: a queue that is busy while only those are pending is busy, not parked.
        /// </summary>
        internal bool HasDelaySinceIdle
        {
            get
            {
                lock (_lock)
                {
                    var since = _lastIdleSequence;
                    return _entries.Any(e => e.IsDelay && e.Sequence >= since);
                }
            }
        }

        /// <summary>
        /// The specs' <c>ADVANCE_TIME(ms)</c>. Fires every timer due within the advance, in order, with
        /// fake "now" set to each timer's due time as it fires, then drains; ends with "now" at the target.
        /// Await it before asserting.
        /// </summary>
        public async Task AdvanceAsync(
            TimeSpan by,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by), "time only moves forward");
            }

            DateTimeOffset target;
            int errorsBefore;
            lock (_lock)
            {
                target = _now + by;
                errorsBefore = _callbackErrors.Count;
            }

            await DrainAsync(callerFilePath, callerLineNumber).ConfigureAwait(false);
            while (true)
            {
                Entry next;
                lock (_lock)
                {
                    next = _entries
                        .Where(e => e.Due <= target)
                        .OrderBy(e => e.Due)
                        .ThenBy(e => e.Sequence)
                        .FirstOrDefault();
                    if (next == null)
                    {
                        _now = target;
                        break;
                    }

                    _entries.Remove(next);
                    if (next.Due > _now)
                    {
                        _now = next.Due;
                    }
                }

                try
                {
                    next.Fire();
                }
                catch (Exception ex)
                {
                    // Recorded, not thrown: one throwing callback (an SDK timer of a client disposed meanwhile, a
                    // test's ScheduleAfter block) mustn't leave the timers after it unfired and the queues undrained.
                    lock (_lock)
                    {
                        _callbackErrors.Add(ex);
                    }
                }

                await DrainAsync(callerFilePath, callerLineNumber).ConfigureAwait(false);
            }

            await DrainAsync(callerFilePath, callerLineNumber).ConfigureAwait(false);

            List<Exception> thrown;
            lock (_lock)
            {
                thrown = _callbackErrors.Skip(errorsBefore).ToList();
            }

            if (thrown.Count > 0)
            {
                // A real timer callback that throws takes the process down (an unhandled exception on a pool
                // thread), so the advance fails, at the test's line, once it has fired everything due.
                throw UtsAssert.Failure(
                    $"ADVANCE_TIME: {thrown.Count} fake-clock callback(s) threw; the first is the cause (all are in FakeClock.CallbackErrors)",
                    callerFilePath,
                    callerLineNumber,
                    thrown[0]);
            }
        }

        /// <inheritdoc cref="AdvanceAsync(TimeSpan, string, int)"/>
        public Task AdvanceAsync(
            long milliseconds,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => AdvanceAsync(TimeSpan.FromMilliseconds(milliseconds), callerFilePath, callerLineNumber);

        /// <summary>
        /// The specs' <c>SCHEDULE_AFTER(delay): …</c> inside a mock handler: run the block once fake time
        /// has advanced by <paramref name="delay"/>.
        /// </summary>
        public void ScheduleAfter(TimeSpan delay, Action action) => Schedule(delay, action, isDelay: false);

        IDisposable ITimerScheduler.Schedule(TimeSpan delay, Action callback) => Schedule(delay, callback, isDelay: false);

        Task ITimerScheduler.Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay == Timeout.InfiniteTimeSpan)
            {
                // Task.Delay's "never", which the SDK uses for a disabled monitor.
                return Task.Delay(Timeout.Infinite, cancellationToken);
            }

            // Completed synchronously when it fires, like a Schedule callback: the SDK's continuation (the
            // heartbeat loop's next iteration, a TimeoutAfter's result) runs inside the advance, up to its
            // next await, so a timer it starts is seen by the same advance rather than racing it.
            var completion = new TaskCompletionSource<bool>();
            var entry = Schedule(delay, () => completion.TrySetResult(true), isDelay: true);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    entry.Dispose();
                    completion.TrySetCanceled(cancellationToken);
                });
            }

            return completion.Task;
        }

        /// <summary>
        /// Registers a queue the clock drains between timers: a client's workflow, a mock transport's
        /// delivery chain. The unit base does this for everything it builds.
        /// </summary>
        internal void AddIdleCheck(Func<bool> isIdle)
        {
            lock (_lock)
            {
                _idleChecks.Add(isIdle);
            }
        }

        /// <summary>
        /// Drains every registered queue (before, between and after timers in an advance, and for
        /// <c>process_pending_events()</c> while the clock is enabled). A queue that stays busy for
        /// <see cref="ParkedWindow"/> while <see cref="HasDelaySinceIdle"/> holds counts as parked on that delay
        /// (the SDK awaiting a fake-clock <c>TimeoutAfter</c>, or a mock request timeout), since only an advance can
        /// unblock it; see <see cref="UtsWait.DrainOrParked"/>.
        /// </summary>
        /// <param name="what">The step named in a failure: <c>ADVANCE_TIME</c> or <c>process_pending_events</c>.</param>
        /// <param name="callerFilePath">Filled in by the caller.</param>
        /// <param name="callerLineNumber">Filled in by the caller.</param>
        /// <returns>True if the queues went idle; false if they were parked on a fresh fake delay.</returns>
        internal Task<bool> DrainAsync(string what, string callerFilePath, int callerLineNumber)
        {
            List<Func<bool>> checks;
            lock (_lock)
            {
                checks = _idleChecks.ToList();
            }

            return UtsWait.DrainOrParked(checks, this, ParkedWindow, UtsWait.DefaultTimeout, what, callerFilePath, callerLineNumber);
        }

        /// <summary>
        /// Called by a drain that saw every queue idle: delays numbered below <paramref name="sequence"/> (the
        /// <see cref="NextSequence"/> read before the checks ran) were pending at an idle moment, so nothing busy
        /// later can be parked on them.
        /// </summary>
        internal void MarkIdle(long sequence)
        {
            lock (_lock)
            {
                if (sequence > _lastIdleSequence)
                {
                    _lastIdleSequence = sequence;
                }
            }
        }

        private Task DrainAsync(string callerFilePath, int callerLineNumber)
            => DrainAsync("ADVANCE_TIME", callerFilePath, callerLineNumber);

        private IDisposable Schedule(TimeSpan delay, Action callback, bool isDelay)
        {
            if (delay == Timeout.InfiniteTimeSpan)
            {
                // System.Threading.Timer's "never" (the real scheduler passes -1 ms straight through): no entry,
                // so no advance fires it and it holds no drain open.
                return NeverFires.Instance;
            }

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            lock (_lock)
            {
                var entry = new Entry(this, _now + delay, _sequence++, callback, isDelay);
                _entries.Add(entry);
                return entry;
            }
        }

        private void Cancel(Entry entry)
        {
            lock (_lock)
            {
                _entries.Remove(entry);
            }
        }

        private sealed class Entry : IDisposable
        {
            private readonly FakeClock _clock;
            private readonly Action _callback;

            internal Entry(FakeClock clock, DateTimeOffset due, long sequence, Action callback, bool isDelay)
            {
                _clock = clock;
                Due = due;
                Sequence = sequence;
                _callback = callback;
                IsDelay = isDelay;
            }

            internal DateTimeOffset Due { get; }

            internal long Sequence { get; }

            /// <summary>Created by <see cref="ITimerScheduler.Delay"/>: something may be awaiting it.</summary>
            internal bool IsDelay { get; }

            public void Dispose() => _clock.Cancel(this);

            internal void Fire() => _callback();
        }

        private sealed class NeverFires : IDisposable
        {
            internal static readonly NeverFires Instance = new NeverFires();

            public void Dispose()
            {
            }
        }
    }
}
