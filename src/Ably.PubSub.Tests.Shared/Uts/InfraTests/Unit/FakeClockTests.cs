using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Transport.States.Connection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>
    /// Self-tests of <see cref="FakeClock"/> and its wiring through hook H1 (<c>ClientOptions.TimerScheduler</c>):
    /// cascades run to quiescence in one advance, SDK timers and <c>TimeoutAfter</c> waits only fire on an
    /// advance, real-time poll deadlines still expire, and the hook is per client.
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class FakeClockTests : UtsUnitTestBase
    {
        /// <summary>A real pause longer than every SDK timeout these tests set, so a real-clock timer would have fired.</summary>
        private static readonly TimeSpan RealPause = TimeSpan.FromMilliseconds(600);

        private static readonly TimeSpan SdkTimeout = TimeSpan.FromMilliseconds(200);

        public FakeClockTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task OneAdvanceRunsCascadedCallbacksToQuiescence()
        {
            var clock = new FakeClock();
            var start = clock.Now;
            ITimerScheduler scheduler = clock;
            var log = new CaptureList<string>();

            scheduler.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                log.Add("a@" + (clock.Now - start).TotalMilliseconds);
                scheduler.Schedule(TimeSpan.Zero, () =>
                {
                    log.Add("b@" + (clock.Now - start).TotalMilliseconds);
                    scheduler.Schedule(TimeSpan.FromMilliseconds(50), () => log.Add("c@" + (clock.Now - start).TotalMilliseconds));
                    scheduler.Schedule(TimeSpan.FromMilliseconds(500), () => log.Add("too late"));
                });
            });

            await clock.AdvanceAsync(200);

            log.Should().Equal("a@100", "b@100", "c@150");
            clock.Now.Should().Be(start + TimeSpan.FromMilliseconds(200));
            clock.PendingCount.Should().Be(1, "a timer created mid-advance but due after it waits for the next advance");
        }

        [Fact]
        public async Task OneAdvanceRunsAnAsyncChainOfDelaysToQuiescence()
        {
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var log = new CaptureList<string>();

            async Task Chain()
            {
                await scheduler.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
                log.Add("first");
                await scheduler.Delay(TimeSpan.Zero).ConfigureAwait(false);
                log.Add("zero-delay");
                await scheduler.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
                log.Add("second");
            }

            var chain = Chain();
            clock.PendingCount.Should().Be(1);

            await clock.AdvanceAsync(50);

            log.Should().Equal(new[] { "first", "zero-delay", "second" }, "each delay's continuation ran inside the advance and its next delay was seen by it");
            chain.IsCompleted.Should().BeTrue();
            clock.PendingCount.Should().Be(0);
        }

        [Fact]
        public async Task OneAdvanceRunsTheSdksCascadedTimers()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket();
            InstallMock(mock);
            var options = new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] };
            var client = Realtime(options);
            var states = UtsRecord.ConnectionStates(client.Connection);
            await mock.AwaitConnectionAttempt();

            // Nothing answers. One advance: attempt 1's connect timer (RealtimeRequestTimeout) fires, DISCONNECTED
            // retries at once (RTN17j), attempt 2 starts a new connect timer mid-advance, and that timer fires
            // inside the same advance too, ending in DISCONNECTED with an RTB1 retry timer pending.
            await AdvanceTime(options.RealtimeRequestTimeout + options.RealtimeRequestTimeout + TimeSpan.FromMilliseconds(100));

            mock.ConnectionAttempts.Should().HaveCount(2);
            (mock.ConnectionAttempts[1].Timestamp - mock.ConnectionAttempts[0].Timestamp).Should().Be(
                options.RealtimeRequestTimeout,
                "attempt 2 was made when attempt 1's timer fired, mid-advance");
            UtsAssert.ContainsInOrder(states, new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Disconnected });
            client.Connection.State.Should().Be(ConnectionState.Disconnected, "the timer created mid-advance fired within it");
        }

        [Fact]
        public async Task SdkConnectTimerFiresOnlyWhenTheFakeClockAdvances()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket();
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0], RealtimeRequestTimeout = SdkTimeout });
            await mock.AwaitConnectionAttempt();

            await Task.Delay(RealPause);
            await ProcessPendingEvents();
            client.Connection.State.Should().Be(ConnectionState.Connecting, "the connect timer isn't on the real clock");

            var states = UtsRecord.ConnectionStates(client.Connection);

            await AdvanceTime(SdkTimeout);

            states.Should().Contain(ConnectionState.Disconnected, "the connect timer fired once the fake clock passed it");
            mock.ConnectionAttempts.Should().HaveCount(2, "and the SDK's instant retry followed");
        }

        [Fact]
        public async Task SdkTimeoutAfterWaitFiresOnlyWhenTheFakeClockAdvances()
        {
            EnableFakeTimers();
            const string name = "publish-without-ack";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.AttachedMessage(name));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, RealtimeRequestTimeout = SdkTimeout });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            var publish = channel.PublishAsync("event", "never acknowledged");
            await mock.AwaitClientMessage(ProtocolMessages.Message);
            await Task.Delay(RealPause);
            await ProcessPendingEvents();
            publish.IsCompleted.Should().BeFalse("PublishAsync's TimeoutAfter isn't on the real clock");

            await AdvanceTime(SdkTimeout);

            var result = await UtsWait.WithTimeout(publish, TimeSpan.FromSeconds(5), "the publish to time out on the fake clock");
            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("timeout");
        }

        [UtsFact]
        public async Task AdvanceFiresATimerWhileARegisteredQueueStaysBusyUntilItFires()
        {
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var fired = false;
            clock.AddIdleCheck(() => fired);
            var work = Task.Run(async () =>
            {
                await scheduler.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                fired = true;
            });
            await UtsWait.PollUntil(() => clock.PendingCount > 0, description: "the delay to be scheduled");

            await clock.AdvanceAsync(11000);

            await UtsWait.WithTimeout(work, TimeSpan.FromSeconds(5), "the awaited work to finish");
            fired.Should().BeTrue();
        }

        [UtsFact]
        public async Task AdvanceTimeUnblocksAnAuthCallbackThatNeverReturns()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var neverReturns = new TaskCompletionSource<object>();
            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams => neverReturns.Task,
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromSeconds(10),
            });
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);
            client.Connect();

            // The workflow is parked on the callback's fake-clock TimeoutAfter: process_pending_events settles
            // (F3) instead of failing after 10 s, and nothing has happened yet.
            await ProcessPendingEvents();
            client.Connection.State.Should().Be(ConnectionState.Connecting);
            changes.Should().NotContain(c => c.Current == ConnectionState.Disconnected);

            await AdvanceTime(11000);

            await UtsWait.PollUntil(
                () => changes.Any(c => c.Current == ConnectionState.Disconnected),
                description: "DISCONNECTED after the auth callback timed out");
            changes.First(c => c.Current == ConnectionState.Disconnected).Reason.Code.Should().Be(80019);

            // Release the callback so the SDK's instant reconnect can't leave the workflow blocked at teardown.
            neverReturns.TrySetCanceled();
        }

        [UtsFact]
        public async Task AdvanceWaitsForAQueueThatIsSlowWhileOnlyAnOldDelayIsPending()
        {
            // F1: a slow handler (400 ms, standing in for a JIT or GC stall) with only a delay that was already
            // pending at an idle moment (the heartbeat monitor's) is busy, not parked: no timer fires under it.
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var busy = false;
            clock.AddIdleCheck(() => !Volatile.Read(ref busy));
            _ = scheduler.Delay(TimeSpan.FromMinutes(1));
            await clock.AdvanceAsync(0);

            var firedWhileBusy = new CaptureList<bool>();
            scheduler.Schedule(TimeSpan.FromMilliseconds(100), () => firedWhileBusy.Add(Volatile.Read(ref busy)));
            Volatile.Write(ref busy, true);
            var slowHandler = Task.Run(async () =>
            {
                await Task.Delay(400).ConfigureAwait(false);
                Volatile.Write(ref busy, false);
            });

            await clock.AdvanceAsync(200);

            await slowHandler;
            firedWhileBusy.Should().Equal(new[] { false }, "the timer fired once, after the slow handler finished");
        }

        [UtsFact]
        public async Task ProcessPendingEventsSettlesWhileAWorkflowIsParkedOnAFreshFakeDelay()
        {
            // F3: with a fake clock, process_pending_events uses the advance's parked-aware drain.
            var clock = EnableFakeTimers();
            ITimerScheduler scheduler = clock;
            var parked = new TaskCompletionSource<bool>();
            clock.AddIdleCheck(() => parked.Task.IsCompleted);
            var work = Task.Run(async () =>
            {
                await scheduler.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                parked.TrySetResult(true);
            });
            await UtsWait.PollUntil(() => clock.PendingCount > 0, description: "the delay to be scheduled");
            var stopwatch = Stopwatch.StartNew();

            await ProcessPendingEvents();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "parked, not failed at the 10 s bound");
            work.IsCompleted.Should().BeFalse("process_pending_events doesn't move fake time");
            await AdvanceTime(5000);
            await UtsWait.WithTimeout(work, TimeSpan.FromSeconds(5), "the parked work to finish");
        }

        [UtsFact]
        public async Task AnInfiniteScheduleNeverFires()
        {
            var clock = new FakeClock();
            ITimerScheduler scheduler = clock;
            var fired = new CaptureList<string>();

            var handle = scheduler.Schedule(Timeout.InfiniteTimeSpan, () => fired.Add("schedule"));
            clock.ScheduleAfter(Timeout.InfiniteTimeSpan, () => fired.Add("schedule-after"));
            var delay = scheduler.Delay(Timeout.InfiniteTimeSpan);
            scheduler.Schedule(TimeSpan.FromMilliseconds(-5), () => fired.Add("negative"));

            clock.PendingCount.Should().Be(1, "infinite means never, as System.Threading.Timer's -1; only the negative delay is queued");
            await clock.AdvanceAsync(TimeSpan.FromDays(1));

            fired.Should().Equal(new[] { "negative" }, "a negative (non-infinite) delay is due at once");
            delay.IsCompleted.Should().BeFalse();
            handle.Dispose();
        }

        [UtsFact]
        public async Task ACallbackThatThrowsDoesNotStopTheAdvance()
        {
            var clock = new FakeClock();
            var start = clock.Now;
            ITimerScheduler scheduler = clock;
            var fired = new CaptureList<string>();
            scheduler.Schedule(TimeSpan.FromMilliseconds(10), () => throw new InvalidOperationException("boom"));
            scheduler.Schedule(TimeSpan.FromMilliseconds(20), () => fired.Add("after"));

            Func<Task> act = () => clock.AdvanceAsync(50);

            var thrown = await act.Should().ThrowAsync<UtsAssert.UtsHelperException>();
            thrown.Which.Cause.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
            thrown.Which.Message.Should().Contain("ADVANCE_TIME: 1 fake-clock callback(s) threw");
            fired.Should().Equal(new[] { "after" }, "the timers after the throwing one still fired");
            clock.Now.Should().Be(start + TimeSpan.FromMilliseconds(50));
            clock.CallbackErrors.Should().ContainSingle();

            await clock.AdvanceAsync(10);
        }

        [Fact]
        public async Task PollDeadlineStillExpiresWithTheFakeClockInstalled()
        {
            var clock = EnableFakeTimers();
            var mock = new MockWebSocket();
            InstallMock(mock);
            Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var fakeStart = clock.Now;
            var stopwatch = Stopwatch.StartNew();

            Func<Task> act = () => UtsWait.PollUntil(() => false, timeout: TimeSpan.FromMilliseconds(300));

            await act.Should().ThrowAsync<XunitException>().WithMessage("*timed out*");
            stopwatch.Elapsed.Should().BeGreaterOrEqualTo(TimeSpan.FromMilliseconds(280)).And.BeLessThan(TimeSpan.FromSeconds(5));
            clock.Now.Should().Be(fakeStart, "the poll's deadline is real time; it neither needs nor moves the fake clock");
        }

        [Fact]
        public async Task TimerHookIsPerClientAndAClientBuiltWithoutItKeepsTheRealClock()
        {
            EnableFakeTimers();
            var fakeMock = new MockWebSocket();
            InstallMock(fakeMock);
            var faked = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0], RealtimeRequestTimeout = SdkTimeout });
            await fakeMock.AwaitConnectionAttempt();
            var pendingWithOneClient = Clock.PendingCount;

            // Built afterwards straight from the core constructor, without the unit factory and its hook.
            var realMock = new MockWebSocket();
            var realOptions = new ClientOptions
            {
                Key = ValidKey,
                FallbackHosts = new string[0],
                RealtimeRequestTimeout = SdkTimeout,
                TransportFactory = realMock,
                HttpClient = MockHttpClient.Strict().AsHttpClient(),
                AutomaticNetworkStateMonitoring = false,
            };
            var real = Track(new PubSubRealtimeClient(realOptions, new Ably.PubSub.Tests.Push.FakeMobileDevice()));
            await realMock.AwaitConnectionAttempt();

            real.Options.TimerScheduler.Should().BeSameAs(SystemTimerScheduler.Instance);
            faked.Options.TimerScheduler.Should().NotBeSameAs(SystemTimerScheduler.Instance);

            // The real client's connect timer fires on the real clock; the faked one's doesn't.
            await UtsWait.AwaitConnectionState(real.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));
            await ProcessPendingEvents();
            faked.Connection.State.Should().Be(ConnectionState.Connecting);
            Clock.PendingCount.Should().Be(pendingWithOneClient, "the second client put no timer on the fake clock");

            var fakedStates = UtsRecord.ConnectionStates(faked.Connection);
            await AdvanceTime(SdkTimeout);
            fakedStates.Should().Contain(ConnectionState.Disconnected, "the faked client's timer fires on the advance");
        }
    }
}
