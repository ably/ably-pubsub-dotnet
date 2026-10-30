// Connection Ping Tests (RTN13, RTN13a, RTN13b, RTN13c, RTN13d, RTN13e)
// Derived from uts/realtime/unit/connection/connection_ping_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving); fake timers are enabled before the client is built where the spec enables them after construction (equivalent: no timer has started yet); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ConnectionPingTests : UtsUnitTestBase
    {
        public ConnectionPingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // A HEARTBEAT frame, with an id when given (the ping response is matched by id, RTN13e).
        private static JObject HeartbeatWithId(string id)
        {
            var heartbeat = ProtocolMessages.HeartbeatMessage();
            if (id != null)
            {
                heartbeat["id"] = id;
            }

            return heartbeat;
        }

        // True once the recorded states contain the sequence in order (a subsequence), for transient states.
        private static bool HasSequence(CaptureList<ConnectionState> states, params ConnectionState[] sequence)
        {
            var next = 0;
            foreach (var state in states.Snapshot())
            {
                if (next < sequence.Length && state == sequence[next])
                {
                    next++;
                }
            }

            return next == sequence.Length;
        }

        private static MockWebSocket EchoingMockWebSocket(Action<PendingWebSocketConnection> onConnectionAttempt)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: onConnectionAttempt,
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Heartbeat)
                    {
                        // Echo back a HEARTBEAT with matching id
                        mockWs.ActiveConnection.SendToClient(HeartbeatWithId(msg.Id));
                    }
                });
            return mockWs;
        }

        // UTS: realtime/unit/RTN13a/ping-heartbeat-roundtrip-0
        [UtsFact]
        [Trait("spec", "RTN13a")]
        public async Task RTN13a_PingHeartbeatRoundtrip()
        {
            // Setup
            var mockWs = EchoingMockWebSocket(conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");

            // Assertions
            // Ping should resolve successfully
            pingResult.IsSuccess.Should().BeTrue("AWAIT client.connection.ping() succeeds ({0})", pingResult.Error?.Message);
            pingResult.Value.Should().HaveValue();
            pingResult.Value.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

            // Verify a HEARTBEAT was sent by the client
            var heartbeatsSent = mockWs.MessagesFromClientOfAction(ProtocolMessages.Heartbeat);
            heartbeatsSent.Should().HaveCount(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13e/heartbeat-random-id-0
        [UtsFact]
        [Trait("spec", "RTN13e")]
        public async Task RTN13e_HeartbeatRandomId()
        {
            // Setup
            string capturedHeartbeatId = null;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Heartbeat)
                    {
                        Volatile.Write(ref capturedHeartbeatId, msg.Id);

                        // First send a HEARTBEAT with a DIFFERENT id (should be ignored)
                        mockWs.ActiveConnection.SendToClient(HeartbeatWithId("wrong-id"));

                        // Then send a HEARTBEAT with the matching id (should resolve ping)
                        mockWs.ActiveConnection.SendToClient(HeartbeatWithId(msg.Id));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");

            // Assertions
            // Ping should resolve (matched the correct id)
            pingResult.IsSuccess.Should().BeTrue("AWAIT client.connection.ping() succeeds ({0})", pingResult.Error?.Message);
            pingResult.Value.Should().HaveValue();
            pingResult.Value.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

            // The sent HEARTBEAT should have had a non-empty id
            Volatile.Read(ref capturedHeartbeatId).Should().NotBeNull();
            Volatile.Read(ref capturedHeartbeatId).Length.Should().BeGreaterThan(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13e/no-id-heartbeat-ignored-1
        [UtsFact]
        [Trait("spec", "RTN13e")]
        public async Task RTN13e_NoIdHeartbeatIgnored()
        {
            // Setup
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Heartbeat)
                    {
                        // Send a HEARTBEAT without an id (like a server-initiated heartbeat)
                        mockWs.ActiveConnection.SendToClient(HeartbeatWithId(null));

                        // Then send the correct response
                        mockWs.ActiveConnection.SendToClient(HeartbeatWithId(msg.Id));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");

            // Assertions
            // Ping should resolve (ignored the no-id heartbeat, matched the correct one)
            pingResult.IsSuccess.Should().BeTrue("AWAIT client.connection.ping() succeeds ({0})", pingResult.Error?.Message);
            pingResult.Value.Should().HaveValue();
            pingResult.Value.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13e/concurrent-pings-unique-ids-2
        [UtsFact]
        [Trait("spec", "RTN13e")]
        public async Task RTN13e_ConcurrentPingsUniqueIds()
        {
            // Setup
            var mockWs = EchoingMockWebSocket(conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start two pings concurrently
            var ping1Future = client.Connection.PingAsync();
            var ping2Future = client.Connection.PingAsync();

            var pingResult1 = await UtsWait.WithTimeout(ping1Future, TimeSpan.FromSeconds(10), "ping1Future");
            var pingResult2 = await UtsWait.WithTimeout(ping2Future, TimeSpan.FromSeconds(10), "ping2Future");

            // Assertions
            // Both pings should resolve
            pingResult1.IsSuccess.Should().BeTrue("AWAIT ping1_future succeeds ({0})", pingResult1.Error?.Message);
            pingResult1.Value.Should().HaveValue();
            pingResult2.IsSuccess.Should().BeTrue("AWAIT ping2_future succeeds ({0})", pingResult2.Error?.Message);
            pingResult2.Value.Should().HaveValue();

            // Verify two separate HEARTBEAT messages were sent
            var heartbeatsSent = mockWs.MessagesFromClientOfAction(ProtocolMessages.Heartbeat);
            heartbeatsSent.Should().HaveCount(2);

            // The two HEARTBEATs should have different ids
            heartbeatsSent[0].Id.Should().NotBe(heartbeatsSent[1].Id);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13c/ping-timeout-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RTN13c")]
        public async Task RTN13c_PingTimeout()
        {
            // Setup
            // No onMessageFromClient handler — server never responds to HEARTBEAT
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(2000),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pingFuture = client.Connection.PingAsync();

            // Advance time past realtimeRequestTimeout
            await AdvanceTime(2100);

            // NOTE: the ping timeout continuation runs asynchronously after ADVANCE_TIME (README Known gaps, connection.ping()); the task is awaited with a wall-clock bound
            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "ping result after the timeout");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();

            // The error should indicate a timeout
            // DEVIATION: spec requires the message to contain "timeout", the SDK says "Request timed out" - see deviations.md
            error.Message.Should().ContainEquivalentOf("timed out");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/ping-error-initialized-0
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_PingErrorInitialized()
        {
            // Setup
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/ping-error-suspended-1
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_PingErrorSuspended()
        {
            // Setup
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100),
                FallbackHosts = new string[0],
            });

            // NOTE: AWAIT_STATE disconnected / suspended could miss a transient state; the states are recorded before connect() and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Advance past connectionStateTtl to reach SUSPENDED
            await AdvanceTime(121000);
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Suspended),
                description: "connection entered SUSPENDED");

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/ping-error-closed-2
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_PingErrorClosed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/ping-error-failed-3
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_PingErrorFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithError(
                ProtocolMessages.ErrorMessage(80000, "Fatal error", 400)));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            var pingResult = await UtsWait.WithTimeout(client.Connection.PingAsync(), TimeSpan.FromSeconds(10), "PingAsync");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13d/ping-deferred-connecting-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN13d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN13d")]
        public async Task RTN13d_PingDeferredConnecting()
        {
            // Setup
            var mockWs = EchoingMockWebSocket(conn =>
            {
                // Delay the CONNECTED response so we can call ping() while CONNECTING
                Clock.ScheduleAfter(TimeSpan.FromMilliseconds(100), () => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();

            // NOTE: connect() only queues the command, so the state is awaited rather than read synchronously; the CONNECTED response is held back on the fake clock, so CONNECTING persists
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // Call ping() while still CONNECTING
            var pingFuture = client.Connection.PingAsync();

            // Advance time so the connection completes
            await AdvanceTime(200);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "deferred ping result");

            // Assertions
            // Ping should resolve after connection was established
            pingResult.IsSuccess.Should().BeTrue("AWAIT ping_future succeeds ({0})", pingResult.Error?.Message);
            pingResult.Value.Should().HaveValue();
            pingResult.Value.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

            // Verify HEARTBEAT was sent (only after CONNECTED)
            var heartbeatsSent = mockWs.MessagesFromClientOfAction(ProtocolMessages.Heartbeat);
            heartbeatsSent.Should().HaveCount(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13d/ping-deferred-disconnected-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN13d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN13d")]
        public async Task RTN13d_PingDeferredDisconnected()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = EchoingMockWebSocket(conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First attempt: connect successfully
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1"));
                }
                else
                {
                    // Subsequent attempts: also connect successfully
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-2", "conn-key-2"));
                }
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // NOTE: AWAIT_STATE disconnected could miss a transient state; the states are recorded before the stimulus and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Force disconnect by closing the transport
            mockWs.ActiveConnection.CloseFromServer();
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Call ping() while DISCONNECTED
            var pingFuture = client.Connection.PingAsync();

            // Advance time past disconnectedRetryTimeout so reconnection happens
            await AdvanceTime(600);

            // NOTE: AWAIT_STATE connected would return at once if the SDK already reconnected; the record is polled for the sequence
            await UtsWait.PollUntil(
                () => HasSequence(states, ConnectionState.Disconnected, ConnectionState.Connected),
                description: "connection reconnected after DISCONNECTED");

            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "deferred ping result");

            // Assertions
            // Ping should resolve after reconnection
            pingResult.IsSuccess.Should().BeTrue("AWAIT ping_future succeeds ({0})", pingResult.Error?.Message);
            pingResult.Value.Should().HaveValue();
            pingResult.Value.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

            // Verify HEARTBEAT was sent
            var heartbeatsSent = mockWs.MessagesFromClientOfAction(ProtocolMessages.Heartbeat);
            heartbeatsSent.Should().HaveCount(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/deferred-ping-error-failed-4
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_DeferredPingErrorFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Respond with fatal error instead of CONNECTED
                Clock.ScheduleAfter(TimeSpan.FromMilliseconds(100), () => conn.RespondWithError(
                    ProtocolMessages.ErrorMessage(80000, "Fatal error", 400)));
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();

            // NOTE: connect() only queues the command, so the state is awaited rather than read synchronously; the response is held back on the fake clock, so CONNECTING persists
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // Call ping() while CONNECTING
            var pingFuture = client.Connection.PingAsync();

            // Advance time so the error response arrives
            await AdvanceTime(200);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "deferred ping result");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13b/deferred-ping-error-suspended-5
        [UtsFact]
        [Trait("spec", "RTN13b")]
        public async Task RTN13b_DeferredPingErrorSuspended()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100),
                FallbackHosts = new string[0],
            });

            // NOTE: AWAIT_STATE disconnected / suspended could miss a transient state; the states are recorded before connect() and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Call ping() while DISCONNECTED
            var pingFuture = client.Connection.PingAsync();

            // Advance past connectionStateTtl to reach SUSPENDED
            await AdvanceTime(121000);
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Suspended),
                description: "connection entered SUSPENDED");

            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "deferred ping result");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN13c/deferred-ping-timeout-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN13d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN13c")]
        public async Task RTN13c_DeferredPingTimeout()
        {
            // Setup
            // No onMessageFromClient — server never responds to HEARTBEAT
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Clock.ScheduleAfter(TimeSpan.FromMilliseconds(100), () => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(2000),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();

            // NOTE: connect() only queues the command, so the state is awaited rather than read synchronously; the CONNECTED response is held back on the fake clock, so CONNECTING persists
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // Call ping() while CONNECTING
            var pingFuture = client.Connection.PingAsync();

            // Advance time so connection completes
            await AdvanceTime(200);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Advance time past realtimeRequestTimeout
            await AdvanceTime(2100);

            // NOTE: the ping timeout continuation runs asynchronously after ADVANCE_TIME (README Known gaps, connection.ping()); the task is awaited with a wall-clock bound
            var pingResult = await UtsWait.WithTimeout(pingFuture, TimeSpan.FromSeconds(5), "deferred ping result after the timeout");
            pingResult.IsFailure.Should().BeTrue();
            var error = pingResult.Error;

            // Assertions
            error.Should().NotBeNull();
            error.Message.Should().ContainEquivalentOf("timeout");
            await CloseClient(client);
        }
    }
}
