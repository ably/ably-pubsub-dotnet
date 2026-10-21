using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Transport.States.Connection;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.InfraTests
{
    /// <summary>Self-tests of the time-bounded waits in <see cref="UtsWait"/>: poll_until, poll_until_success and the wall-clock bound.</summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "common")]
    public class UtsWaitTests
    {
        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

        [Fact]
        public async Task PollUntilReturnsTheSettledValue()
        {
            var calls = 0;

            var value = await UtsWait.PollUntil(
                () => Interlocked.Increment(ref calls) >= 3 ? "settled on read " + calls : null,
                interval: TimeSpan.FromMilliseconds(1));

            value.Should().Be("settled on read 3");
            calls.Should().Be(3, "the poll stops at the first non-null read and returns that read");
        }

        [Fact]
        public async Task PollUntilWithABooleanConditionCompletesOnceItHolds()
        {
            var calls = 0;

            await UtsWait.PollUntil(() => Interlocked.Increment(ref calls) >= 2, interval: TimeSpan.FromMilliseconds(1));

            calls.Should().Be(2);
        }

        [Fact]
        public async Task PollUntilAbortsAtOnceWhenTheConditionThrows()
        {
            var calls = 0;
            var stopwatch = Stopwatch.StartNew();

            Func<string> condition = () =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("condition broke");
            };

            Func<Task> act = () => UtsWait.PollUntil(condition, timeout: TimeSpan.FromSeconds(10));

            var thrown = await act.Should().ThrowAsync<UtsAssert.UtsHelperException>();
            thrown.Which.Message.Should().Contain("aborted");
            thrown.Which.Cause.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("condition broke");
            calls.Should().Be(1, "an error aborts the poll instead of being retried");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "it doesn't wait for the timeout");
        }

        [Fact]
        public async Task PollUntilTimesOutAndReportsTheCallersLine()
        {
            int line = 0;
            var stopwatch = Stopwatch.StartNew();

            Func<Task> act = () =>
            {
                line = NextLine() + 1;
                return UtsWait.PollUntil(() => false, timeout: Short, description: "never true");
            };

            var thrown = await act.Should().ThrowAsync<XunitException>();
            thrown.Which.Message.Should().StartWith($"UtsWaitTests.cs:{line}: ").And.Contain("never true");
            stopwatch.Elapsed.Should().BeGreaterOrEqualTo(Short - TimeSpan.FromMilliseconds(20));
        }

        [Fact]
        public async Task PollUntilSuccessRetriesErrorsUntilTheConditionSucceeds()
        {
            var calls = 0;

            var value = await UtsWait.PollUntilSuccess(
                () =>
                {
                    if (Interlocked.Increment(ref calls) < 3)
                    {
                        throw new InvalidOperationException("not ready " + calls);
                    }

                    return Task.FromResult("ready");
                },
                interval: TimeSpan.FromMilliseconds(1));

            value.Should().Be("ready");
            calls.Should().Be(3);
        }

        [Fact]
        public async Task PollUntilSuccessRethrowsTheLastErrorOnTimeout()
        {
            var calls = 0;

            Func<Task> act = () => UtsWait.PollUntilSuccess<string>(
                () => throw new InvalidOperationException("failure " + Interlocked.Increment(ref calls)),
                timeout: Short,
                interval: TimeSpan.FromMilliseconds(10));

            var thrown = await act.Should().ThrowAsync<UtsAssert.UtsHelperException>();
            calls.Should().BeGreaterThan(1, "it kept polling through the errors");
            thrown.Which.Cause.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Be("failure " + calls, "the cause is the most recent error, not the first");
            thrown.Which.Message.Should().Contain("timed out");
        }

        [Fact]
        public async Task WithTimeoutReturnsTheResultOfATaskThatCompletes()
        {
            var result = await UtsWait.WithTimeout(Task.FromResult(42), Short, "an answer");

            result.Should().Be(42);
        }

        [Fact]
        public async Task WithTimeoutBoundsAWaitInRealTimeWhileAFakeClockIsInstalled()
        {
            var clock = new FakeClock();
            var start = clock.Now;
            var neverOnItsOwn = ((ITimerScheduler)clock).Delay(TimeSpan.FromHours(1));
            int line = 0;
            var stopwatch = Stopwatch.StartNew();

            Func<Task> act = () =>
            {
                line = NextLine() + 1;
                return UtsWait.WithTimeout(neverOnItsOwn, Short, "a fake-clock delay nobody advances");
            };

            var thrown = await act.Should().ThrowAsync<XunitException>();
            stopwatch.Elapsed.Should().BeGreaterOrEqualTo(Short - TimeSpan.FromMilliseconds(20)).And.BeLessThan(TimeSpan.FromSeconds(5));
            thrown.Which.Message.Should().StartWith($"UtsWaitTests.cs:{line}: ").And.Contain("a fake-clock delay nobody advances");
            neverOnItsOwn.IsCompleted.Should().BeFalse("the fake clock didn't move");
            clock.Now.Should().Be(start, "the bound is measured on the real clock, not the fake one");
        }

        [UtsFact]
        public async Task DrainOrParkedReturnsTrueWhenTheChecksGoIdle()
        {
            var reads = 0;
            var checks = new List<Func<bool>> { () => Interlocked.Increment(ref reads) > 3 };

            var idle = await UtsWait.DrainOrParked(checks, new FakeClock(), TimeSpan.FromSeconds(5), UtsWait.DefaultTimeout, "drain", string.Empty, 0);

            idle.Should().BeTrue();
        }

        [UtsFact]
        public async Task DrainOrParkedReturnsFalseWithinTheStallWindowWhenBusyOnAFreshDelay()
        {
            var clock = new FakeClock();
            _ = ((ITimerScheduler)clock).Delay(TimeSpan.FromSeconds(10));
            var stall = TimeSpan.FromMilliseconds(200);
            var checks = new List<Func<bool>> { () => false };
            var stopwatch = Stopwatch.StartNew();

            var idle = await UtsWait.DrainOrParked(checks, clock, stall, UtsWait.DefaultTimeout, "drain", string.Empty, 0);

            idle.Should().BeFalse("a queue busy since before the delay started is parked on it, not failed");
            stopwatch.Elapsed.Should().BeGreaterOrEqualTo(stall - TimeSpan.FromMilliseconds(20)).And.BeLessThan(TimeSpan.FromSeconds(5));
        }

        [UtsFact]
        public async Task DrainOrParkedDoesNotTreatABusyQueueAsParkedOnADelayThatWasPendingWhileIdle()
        {
            // F1: the heartbeat monitor keeps a fake delay pending for the client's whole life. A queue that is
            // merely slow (JIT, GC, CPU contention) while only such an old delay, or a Schedule timer, is pending
            // must not read as parked, or an advance would fire timers under a command that is still running.
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var busy = false;
            var checks = new List<Func<bool>> { () => !Volatile.Read(ref busy) };
            _ = scheduler.Delay(TimeSpan.FromSeconds(10));
            (await UtsWait.DrainOrParked(checks, clock, TimeSpan.FromMilliseconds(100), UtsWait.DefaultTimeout, "drain", string.Empty, 0))
                .Should().BeTrue("everything was idle with the old delay pending");

            Volatile.Write(ref busy, true);
            scheduler.Schedule(TimeSpan.FromSeconds(1), () => { });
            var bound = TimeSpan.FromMilliseconds(800);
            var stopwatch = Stopwatch.StartNew();
            var line = NextLine() + 1;
            Func<Task> act = () => UtsWait.DrainOrParked(checks, clock, TimeSpan.FromMilliseconds(100), bound, "drain", "UtsWaitTests.cs", line);

            var thrown = await act.Should().ThrowAsync<XunitException>();
            thrown.Which.Message.Should().StartWith($"UtsWaitTests.cs:{line}: drain: ").And.Contain("no fake delay started since they were last idle");
            stopwatch.Elapsed.Should().BeGreaterOrEqualTo(bound - TimeSpan.FromMilliseconds(20), "busy far past the stall window, never parked");
            clock.PendingCount.Should().Be(2);
        }

        [UtsFact]
        public async Task DrainOrParkedTreatsADelayStartedDuringTheBusyStreakAsParked()
        {
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var busy = false;
            var checks = new List<Func<bool>> { () => !Volatile.Read(ref busy) };
            _ = scheduler.Delay(TimeSpan.FromSeconds(10));
            (await UtsWait.DrainOrParked(checks, clock, TimeSpan.FromMilliseconds(100), UtsWait.DefaultTimeout, "drain", string.Empty, 0)).Should().BeTrue();

            Volatile.Write(ref busy, true);
            var stall = TimeSpan.FromMilliseconds(200);
            var stopwatch = Stopwatch.StartNew();
            var drain = UtsWait.DrainOrParked(checks, clock, stall, UtsWait.DefaultTimeout, "drain", string.Empty, 0);

            // The busy work now starts the delay it will await (a TimeoutAfter, say), mid-streak.
            await Task.Delay(400);
            drain.IsCompleted.Should().BeFalse("only the old delay was pending, so the busy queue wasn't parked");
            _ = scheduler.Delay(TimeSpan.FromSeconds(10));

            (await UtsWait.WithTimeout(drain, TimeSpan.FromSeconds(5), "the drain to see the queue parked")).Should().BeFalse();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        }

        [UtsFact]
        public async Task DrainOrParkedFailsWhenBusyWithNoDelayPending()
        {
            var clock = new FakeClock();
            ((ITimerScheduler)clock).Schedule(TimeSpan.FromSeconds(1), () => { });
            var checks = new List<Func<bool>> { () => false };

            Func<Task> act = () => UtsWait.DrainOrParked(checks, clock, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), "ADVANCE_TIME", string.Empty, 0);

            var thrown = await act.Should().ThrowAsync<XunitException>();
            thrown.Which.Message.Should().Contain("ADVANCE_TIME: the SDK's queues didn't empty").And.Contain("no fake delay started since");
        }

        [Fact]
        public void UtsFactAndUtsTheoryCarryTheDefaultTestTimeout()
        {
            new UtsFactAttribute().Timeout.Should().Be(UtsTestTimeouts.DefaultTestTimeoutMs);
            new UtsTheoryAttribute().Timeout.Should().Be(UtsTestTimeouts.DefaultTestTimeoutMs);
        }

        private static int NextLine([CallerLineNumber] int line = 0) => line;
    }
}
