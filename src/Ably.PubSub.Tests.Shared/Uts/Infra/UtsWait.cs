using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra.Unit;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// The pseudocode's waits: <c>AWAIT_STATE</c>, <c>poll_until</c>, <c>poll_until_success</c>, a wall-clock
    /// bound for any task, and the queue drain behind <c>process_pending_events()</c>.
    /// </summary>
    public static class UtsWait
    {
        /// <summary>
        /// The bound on a state wait or a poll when the spec gives none: 10 seconds, the bottom of the 10-15 s
        /// range <c>integration-testing.md</c> gives state waits.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>How often a poll re-evaluates its condition when the spec gives no interval.</summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// The specs' <c>AWAIT_STATE connection.state == state</c>. Subscribes first, then checks the current
        /// state, and completes exactly once, whichever wins.
        /// </summary>
        public static Task AwaitConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ConnectionStateChange> listener = change =>
            {
                if (change.Current == state)
                {
                    completion.TrySetResult(true);
                }
                else if (change.Current == ConnectionState.Failed)
                {
                    completion.TrySetException(UtsAssert.Failure(
                        $"connection entered FAILED while waiting for {state}: {Describe(change.Reason)}",
                        callerFilePath,
                        callerLineNumber));
                }
            };

            connection.On(listener);
            if (connection.State == state)
            {
                completion.TrySetResult(true);
            }

            return Finish(
                completion.Task,
                timeout,
                () => connection.Off(listener),
                () => $"connection state {state} (current: {connection.State}, reason: {Describe(connection.ErrorReason)})",
                callerFilePath,
                callerLineNumber);
        }

        /// <summary>The specs' <c>AWAIT_STATE channel.state == state</c>; the same guarantees as for a connection.</summary>
        public static Task AwaitChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ChannelStateChange> listener = change =>
            {
                if (change.Current == state)
                {
                    completion.TrySetResult(true);
                }
                else if (change.Current == ChannelState.Failed)
                {
                    completion.TrySetException(UtsAssert.Failure(
                        $"channel '{channel.Name}' entered FAILED while waiting for {state}: {Describe(change.Error)}",
                        callerFilePath,
                        callerLineNumber));
                }
            };

            channel.On(listener);
            if (channel.State == state)
            {
                completion.TrySetResult(true);
            }

            return Finish(
                completion.Task,
                timeout,
                () => channel.Off(listener),
                () => $"channel '{channel.Name}' state {state} (current: {channel.State}, reason: {Describe(channel.ErrorReason)})",
                callerFilePath,
                callerLineNumber);
        }

        /// <summary>
        /// The specs' <c>poll_until(condition, interval, timeout)</c> for a boolean condition. An exception from
        /// the condition aborts the poll at once.
        /// </summary>
        public static async Task PollUntil(
            Func<bool> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            await PollUntil(
                () => Task.FromResult(condition()),
                timeout,
                interval,
                description,
                callerFilePath,
                callerLineNumber).ConfigureAwait(false);
        }

        /// <summary>The specs' <c>poll_until</c> for an async boolean condition (one that awaits the SDK).</summary>
        public static async Task PollUntil(
            Func<Task<bool>> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            await PollUntil<object>(
                async () => await condition().ConfigureAwait(false) ? Settled : null,
                timeout,
                interval,
                description,
                callerFilePath,
                callerLineNumber).ConfigureAwait(false);
        }

        /// <summary>
        /// The value-returning <c>x = poll_until(...)</c>: polls until the condition returns non-null and returns
        /// that value, so later assertions use the settled read rather than a second one.
        /// </summary>
        public static Task<T> PollUntil<T>(
            Func<T> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            where T : class
            => PollUntil(() => Task.FromResult(condition()), timeout, interval, description, callerFilePath, callerLineNumber);

        /// <summary>The value-returning <c>poll_until</c> for an async condition.</summary>
        public static async Task<T> PollUntil<T>(
            Func<Task<T>> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            where T : class
        {
            var bound = timeout ?? DefaultTimeout;
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                T value;
                try
                {
                    value = await condition().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw UtsAssert.Failure(
                        $"poll_until aborted: the condition {Name(description)}threw",
                        callerFilePath,
                        callerLineNumber,
                        ex);
                }

                if (value != null)
                {
                    return value;
                }

                if (stopwatch.Elapsed >= bound)
                {
                    throw UtsAssert.Failure(
                        $"poll_until timed out after {bound.TotalMilliseconds} ms waiting for {Name(description)}the condition",
                        callerFilePath,
                        callerLineNumber);
                }

                await Task.Delay(interval ?? DefaultPollInterval).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The specs' <c>poll_until_success(condition)</c>: an exception means "not ready yet" and the poll
        /// carries on; on timeout the most recent exception is rethrown (as the failure's cause), so the failure
        /// says what was still going wrong.
        /// </summary>
        public static async Task<T> PollUntilSuccess<T>(
            Func<Task<T>> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            where T : class
        {
            var bound = timeout ?? DefaultTimeout;
            var stopwatch = Stopwatch.StartNew();
            Exception lastError = null;
            while (true)
            {
                try
                {
                    var value = await condition().ConfigureAwait(false);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                if (stopwatch.Elapsed >= bound)
                {
                    var message = $"poll_until_success timed out after {bound.TotalMilliseconds} ms waiting for {Name(description)}the condition";
                    throw lastError == null
                        ? UtsAssert.Failure(message, callerFilePath, callerLineNumber)
                        : UtsAssert.Failure(message, callerFilePath, callerLineNumber, lastError);
                }

                await Task.Delay(interval ?? DefaultPollInterval).ConfigureAwait(false);
            }
        }

        /// <summary><c>poll_until_success</c> for a boolean condition.</summary>
        public static Task PollUntilSuccess(
            Func<Task<bool>> condition,
            TimeSpan? timeout = null,
            TimeSpan? interval = null,
            string description = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => PollUntilSuccess<object>(
                async () => await condition().ConfigureAwait(false) ? Settled : null,
                timeout,
                interval,
                description,
                callerFilePath,
                callerLineNumber);

        /// <summary>
        /// Bounds any task by wall-clock time: the specs' <c>WITH timeout: N seconds</c> on a bare <c>AWAIT</c>,
        /// and the guard on every integration await.
        /// </summary>
        public static async Task<T> WithTimeout<T>(
            Task<T> task,
            TimeSpan timeout,
            string what,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            await WithTimeout((Task)task, timeout, what, callerFilePath, callerLineNumber).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        /// <inheritdoc cref="WithTimeout{T}"/>
        public static async Task WithTimeout(
            Task task,
            TimeSpan timeout,
            string what,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task)
            {
                throw UtsAssert.Failure(
                    $"timed out after {timeout.TotalMilliseconds} ms waiting for {what}",
                    callerFilePath,
                    callerLineNumber);
            }

            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// The drain used while a fake clock is enabled (by <c>ADVANCE_TIME</c> before, between and after timers,
        /// and by <c>process_pending_events()</c>): <see cref="Drain"/>, except that a queue parked on a fake
        /// delay counts as settled.
        /// Waiting for idle would deadlock: only an advance can unblock a queue parked on a fake delay.
        /// </summary>
        /// <param name="idleChecks">Each returns true when its queue is empty and nothing is being processed.</param>
        /// <param name="clock">The fake clock: its fresh delays decide "parked", and idle passes are reported to it.</param>
        /// <param name="stallWindow">How long a queue must stay busy, with a fresh delay pending, to count as parked.</param>
        /// <param name="what">The step named in a failure (<c>ADVANCE_TIME</c>, <c>process_pending_events</c>).</param>
        /// <returns>True if the queues went idle; false if they were parked on a fresh fake delay.</returns>
        internal static async Task<bool> DrainOrParked(
            IReadOnlyCollection<Func<bool>> idleChecks,
            FakeClock clock,
            TimeSpan stallWindow,
            TimeSpan timeout,
            string what,
            string callerFilePath,
            int callerLineNumber)
        {
            var stopwatch = Stopwatch.StartNew();
            var busySince = Stopwatch.StartNew();
            var idlePasses = 0;
            var spins = 0;
            while (idlePasses < 2)
            {
                var sequence = clock.NextSequence;
                if (idleChecks.All(check => check()))
                {
                    idlePasses++;
                    clock.MarkIdle(sequence);
                    busySince.Restart();
                }
                else
                {
                    idlePasses = 0;
                    if (busySince.Elapsed >= stallWindow && clock.HasDelaySinceIdle)
                    {
                        return false;
                    }
                }

                if (stopwatch.Elapsed >= timeout)
                {
                    throw UtsAssert.Failure(
                        $"{what}: the SDK's queues didn't empty within {timeout.TotalMilliseconds} ms, and no fake delay started since they were last idle was pending to unblock them",
                        callerFilePath,
                        callerLineNumber);
                }

                if (spins++ < 50)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }

            return true;
        }

        /// <summary>
        /// The mechanism behind <c>process_pending_events()</c>: yields until every idle check reports idle on
        /// two consecutive passes, so work that queues more work (a command that queues a command, a transport
        /// delivery that becomes a command) is drained too.
        /// </summary>
        /// <param name="idleChecks">Each returns true when its queue is empty and nothing is being processed.</param>
        public static async Task Drain(
            IReadOnlyCollection<Func<bool>> idleChecks,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var bound = timeout ?? DefaultTimeout;
            var stopwatch = Stopwatch.StartNew();
            var idlePasses = 0;
            var spins = 0;
            while (idlePasses < 2)
            {
                if (idleChecks.All(check => check()))
                {
                    idlePasses++;
                }
                else
                {
                    idlePasses = 0;
                }

                if (stopwatch.Elapsed >= bound)
                {
                    throw UtsAssert.Failure(
                        $"process_pending_events: the SDK's queues didn't empty within {bound.TotalMilliseconds} ms",
                        callerFilePath,
                        callerLineNumber);
                }

                // A yield first, so continuations already scheduled run; a 1 ms sleep only once the queue
                // has stayed busy for a while, so the drain doesn't spin a core.
                if (spins++ < 50)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
        }

        private static readonly object Settled = new object();

        private static async Task Finish(
            Task completion,
            TimeSpan? timeout,
            Action unsubscribe,
            Func<string> describe,
            string callerFilePath,
            int callerLineNumber)
        {
            try
            {
                var bound = timeout ?? DefaultTimeout;
                var completed = await Task.WhenAny(completion, Task.Delay(bound)).ConfigureAwait(false);
                if (completed != completion)
                {
                    throw UtsAssert.Failure(
                        $"timed out after {bound.TotalMilliseconds} ms waiting for {describe()}",
                        callerFilePath,
                        callerLineNumber);
                }

                await completion.ConfigureAwait(false);
            }
            finally
            {
                unsubscribe();
            }
        }

        private static string Name(string description) => description == null ? string.Empty : $"'{description}': ";

        private static string Describe(ErrorInfo error) => error == null ? "none" : $"{error.Code} {error.Message}";
    }
}
