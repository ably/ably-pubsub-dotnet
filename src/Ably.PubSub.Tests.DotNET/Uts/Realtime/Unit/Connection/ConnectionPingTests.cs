using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/connection_ping_test.md in ably/specification.
    ///
    /// Spec points: RTN13, RTN13a, RTN13b, RTN13c, RTN13d, RTN13e
    ///
    /// Translation notes:
    /// - <c>Connection.PingAsync()</c> hands back a <c>Result</c> rather than throwing, so the specs'
    ///   <c>AWAIT_ERROR ping_future</c> becomes an assertion on <c>IsFailure</c> and <c>Error</c>, and
    ///   their <c>duration</c> is <c>Result.Value</c>, a nullable TimeSpan.
    /// - The specs' <c>SCHEDULE_AFTER(100ms): conn.respond_with_success(...)</c> exists only to leave the
    ///   client in CONNECTING long enough to call <c>ping()</c>. Here the attempt is simply withheld -
    ///   claimed with <c>AwaitConnectionAttempt()</c> and answered when the test is ready - which is the
    ///   same premise without a real delay.
    /// - <c>enable_fake_timers()</c> / <c>ADVANCE_TIME</c> over a *scheduled* wait has no seam in this
    ///   SDK, so the ping deadline is driven by shortening <c>RealtimeRequestTimeout</c> and the retry
    ///   delay by shortening <c>DisconnectedRetryTimeout</c>. Where the elapsing is *measured* -
    ///   <c>connectionStateTtl</c> in the two SUSPENDED scenarios - a <see cref="TestClock"/> advance
    ///   does reach it, because <c>AttemptsHelpers.ShouldSuspend</c> reads <c>ClientOptions.NowFunc</c>.
    /// - The specs' <c>CLOSE_CLIENT(client)</c> is <see cref="UtsTestBase"/>'s teardown, which disposes
    ///   every client it handed out, including when the test throws.
    /// - RTN13d is not implemented by this SDK: <c>RealtimeWorkflow.HandlePingCommand</c> fails any ping
    ///   whose connection state is not CONNECTED instead of deferring it. The three tests whose premise
    ///   is the deferral carry the spec-correct assertions under <see cref="DeviationFactAttribute"/>;
    ///   the two scenarios that only assert "an error" are satisfied by the immediate failure and run as
    ///   ordinary facts.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionPingTests : UtsTestBase
    {
        public ConnectionPingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN13a/ping-heartbeat-roundtrip-0
        [Fact]
        public async Task RTN13a_PingHeartbeatRoundtrip()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));
            mockWs.OnMessageFromClient = EchoHeartbeat(mockWs);

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.HasValue.Should().BeTrue();
            result.Value.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);

            HeartbeatsSentBy(mockWs).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN13e/heartbeat-random-id-0
        [Fact]
        public async Task RTN13e_HeartbeatRandomId()
        {
            string capturedHeartbeatId = null;

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg == null || msg.Action != ProtocolMessage.MessageAction.Heartbeat)
                {
                    return;
                }

                capturedHeartbeatId = msg.Id;

                // A HEARTBEAT carrying a different id must be ignored ...
                mockWs.SendToClient(HeartbeatMessage("wrong-id"));

                // ... and only the matching one resolves the ping.
                mockWs.SendToClient(HeartbeatMessage(msg.Id));
            };

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.HasValue.Should().BeTrue();
            result.Value.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);

            capturedHeartbeatId.Should().NotBeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN13e/no-id-heartbeat-ignored-1
        [Fact]
        public async Task RTN13e_NoIdHeartbeatIgnored()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg == null || msg.Action != ProtocolMessage.MessageAction.Heartbeat)
                {
                    return;
                }

                // A server-initiated HEARTBEAT has no id and is not a ping response ...
                mockWs.SendToClient(ProtocolMessages.HeartbeatMessage());

                // ... the one that is carries the ping's id.
                mockWs.SendToClient(HeartbeatMessage(msg.Id));
            };

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.HasValue.Should().BeTrue();
            result.Value.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
        }

        // UTS: realtime/unit/RTN13e/concurrent-pings-unique-ids-2
        [Fact]
        public async Task RTN13e_ConcurrentPingsUniqueIds()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));
            mockWs.OnMessageFromClient = EchoHeartbeat(mockWs);

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var firstPing = client.Connection.PingAsync();
            var secondPing = client.Connection.PingAsync();

            var firstResult = await firstPing;
            var secondResult = await secondPing;

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();
            firstResult.Value.HasValue.Should().BeTrue();
            secondResult.Value.HasValue.Should().BeTrue();

            var heartbeats = HeartbeatsSentBy(mockWs);
            heartbeats.Should().HaveCount(2);
            heartbeats[0].Id.Should().NotBe(heartbeats[1].Id);
        }

        // UTS: realtime/unit/RTN13c/ping-timeout-0
        [Fact]
        public async Task RTN13c_PingTimeout()
        {
            // No OnMessageFromClient handler: the server never answers the HEARTBEAT.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));

            // The spec pairs realtimeRequestTimeout: 2000 with ADVANCE_TIME(2100). The ping deadline is
            // a scheduled wait with no seam here - RealtimeWorkflow.HandlePingCommand queues a
            // DelayCommand for ConnectionManager.DefaultTimeout, which is this option - so it is
            // shortened to a real 500ms rather than advanced.
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await client.Connection.PingAsync();

            result.IsFailure.Should().BeTrue();
            AssertTimeoutError(result.Error);

            HeartbeatsSentBy(mockWs).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN13b/ping-error-initialized-0
        [Fact]
        public async Task RTN13b_PingErrorInitialized()
        {
            // The spec installs no mock here; one is still needed to build a client, and with
            // AutoConnect off - the unit-tier default - its handler is never reached.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            var result = await client.Connection.PingAsync();

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13b/ping-error-suspended-1
        [Fact]
        public async Task RTN13b_PingErrorSuspended()
        {
            var clock = new TestClock();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());

            // fallbackHosts: [] is already the unit-tier default, so the spec's value is not restated.
            var client = RealtimeClient(
                mockWs,
                clock: clock,
                configure: options =>
                {
                    options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000);
                    options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
                });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            // The spec's ADVANCE_TIME(121s) past connectionStateTtl. This one is reachable with the test
            // clock: AttemptsHelpers.ShouldSuspend compares Now() against the first attempt, and the
            // decision is taken when the next attempt fails - which the real 1s retry timer provokes.
            clock.Advance(TimeSpan.FromSeconds(121));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(10));

            var result = await client.Connection.PingAsync();

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13b/ping-error-closed-2
        [Fact]
        public async Task RTN13b_PingErrorClosed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Close() does not close the socket by itself: it sends CLOSE and waits for the server's
            // CLOSED, so the spec's AWAIT client.close() needs the server half answered here.
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            var result = await client.Connection.PingAsync();

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13b/ping-error-failed-3
        [Fact]
        public async Task RTN13b_PingErrorFailed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithError(
                    ProtocolMessages.ErrorMessage(80000, "Fatal error", 400)));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            var result = await client.Connection.PingAsync();

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13d/ping-deferred-connecting-0
        [DeviationFact]
        public async Task RTN13d_PingDeferredConnecting()
        {
            // The handler withholds its answer so the client stays in CONNECTING; the attempt is then
            // claimed below and answered once ping() has been called.
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => { });
            mockWs.OnMessageFromClient = EchoHeartbeat(mockWs);

            var client = RealtimeClient(mockWs);

            var pendingAttempt = mockWs.AwaitConnectionAttempt();
            client.Connect();
            var attempt = await pendingAttempt;

            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // NOTE: the ping is issued while CONNECTING, but the workflow consumes its command on its own
            // loop, so "processed while CONNECTING" rests on the CONNECTED answer coming after this line
            // rather than on a barrier the SDK exposes.
            var ping = client.Connection.PingAsync();

            attempt.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage(
                    connectionId: "conn-id-1",
                    connectionKey: "conn-key-1"));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await ping;

            result.IsSuccess.Should().BeTrue();
            result.Value.HasValue.Should().BeTrue();
            result.Value.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);

            HeartbeatsSentBy(mockWs).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN13d/ping-deferred-disconnected-1
        [DeviationFact]
        public async Task RTN13d_PingDeferredDisconnected()
        {
            // NOTE: the spec answers every attempt with success, but RTN15a retries a drop out of
            // CONNECTED immediately, so that setup leaves no DISCONNECTED window to call ping() in. The
            // immediate retry is refused instead, which parks the client in DISCONNECTED for
            // disconnectedRetryTimeout; the attempt after that succeeds, as the spec intends.
            var attempts = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts++;
                if (attempts == 1)
                {
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "conn-id-1",
                            connectionKey: "conn-key-1"));
                }
                else if (attempts == 2)
                {
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "conn-id-2",
                            connectionKey: "conn-key-2"));
                }
            });
            mockWs.OnMessageFromClient = EchoHeartbeat(mockWs);

            var client = RealtimeClient(
                mockWs,
                configure: options => options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2
                      && client.Connection.State == ConnectionState.Disconnected,
                "the RTN15a immediate retry to fail and the connection to settle in DISCONNECTED");

            var ping = client.Connection.PingAsync();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            var result = await ping;

            result.IsSuccess.Should().BeTrue();
            result.Value.HasValue.Should().BeTrue();
            result.Value.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);

            HeartbeatsSentBy(mockWs).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN13b/deferred-ping-error-failed-4
        [Fact]
        public async Task RTN13b_DeferredPingErrorFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => { });

            var client = RealtimeClient(mockWs);

            var pendingAttempt = mockWs.AwaitConnectionAttempt();
            client.Connect();
            var attempt = await pendingAttempt;

            client.Connection.State.Should().Be(ConnectionState.Connecting);

            var ping = client.Connection.PingAsync();

            attempt.RespondWithError(ProtocolMessages.ErrorMessage(80000, "Fatal error", 400));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            var result = await ping;

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13b/deferred-ping-error-suspended-5
        [Fact]
        public async Task RTN13b_DeferredPingErrorSuspended()
        {
            var clock = new TestClock();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());

            var client = RealtimeClient(
                mockWs,
                clock: clock,
                configure: options =>
                {
                    options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000);
                    options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
                });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            var ping = client.Connection.PingAsync();

            clock.Advance(TimeSpan.FromSeconds(121));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(10));

            var result = await ping;

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN13c/deferred-ping-timeout-1
        [DeviationFact]
        public async Task RTN13c_DeferredPingTimeout()
        {
            // No OnMessageFromClient handler: the server never answers the HEARTBEAT.
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => { });

            // realtimeRequestTimeout shortened from the spec's 2000 for the same reason as in
            // RTN13c_PingTimeout - the ping deadline is scheduled, not measured.
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500));

            var pendingAttempt = mockWs.AwaitConnectionAttempt();
            client.Connect();
            var attempt = await pendingAttempt;

            client.Connection.State.Should().Be(ConnectionState.Connecting);

            var ping = client.Connection.PingAsync();

            attempt.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage(
                    connectionId: "conn-id-1",
                    connectionKey: "conn-key-1"));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var result = await ping;

            result.IsFailure.Should().BeTrue();
            AssertTimeoutError(result.Error);

            // Beyond the spec's listed assertions, but the point of this scenario is that the deadline
            // starts when the HEARTBEAT is sent. Without it the test would also pass on a client that
            // never deferred and failed the ping outright.
            HeartbeatsSentBy(mockWs).Should().HaveCount(1);
        }

        private static Action<ProtocolMessage> EchoHeartbeat(MockWebSocket mockWs)
            => msg =>
            {
                if (msg != null && msg.Action == ProtocolMessage.MessageAction.Heartbeat)
                {
                    mockWs.SendToClient(HeartbeatMessage(msg.Id));
                }
            };

        private static JObject HeartbeatMessage(string id)
            => new JObject { ["action"] = ProtocolMessages.Heartbeat, ["id"] = id };

        private static List<ProtocolMessage> HeartbeatsSentBy(MockWebSocket mockWs)
            => mockWs.MessagesFromClient
                .Where(m => m != null && m.Action == ProtocolMessage.MessageAction.Heartbeat)
                .ToList();

        private static void AssertTimeoutError(ErrorInfo error)
        {
            error.Should().NotBeNull();

            // RTN13c asserts that the message contains "timeout", case-insensitively. This SDK's
            // PingRequest.TimeOutError reads "Unable to ping service; Request timed out" - the same
            // meaning in the other English spelling - so both are accepted rather than pinning the test
            // to one library's wording. The other failure reachable here, PingRequest.DefaultError
            // ("not connected"), matches neither, so the assertion still tells the two apart.
            var message = error.Message ?? string.Empty;
            var readsAsTimeout = message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                                 || message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;

            readsAsTimeout.Should().BeTrue(
                "RTN13c requires the failure to be reported as a timeout; the message was: " + message);
        }
    }
}
