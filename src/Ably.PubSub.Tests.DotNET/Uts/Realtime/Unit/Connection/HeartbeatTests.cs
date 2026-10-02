using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/heartbeat_test.md in ably/specification.
    ///
    /// Spec points: RTN23, RTN23a
    ///
    /// <b>Only one of the spec's three platform variants applies here.</b> Its own "Choosing Between
    /// RTN23a and RTN23b" section decides which: a platform whose WebSocket client cannot surface
    /// ping frame events implements RTN23a. .NET's <c>ClientWebSocket</c> answers ping frames inside
    /// the protocol and raises no event, so the seven RTN23a tests are translated and the rest are
    /// not applicable — RTN23b presupposes a ping-frame event or a <c>heartbeats=false</c> transport,
    /// RTN23c is browser-only by the spec's own wording, and RTN23c1 needs protocol actions this SDK
    /// does not define. Those are recorded as Mock Infrastructure Limitations, not deviations.
    ///
    /// <b>Timers stay real here, scaled down.</b> RTN23a is evaluated by a loop that samples every
    /// <c>HeartbeatMonitorDelay</c> milliseconds of real time, so the quantity being measured and the
    /// schedule that samples it have to move together — a clock that moved only <c>Now()</c> would
    /// still be sampled at the unchanged cadence. Every interval in the spec is therefore shortened
    /// rather than faked.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class HeartbeatTests : UtsTestBase
    {
        // How often RTN23a is evaluated. The default is 1000ms, which is coarser than the whole scaled
        // idle window below.
        private const int MonitorTickMs = 20;

        // The pure-timeout tests. The spec uses maxIdleInterval 5000 / realtimeRequestTimeout 2000 for
        // one and 2000 / 1000 for the others. Neither length matters here, because nothing is
        // asserted part-way through the window, so both scale to the same pair.
        private const int TimeoutIdleMs = 400;
        private const int TimeoutRequestMs = 300;

        // The "activity resets the timer" tests, scaled from the spec's 3000 / 1000 and 2000 / 1000 with
        // a deliberately wide margin in both directions. The allowed idle window is 1300ms and each step
        // is 800ms, so at the mid-point assertion 1600ms have passed since CONNECTED — 300ms past the
        // window the connection would have died in had the injected message not reset it — while only
        // 800ms have passed since that message, leaving 500ms of slack for a loaded test host.
        // Widened from the spec's figures. The idle window the SDK actually enforces is
        // maxIdleInterval + realtimeRequestTimeout, and these waits are real elapsed time because
        // there is no timer seam for a scheduled check - so a step that sits close to the boundary
        // trips the idle timer under ordinary scheduling jitter and the client reconnects mid-test.
        // 2000 + 500 leaves a 800ms step comfortably inside the window while still letting the
        // window elapse when the test stops resetting it.
        private const int ResetIdleMs = 2000;
        private const int ResetRequestMs = 500;
        private const int ResetStepMs = 800;

        // The spec's PING. This SDK's ProtocolMessage.MessageAction tops out at Auth = 17, so a PING goes
        // in as its wire number through a JObject, which reaches the SDK untouched.
        private const int PingAction = 22;

        public HeartbeatTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN23a/heartbeats-true-query-param-0
        [Fact]
        public async Task RTN23a_HeartbeatsTrueQueryParam()
        {
            RecordedUrl capturedUrl = null;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    capturedUrl = conn.Url;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key",
                        maxIdleInterval: 15000));
                });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The client should request heartbeats, because it cannot observe ping frames.
            capturedUrl.Should().NotBeNull();
            capturedUrl.QueryParams.Should().ContainKey("heartbeats");
            capturedUrl.QueryParams["heartbeats"].Should().Be("true");
        }

        // UTS: realtime/unit/RTN23a/idle-timeout-reconnect-1
        [Fact]
        public async Task RTN23a_IdleTimeoutReconnect()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttemptCount++;
                    var attempt = connectionAttemptCount;

                    // The server sends CONNECTED but then no further messages.
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: TimeoutIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, TimeoutRequestMs));

            // Registered before connecting: DISCONNECTED is transient on the way back up (RTN15a) and
            // cannot be caught by waiting for it.
            var stateChanges = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            connectionAttemptCount.Should().Be(1);

            // The spec's ADVANCE_TIME(7100) past maxIdleInterval + realtimeRequestTimeout, then its
            // AWAIT_STATE connected for the reconnection. NextConnectionState rather than
            // AwaitConnectionState: the client is still CONNECTED here, so waiting for CONNECTED would
            // return immediately and assert nothing.
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(stateChanges),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            connectionAttemptCount.Should().Be(2);
            client.Connection.Id.Should().Be("connection-id-2");

            mockWs.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN23a/heartbeat-resets-timer-2
        [Fact]
        public async Task RTN23a_HeartbeatResetsTimer()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttemptCount++;
                    var attempt = connectionAttemptCount;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: ResetIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, ResetRequestMs));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            connectionAttemptCount.Should().Be(1);

            // Not enough on its own to trigger the timeout.
            await AdvanceTime(ResetStepMs);

            // A HEARTBEAT from the server resets the timer.
            mockWs.SendToClient(ProtocolMessages.HeartbeatMessage());

            // Past the window measured from CONNECTED, but still inside it measured from the HEARTBEAT.
            await AdvanceTime(ResetStepMs);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionAttemptCount.Should().Be(1);

            // The spec's ADVANCE_TIME(2100) past the window, then its AWAIT_STATE connected for the
            // reconnection that follows.
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            connectionAttemptCount.Should().Be(2);

            mockWs.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN23a/any-message-resets-timer-3
        [Fact]
        public async Task RTN23a_AnyMessageResetsTimer()
        {
            var channelName = "test-RTN23a-message-" + UtsSandbox.RandomId();
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttemptCount++;
                    var attempt = connectionAttemptCount;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: ResetIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, ResetRequestMs));

            var stateChanges = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            await AdvanceTime(ResetStepMs);

            // An ACK from the server resets the timer. NOTE: the spec writes
            // ProtocolMessage(action: ACK, msgSerial: 0); the template adds the count: 1 the spec leaves out,
            // and with nothing awaiting an ACK the SDK treats it as a no-op either way.
            mockWs.SendToClient(ProtocolMessages.AckMessage(0));

            await AdvanceTime(ResetStepMs);

            // Still alive, because the timer was reset.
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // A MESSAGE from the server resets the timer again. It names a channel the client never
            // attached to, which is what the spec sets up; the SDK logs that and moves on.
            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray(new JObject
                {
                    ["name"] = "event",
                    ["data"] = "data",
                })));

            await AdvanceTime(ResetStepMs);

            // Still only one connection attempt — no timeout yet.
            connectionAttemptCount.Should().Be(1);

            // The spec's ADVANCE_TIME(3100) with no message, then its AWAIT_STATE connected.
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(stateChanges),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            connectionAttemptCount.Should().Be(2);

            mockWs.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN23a/timeout-triggers-reconnect-4
        [Fact]
        public async Task RTN23a_TimeoutTriggersReconnect()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttemptCount++;
                    var attempt = connectionAttemptCount;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: TimeoutIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, TimeoutRequestMs));

            var stateChanges = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            connectionAttemptCount.Should().Be(1);

            // The spec's ADVANCE_TIME(3100) past maxIdleInterval + realtimeRequestTimeout, then its
            // AWAIT_STATE connected for the immediate reconnection (RTN15a).
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(stateChanges),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            connectionAttemptCount.Should().Be(2);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be("connection-id-2");

            mockWs.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN23a/reconnect-uses-resume-5
        [Fact]
        public async Task RTN23a_ReconnectUsesResume()
        {
            var connectionAttempts = new List<RecordedUrl>();
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttempts.Add(conn.Url);
                    var attempt = connectionAttempts.Count;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: TimeoutIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, TimeoutRequestMs));

            var stateChanges = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec's ADVANCE_TIME(3100) past the window, then its AWAIT_STATE connected.
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(stateChanges),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            connectionAttempts.Should().HaveCount(2);

            // The first connection carries no resume parameter.
            connectionAttempts[0].QueryParams.Should().NotContainKey("resume");

            // The second resumes against the first connection's key (RTN15c).
            connectionAttempts[1].QueryParams.Should().ContainKey("resume");
            connectionAttempts[1].QueryParams["resume"].Should().Be("connection-key-1");
        }

        // UTS: realtime/unit/RTN23a/ping-resets-timer-6
        [Fact]
        public async Task RTN23a_PingResetsTimer()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    connectionAttemptCount++;
                    var attempt = connectionAttemptCount;
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-" + attempt,
                        connectionKey: "connection-key-" + attempt,
                        maxIdleInterval: ResetIdleMs));
                });

            var client = RealtimeClient(mockWs, configure: options => ScaleTimeouts(options, ResetRequestMs));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            connectionAttemptCount.Should().Be(1);

            // Not enough on its own to trigger the timeout.
            await AdvanceTime(ResetStepMs);

            // A PING from the server resets the timer. NOTE: the spec writes
            // ProtocolMessage(action: PING, id: "ping-1"). PING is action 22 on the wire and this SDK's
            // MessageAction does not define it, so it is injected as a raw JObject — which is exactly
            // what this spec point needs. RTN23a counts *any* inbound message as activity, and the SDK
            // refreshes its activity timestamp in ProcessMessage before any action-specific handler runs
            // (Realtime/Workflows/RealtimeWorkflow.cs:636), so an unrecognised action still counts.
            mockWs.SendToClient(new JObject
            {
                ["action"] = PingAction,
                ["id"] = "ping-1",
            });

            // Past the window measured from CONNECTED, but still inside it measured from the PING.
            await AdvanceTime(ResetStepMs);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionAttemptCount.Should().Be(1);

            // The spec's ADVANCE_TIME(2100) past the window, then its AWAIT_STATE connected.
            await UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            connectionAttemptCount.Should().Be(2);

            mockWs.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1);
        }

        /// <summary>
        /// The spec's <c>ADVANCE_TIME(ms)</c>. Real elapsed time, and deliberately so: RTN23a is a
        /// measurement of wall-clock inactivity sampled by a real timer loop, so for the window to elapse
        /// the time has to actually pass. It is never used to wait for an event — those waits all go
        /// through <c>UtsClients</c>.
        /// </summary>
        private static Task AdvanceTime(int milliseconds) => Task.Delay(milliseconds);

        /// <summary>
        /// The spec's <c>realtimeRequestTimeout</c> and <c>disconnectedRetryTimeout</c> client options,
        /// scaled, plus the monitor tick that samples the idle window. The spec's
        /// <c>disconnectedRetryTimeout: 500</c> is never waited out: the idle timeout asks for its
        /// reconnection with <c>retryInstantly</c> (RTN15a), so DISCONNECTED is left at once.
        /// </summary>
        private static void ScaleTimeouts(ClientOptions options, int realtimeRequestTimeoutMs)
        {
            options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(realtimeRequestTimeoutMs);
            options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(50);
            options.HeartbeatMonitorDelay = MonitorTickMs;
        }
    }
}
