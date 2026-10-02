using System;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>enable_fake_timers()</c> / <c>ADVANCE_TIME(ms)</c>, for everything the SDK
    /// <em>measures</em> against the clock.
    ///
    /// Installed through <c>ClientOptions.NowFunc</c>, which is the single clock the SDK reads: token
    /// expiry, the fallback-host cache in <c>AblyHttpRequester</c> (<c>FallbackHostUsedFrom</c>) and the
    /// cumulative <c>HttpMaxRetryDuration</c> budget all go through it. Advancing this clock makes those
    /// elapse instantly.
    ///
    /// It deliberately does <strong>not</strong> fake scheduled timers. Every connection state builds its
    /// own <c>CountdownTimer</c> inline (<c>Transport/States/Connection/*.cs</c>) with no injection point,
    /// so anything the SDK <em>schedules</em> — retry backoff, request timeouts, channel attach deadlines
    /// — is driven instead by shortening the matching public client option:
    ///
    /// <list type="bullet">
    ///   <item><c>RealtimeRequestTimeout</c> — connect/attach/detach/ping deadlines (TO3l11)</item>
    ///   <item><c>DisconnectedRetryTimeout</c> — the wait in DISCONNECTED before retrying</item>
    ///   <item><c>SuspendedRetryTimeout</c> — the wait in SUSPENDED before retrying</item>
    ///   <item><c>ChannelRetryTimeout</c> — the wait before re-attaching a channel</item>
    ///   <item><c>HttpRequestTimeout</c> / <c>HttpOpenTimeout</c> — REST request deadlines</item>
    ///   <item><c>FallbackRetryTimeout</c> — how long a fallback host stays preferred</item>
    /// </list>
    ///
    /// Where a spec's <c>ADVANCE_TIME</c> drives something only a real timer reaches, the derived test
    /// shortens the option instead and says so at the site.
    /// </summary>
    public sealed class TestClock
    {
        private readonly object _lock = new object();
        private DateTimeOffset _now;

        public TestClock()
            : this(DateTimeOffset.UtcNow)
        {
        }

        public TestClock(DateTimeOffset start)
        {
            _now = start;
        }

        /// <summary>A fixed, readable instant for tests that assert on absolute times.</summary>
        public static DateTimeOffset FixedStart { get; } =
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

        /// <summary>The delegate to hand to <c>ClientOptions.NowFunc</c>.</summary>
        public Func<DateTimeOffset> NowFunc => () => Now;

        /// <summary>The specs' <c>ADVANCE_TIME(ms)</c>.</summary>
        public void Advance(int milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));

        public void Advance(TimeSpan by)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by), "Time only moves forward.");
            }

            lock (_lock)
            {
                _now = _now.Add(by);
            }
        }

        public void SetTo(DateTimeOffset instant)
        {
            lock (_lock)
            {
                _now = instant;
            }
        }
    }
}
