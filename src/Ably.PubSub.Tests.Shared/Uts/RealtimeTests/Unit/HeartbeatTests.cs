// Heartbeat Tests (RTN23)
// Derived from uts/realtime/unit/connection/heartbeat_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: EnableFakeTimers is called before the client is built (the spec calls it first in Test Steps); a PING/ACK/MESSAGE the server sends is a JObject built from ProtocolMessages

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class HeartbeatTests : UtsUnitTestBase
    {
        // The spec's repeated expected sequence after an idle-timeout reconnect.
        private static readonly ConnectionState[] DisconnectReconnectSequence =
        {
            ConnectionState.Connecting,
            ConnectionState.Connected,
            ConnectionState.Disconnected,
            ConnectionState.Connecting,
            ConnectionState.Connected,
        };

        public HeartbeatTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's CONNECTED response: connection-id-N / connection-key-N with the given maxIdleInterval (ms).
        private static JObject NumberedConnected(int attempt, long maxIdleInterval)
        {
            return ProtocolMessages.ConnectedMessage(
                "connection-id-" + attempt,
                "connection-key-" + attempt,
                maxIdleInterval: maxIdleInterval);
        }

        // The spec's recurring CONNECTED_MESSAGE-shaped response with a fixed connection-id / connection-key.
        private static JObject FixedConnected()
        {
            return ProtocolMessages.ConnectedMessage("connection-id", "connection-key");
        }

        // Polls the recorded states for a re-entry into CONNECTED after a DISCONNECTED.
        private static Task AwaitReconnected(CaptureList<ConnectionState> states)
        {
            return UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(10),
                description: "connection re-entered CONNECTED after DISCONNECTED");
        }

        // UTS: realtime/unit/RTN23a/heartbeats-true-query-param-0
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_HeartbeatsTrueQueryParam()
        {
            // Setup
            var capturedUrls = new CaptureList<RecordedUrl>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                capturedUrls.Add(conn.Url);
                conn.RespondWithSuccess(FixedConnected());
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // Client should request heartbeats if it cannot observe ping frames
            capturedUrls[0].QueryParams["heartbeats"].Should().Be("true");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/idle-timeout-reconnect-1
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_IdleTimeoutReconnect()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);

                // Server sends CONNECTED but then no further messages
                conn.RespondWithSuccess(NumberedConnected(attempt, 5000)); // 5 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(2000), // 2 seconds
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Advance time past maxIdleInterval + realtimeRequestTimeout
            // = 5000 + 2000 = 7000ms
            await AdvanceTime(7100);

            // DEVIATION: the SDK evaluates RTN23a idle detection on a 1 s poll (ClientOptions.HeartbeatMonitorDelay), so the
            // disconnect comes up to 1 s after maxIdleInterval + realtimeRequestTimeout; time is advanced 1 s more - see deviations.md
            await AdvanceTime(1000);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for the reconnection to complete
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(stateChanges);

            // Assertions
            // Verify the full state change sequence
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), DisconnectReconnectSequence);

            // Verify two connection attempts were made (initial + reconnect)
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Verify we're connected with new connection details
            client.Connection.Id.Should().Be("connection-id-2");

            // Verify the client closed the first WebSocket connection
            var clientCloseEvents = mockWs.EventsOfType(MockEventType.ClientClose);
            clientCloseEvents.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/heartbeat-resets-timer-2
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_HeartbeatResetsTimer()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(NumberedConnected(attempt, 3000)); // 3 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Advance time (not enough to trigger timeout: 3000 + 1000 = 4000ms)
            await AdvanceTime(2000);

            // Send HEARTBEAT from server - resets timer
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.HeartbeatMessage());

            // Advance time again (2000ms since HEARTBEAT, still within threshold)
            await AdvanceTime(2000);

            // Connection should still be alive - no reconnection triggered
            client.Connection.State.Should().Be(ConnectionState.Connected);
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // NOTE: states are recorded before the stimulus, so the re-entry into CONNECTED can be polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Advance time past the timeout window (4100ms since last HEARTBEAT)
            await AdvanceTime(2100);

            // DEVIATION: the SDK evaluates RTN23a idle detection on a 1 s poll (ClientOptions.HeartbeatMonitorDelay), so the
            // disconnect comes up to 1 s after maxIdleInterval + realtimeRequestTimeout; time is advanced 1 s more - see deviations.md
            await AdvanceTime(1000);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for reconnection to complete
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(states);

            // Assertions
            // Verify reconnection happened
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Verify the client closed the first WebSocket connection
            var clientCloseEvents = mockWs.EventsOfType(MockEventType.ClientClose);
            clientCloseEvents.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/any-message-resets-timer-3
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_AnyMessageResetsTimer()
        {
            // Setup
            var channelName = "test-RTN23a-message-" + Guid.NewGuid().ToString("N");
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(NumberedConnected(attempt, 2000)); // 2 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Advance time (timeout is 2000+1000=3000ms)
            await AdvanceTime(1500);

            // Send ACK message from server - resets timer
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AckMessage(0));

            // Advance time again (1500ms since ACK, still within threshold)
            await AdvanceTime(1500);

            // Connection should still be alive (timer was reset)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Send MESSAGE from server - resets timer again
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray { new JObject { ["name"] = "event", ["data"] = "data" } }));

            // Advance time again (1500ms since MESSAGE)
            await AdvanceTime(1500);

            // Still only one connection attempt - no timeout yet
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Advance time past timeout without any message (3100ms since last activity)
            await AdvanceTime(3100);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for reconnection to complete
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(stateChanges);

            // Assertions
            // Verify the state change sequence includes disconnected
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), DisconnectReconnectSequence);

            // Verify two connection attempts were made
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Verify the client closed the first WebSocket connection
            var clientCloseEvents = mockWs.EventsOfType(MockEventType.ClientClose);
            clientCloseEvents.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/timeout-triggers-reconnect-4
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_TimeoutTriggersReconnect()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(NumberedConnected(attempt, 2000)); // 2 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Advance time past maxIdleInterval + realtimeRequestTimeout
            // = 2000 + 1000 = 3000ms
            await AdvanceTime(3100);

            // DEVIATION: the SDK evaluates RTN23a idle detection on a 1 s poll (ClientOptions.HeartbeatMonitorDelay), so the
            // disconnect comes up to 1 s after maxIdleInterval + realtimeRequestTimeout; time is advanced 1 s more - see deviations.md
            await AdvanceTime(1000);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for reconnection to complete (immediate per RTN15a)
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(stateChanges);

            // Assertions
            // Verify the state change sequence shows disconnect then reconnect
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), DisconnectReconnectSequence);

            // Verify two connection attempts were made (initial + reconnect)
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Verify the client is now connected with new connection details
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be("connection-id-2");

            // Verify the first connection was closed by the client
            var clientCloseEvents = mockWs.EventsOfType(MockEventType.ClientClose);
            clientCloseEvents.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/reconnect-uses-resume-5
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_ReconnectUsesResume()
        {
            // Setup
            var connectionAttempts = new CaptureList<RecordedUrl>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url);
                conn.RespondWithSuccess(NumberedConnected(connectionAttempts.Count, 2000)); // 2 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Advance time past timeout to trigger disconnection and reconnection
            await AdvanceTime(3100);

            // DEVIATION: the SDK evaluates RTN23a idle detection on a 1 s poll (ClientOptions.HeartbeatMonitorDelay), so the
            // disconnect comes up to 1 s after maxIdleInterval + realtimeRequestTimeout; time is advanced 1 s more - see deviations.md
            await AdvanceTime(1000);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for reconnection to complete
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(stateChanges);

            // Assertions
            // Verify the state change sequence
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), DisconnectReconnectSequence);

            connectionAttempts.Count.Should().Be(2);

            // First connection should not have resume parameter
            var firstUrl = connectionAttempts[0];
            firstUrl.QueryParams.Should().NotContainKey("resume");

            // Second connection should include resume parameter with first connectionKey
            var secondUrl = connectionAttempts[1];
            secondUrl.QueryParams["resume"].Should().Be("connection-key-1");
            await CloseClient(client);
        }

        // realtime/unit/connection: `# RTN23b Tests (WebSocket Ping Frames)` applies "to platforms where the WebSocket client CAN surface ping frame events";
        // ClientWebSocket can't, and the SDK always sends heartbeats=true (RTN23a path)
        // UTS-NA: realtime/unit/RTN23b/heartbeats-false-query-param-0 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/idle-timeout-reconnect-1 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/ping-frame-resets-timer-2 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/any-message-resets-timer-3 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/timeout-triggers-reconnect-4 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/reconnect-uses-resume-5 — not applicable: the platform WebSocket can't surface ping frames
        // UTS-NA: realtime/unit/RTN23b/multiple-pings-keep-alive-6 — not applicable: the platform WebSocket can't surface ping frames
        // realtime/unit/RTN23c: `heartbeats=bounce` "Only applies to browser (or equivalent) builds of an SDK"; this SDK always sends heartbeats=true
        // UTS-NA: realtime/unit/RTN23c/heartbeats-bounce-query-param-0 — not applicable: the spec limits it to browser builds, this SDK never sends heartbeats=bounce
        // UTS: realtime/unit/RTN23c1/ping-pong-echo-id-0
        [UtsFact]
        [Trait("spec", "RTN23c1")]
        public async Task RTN23c1_PingPongEchoId()
        {
            // Setup
            var rawFrames = new CaptureList<string>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(FixedConnected()),
                onTextDataFrame: text => rawFrames.Add(text));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // PING with an id
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.PingMessage("ping-1"));

            // NOTE: await_client_message(PONG) instead of await_next_message_from_client: the PONG can be sent before the await is registered, and await_client_message also sees earlier messages
            var pongWithId = await mockWs.AwaitClientMessage(ProtocolMessages.Pong);

            // PING without an id
            mockWs.ActiveConnection.SendToClient(new JObject { ["action"] = ProtocolMessages.Ping });
            var pongWithoutId = await mockWs.AwaitClientMessage(ProtocolMessages.Pong);

            // Assertions
            pongWithId.Action.Should().Be(ProtocolMessage.MessageAction.Pong);
            pongWithId.Id.Should().Be("ping-1");

            pongWithoutId.Action.Should().Be(ProtocolMessage.MessageAction.Pong);
            pongWithoutId.Id.Should().BeNull();

            // NOTE: ProtocolMessage.MsgSerial is a non-nullable long, so "msgSerial IS NULL" is asserted on the raw wire frames (the PONG frames captured by OnTextDataFrame)
            var pongFrames = rawFrames.Snapshot().Select(JObject.Parse).Where(f => (int)f["action"] == ProtocolMessages.Pong).ToList();
            pongFrames.Count.Should().Be(2);
            foreach (var pongFrame in pongFrames)
            {
                // DEVIATION: spec says msgSerial IS NULL; ProtocolMessage.MsgSerial is a non-nullable long, so every client frame (PONG included) carries msgSerial 0 - see deviations.md
                ((long)pongFrame["msgSerial"]).Should().Be(0L, "the SDK serialises msgSerial 0 on a PONG");
            }

            // A PONG is a bare liveness response; nothing else should be populated
            // NOTE: the decoded ProtocolMessage initialises Messages and Presence to empty arrays, so "IS NULL" is asserted on the raw PONG frames
            foreach (var pongFrame in pongFrames)
            {
                (pongFrame["channel"] == null || pongFrame["channel"].Type == JTokenType.Null).Should().BeTrue("a PONG carries no channel");
                (pongFrame["messages"] == null || pongFrame["messages"].Type == JTokenType.Null).Should().BeTrue("a PONG carries no messages");
                (pongFrame["presence"] == null || pongFrame["presence"].Type == JTokenType.Null).Should().BeTrue("a PONG carries no presence");
            }

            // The client sent nothing else in response to the PINGs (e.g. no HEARTBEAT)
            var clientMessages = mockWs.EventsOfType(MockEventType.MessageFromClient);
            clientMessages.Count.Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23c1/pong-regardless-of-heartbeats-param-1
        [UtsFact]
        [Trait("spec", "RTN23c1")]
        public async Task RTN23c1_PongRegardlessOfHeartbeatsParam()
        {
            // Setup
            var capturedUrls = new CaptureList<RecordedUrl>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                capturedUrls.Add(conn.Url);
                conn.RespondWithSuccess(FixedConnected());
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                TransportParams = new Dictionary<string, object> { ["heartbeats"] = "false" },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            capturedUrls[0].QueryParams["heartbeats"].Should().Be("false");

            mockWs.ActiveConnection.SendToClient(ProtocolMessages.PingMessage("ping-1"));

            // NOTE: await_client_message(PONG) instead of await_next_message_from_client: the PONG can be sent before the await is registered
            var pong = await mockWs.AwaitClientMessage(ProtocolMessages.Pong);

            // Assertions
            pong.Action.Should().Be(ProtocolMessage.MessageAction.Pong);
            pong.Id.Should().Be("ping-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN23a/ping-resets-timer-6
        [UtsFact]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_PingResetsTimer()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(NumberedConnected(attempt, 3000)); // 3 seconds
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Advance time (not enough to trigger timeout: 3000 + 1000 = 4000ms)
            await AdvanceTime(2000);

            // Send PING from server - resets timer
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.PingMessage("ping-1"));

            // Advance time again (2000ms since PING, still within threshold)
            await AdvanceTime(2000);

            // Connection should still be alive - no reconnection triggered
            client.Connection.State.Should().Be(ConnectionState.Connected);
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // NOTE: states are recorded before the stimulus, so the re-entry into CONNECTED can be polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Advance time past the timeout window (4100ms since last PING)
            await AdvanceTime(2100);

            // DEVIATION: the SDK evaluates RTN23a idle detection on a 1 s poll (ClientOptions.HeartbeatMonitorDelay), so the
            // disconnect comes up to 1 s after maxIdleInterval + realtimeRequestTimeout; time is advanced 1 s more - see deviations.md
            await AdvanceTime(1000);

            // After idle timeout fires, the client enters DISCONNECTED and waits
            // disconnectedRetryTimeout before reconnecting. If using fake timers,
            // ensure time is advanced past both the idle timeout AND the retry delay.

            // Wait for reconnection to complete
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the recorded states are polled for the re-entry into CONNECTED
            await AwaitReconnected(states);

            // Assertions
            // Verify reconnection happened
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Verify the client closed the first WebSocket connection
            var clientCloseEvents = mockWs.EventsOfType(MockEventType.ClientClose);
            clientCloseEvents.Count.Should().Be(1);
            await CloseClient(client);
        }
    }
}
