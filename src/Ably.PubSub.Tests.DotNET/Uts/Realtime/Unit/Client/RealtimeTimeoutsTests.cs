using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Client
{
    /// <summary>
    /// Derived from uts/realtime/unit/client/realtime_timeouts.md in ably/specification.
    ///
    /// Spec points: RTC7 (and, through it, TO3l1, TO3l2, TO3l11, RTL4f, RTL5f)
    ///
    /// Translation notes:
    /// - The specs' <c>enable_fake_timers()</c> / <c>ADVANCE_TIME</c> drives waits the SDK *schedules*,
    ///   and there is no timer seam: every state and every channel awaiter builds its own
    ///   <c>CountdownTimer</c> inline. So each deadline is driven by the real option the spec is about -
    ///   which is the point of RTC7 - and the waits are short by construction.
    /// - <c>channel.attach()</c> / <c>channel.detach()</c> are <c>AttachAsync()</c> / <c>DetachAsync()</c>
    ///   and hand back a <c>Result</c> rather than throwing, so the specs'
    ///   <c>AWAIT attach_future FAILS WITH error</c> becomes <c>IsFailure</c> plus <c>Error</c>.
    /// - <c>client.options</c> is <c>PubSubRealtimeClient.Options</c>, internal and reachable through
    ///   <c>InternalsVisibleTo</c>; the values are <c>TimeSpan</c> rather than milliseconds.
    /// - The specs' <c>CLOSE_CLIENT(client)</c> is <see cref="UtsTestBase"/>'s teardown.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimeTimeoutsTests : UtsTestBase
    {
        public RealtimeTimeoutsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC7/attach-request-timeout-0
        [Fact]
        public async Task RTC7_AttachRequestTimeout()
        {
            var channelName = "test-RTC7-attach-" + UtsSandbox.RandomId();

            // No OnMessageFromClient handler, which is the spec's `PASS` on ATTACH: the server never
            // answers, so the attach has to hit the configured deadline.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key",
                        connectionStateTtl: 120000,
                        maxIdleInterval: 15000)));

            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500));

            var channel = client.Channels.Get(channelName);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec's ADVANCE_TIME(600) over a 500ms deadline. ChannelAwaiter.StartWait arms a real
            // CountdownTimer with ConnectionManager.Options.RealtimeRequestTimeout and there is nothing
            // to advance, so the 500ms is simply waited out.
            var result = await channel.AttachAsync();

            result.IsFailure.Should().BeTrue(
                "the custom 500ms realtimeRequestTimeout must apply, not the 10000ms default");
            result.Error.Should().NotBeNull();

            // RTL4f - an attach timeout suspends the channel.
            channel.State.Should().Be(ChannelState.Suspended);

            var attachMessages = await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);
            attachMessages.Should().NotBeEmpty("the attach must have reached the wire and gone unanswered");
        }

        // UTS: realtime/unit/RTC7/detach-request-timeout-1
        [Fact]
        public async Task RTC7_DetachRequestTimeout()
        {
            var channelName = "test-RTC7-detach-" + UtsSandbox.RandomId();
            var ignoreDetach = false;

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key",
                        connectionStateTtl: 120000,
                        maxIdleInterval: 15000)));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg == null)
                {
                    return;
                }

                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                        channelName,
                        new Dictionary<string, JToken> { ["flags"] = 0 }));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach && ignoreDetach)
                {
                    // The spec's `PASS`: the DETACHED is withheld so the detach hits the deadline.
                    Output?.WriteLine("Withholding DETACHED for channel " + msg.Channel);
                }
            };

            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500));

            var channel = client.Channels.Get(channelName);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue();

            ignoreDetach = true;

            var result = await channel.DetachAsync();

            result.IsFailure.Should().BeTrue(
                "the custom 500ms realtimeRequestTimeout must apply, not the 10000ms default");
            result.Error.Should().NotBeNull();

            // RTL5f - a detach timeout puts the channel back where it was.
            channel.State.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTC7/disconnected-retry-timeout-2
        [Fact]
        public async Task RTC7_DisconnectedRetryTimeout()
        {
            var attemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "connection-id",
                            connectionKey: "connection-key",
                            connectionStateTtl: 120000,
                            maxIdleInterval: 0));
                }
                else
                {
                    conn.RespondWithRefused();
                }
            });

            // The spec's connectivity-checker guard (RTN17j). SkipInternetCheck is already the unit-tier
            // default, so this only makes doubly sure nothing reaches the network.
            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200, "yes"));

            // NOTE: the spec configures disconnectedRetryTimeout: 2000 and probes at ADVANCE_TIME(1500).
            // With a real timer the RTB1 jitter floor for 2000ms is 1600ms, which leaves 100ms of margin
            // against a 1500ms probe - too little. Shortened to 1000ms, whose floor is 800ms, and probed
            // at 300ms: the same assertion with a usable margin.
            var client = RealtimeClient(
                mockWs,
                mockHttp,
                configure: options => options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            attemptCount.Should().Be(1);

            // Force the disconnection. RTN15a retries immediately, that retry is refused, and only then
            // is the timer-based retry scheduled with disconnectedRetryTimeout.
            mockWs.SimulateDisconnect();

            // Not AwaitConnectionState: DISCONNECTED is entered twice here, and the first entry is
            // already held by the time the immediate retry is in flight, so waiting for the state alone
            // would return before the retry had happened.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2
                      && client.Connection.State == ConnectionState.Disconnected,
                "the RTN15a immediate retry to fail and the connection to settle back in DISCONNECTED");

            // The immediate retry is a *burst*, not a single attempt: RTN17j bounds it by the number
            // of domains left to traverse, so more than one attempt can land before the timer-based
            // retry takes over. Capturing the count mid-burst makes the assertion below race the
            // burst - measured at 2 on one run and 4 on another. Waiting for the count to stop
            // moving is what makes "no retry before the timeout" a statement about the timer rather
            // than about whatever the burst was doing.
            var countAfterImmediate = await SettledAttemptCount(mockWs);

            // The spec's "advance by less than the custom timeout - no new retry yet". Proving the
            // absence of an event is the one thing a poll cannot do, so this is a real, bounded wait
            // rather than a settling delay.
            await Task.Delay(300);
            mockWs.ConnectionAttempts.Count.Should().Be(
                countAfterImmediate,
                "no retry may happen before the configured disconnectedRetryTimeout has elapsed");

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count > countAfterImmediate,
                "a reconnection attempt once the configured disconnectedRetryTimeout had elapsed",
                TimeSpan.FromSeconds(5));

            mockWs.ConnectionAttempts.Count.Should().BeGreaterThan(countAfterImmediate);
        }

        // UTS: realtime/unit/RTC7/default-timeouts-applied-3
        [Fact]
        public void RTC7_DefaultTimeoutsApplied()
        {
            // The spec installs no mock; one is still needed to build a client, and with AutoConnect off
            // - the unit-tier default - its handler is never reached.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);

            client.Options.RealtimeRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
            client.Options.DisconnectedRetryTimeout.Should().Be(TimeSpan.FromMilliseconds(15000));
            client.Options.SuspendedRetryTimeout.Should().Be(TimeSpan.FromMilliseconds(30000));
            client.Options.HttpOpenTimeout.Should().Be(TimeSpan.FromMilliseconds(4000));
            client.Options.HttpRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
        }

        /// <summary>
        /// The connection-attempt count once it has stopped moving. Polls until two consecutive
        /// samples a short interval apart agree, so a retry burst in flight is allowed to finish
        /// before a test measures against it.
        /// </summary>
        private static async Task<int> SettledAttemptCount(MockWebSocket mockWebSocket)
        {
            var previous = -1;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

            while (DateTimeOffset.UtcNow < deadline)
            {
                var current = mockWebSocket.ConnectionAttempts.Count;
                if (current == previous)
                {
                    return current;
                }

                previous = current;
                await Task.Delay(150).ConfigureAwait(false);
            }

            return mockWebSocket.ConnectionAttempts.Count;
        }
    }
}
