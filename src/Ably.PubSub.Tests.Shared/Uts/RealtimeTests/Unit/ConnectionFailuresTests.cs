// Connection Failures When Connected Tests (RTN14h, RTN15, RTN15a, RTN15b, RTN15c, RTN15d, RTN15e, RTN15h, RTN15j)
// Derived from uts/realtime/unit/connection/connection_failures_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    public class ConnectionFailuresTests : UtsUnitTestBase
    {
        public ConnectionFailuresTests(ITestOutputHelper output)
            : base(output)
        {
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

        private static JObject TokenDetailsBody(string token)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new JObject
            {
                ["token"] = token,
                ["keyName"] = "appId.keyId",
                ["issued"] = now,
                ["expires"] = now + 3600000,
                ["capability"] = "{\"*\":[\"*\"]}",
            };
        }

        // UTS: realtime/unit/RTN15h1/token-error-no-renew-0
        [UtsFact(Skip = "UTS spec error RTN15h1: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN15h1")]
        public Task RTN15h1_TokenErrorNoRenew()
        {
            // SPEC ERROR RTN15h1: the spec asserts errorReason.code == 40142 for a client given only a token (no means to renew),
            // but RSA4a2 says the library "should indicate an error with error code 40171" in exactly that case (the proxy-tier
            // RTN15h1 spec asserts 40171). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN15h1 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN15h2/token-error-renew-success-0
        [UtsFact(Skip = "UTS spec error RTN15h2: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN15h2")]
        public Task RTN15h2_TokenErrorRenewSuccess()
        {
            // SPEC ERROR RTN15h2: the spec asserts two token requests ("Initial + renewal") for a client built with only key:, but
            // RSA2 makes Basic auth the default when an API key exists and RSA4 uses token auth only with useTokenAuth, authUrl,
            // authCallback, token or tokenDetails, so no initial token request is made. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN15h2 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN15h2/token-error-renew-fails-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN15h2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN15h2")]
        public async Task RTN15h2_TokenErrorRenewFails()
        {
            // Setup
            // Mock HTTP for token requests (returns error)
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (req.Path.Contains("/keys/"))
                    {
                        req.RespondWith(401, new JObject
                        {
                            ["error"] = new JObject
                            {
                                ["code"] = 40101,
                                ["statusCode"] = 401,
                                ["message"] = "Invalid credentials",
                            },
                        });
                    }
                });
            InstallMock(mockHttp);

            // Mock WebSocket
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Connect successfully
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Get WebSocket connection
            var ws = mockWs.Connections[0];

            // NOTE: RTN15h2i has a transient DISCONNECTED before CONNECTING, so a bare AWAIT_STATE disconnected could match the wrong transition; the states are recorded before the stimulus and CONNECTING then DISCONNECTED is awaited in order
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Server sends DISCONNECTED with token error and closes connection
            ws.SendToClientAndClose(ProtocolMessages.DisconnectedMessage(40142, "Token expired", 401));

            // Should transition to CONNECTING (to attempt renewal)
            // Renewal fails, should transition to DISCONNECTED
            await UtsWait.PollUntil(
                () => HasSequence(states, ConnectionState.Connecting, ConnectionState.Disconnected),
                timeout: TimeSpan.FromSeconds(5),
                description: "connection went CONNECTING then DISCONNECTED after the failed renewal");

            // Assertions
            // Connection is DISCONNECTED (will retry later)
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // Error reason is set (from token renewal failure)
            client.Connection.ErrorReason.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15h3/non-token-error-resume-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN15h3", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN15h3")]
        public async Task RTN15h3_NonTokenErrorResume()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First connection succeeds
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Resume succeeds
                    conn.RespondWithSuccess();

                    // Same ID = resumed
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Connect successfully
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;

            // Get WebSocket connection
            var ws = mockWs.Connections[0];

            // NOTE: AWAIT_STATE connecting would return at once or miss a transient state; the states are recorded before the stimulus and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Server sends DISCONNECTED with non-token error and closes connection
            ws.SendToClientAndClose(ProtocolMessages.DisconnectedMessage(80003, "Service unavailable", 503));

            // Should transition to CONNECTING immediately (no token renewal)
            // Should reconnect and resume
            await UtsWait.PollUntil(
                () => HasSequence(states, ConnectionState.Connecting, ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(5),
                description: "connection went CONNECTING then CONNECTED again");

            // Assertions
            // Successfully reconnected
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Connection was resumed (same ID)
            client.Connection.Id.Should().Be(originalConnectionId);

            // Two connection attempts total
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Second connection attempt included resume parameter
            // NOTE: the spec reads mock_ws.events[1], which would be the second event on the timeline; interpreted as the second connection attempt
            mockWs.ConnectionAttempts[1].QueryParams["resume"].Should().Be("key-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15j/error-empty-channel-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN15j", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN15j")]
        public async Task RTN15j_ErrorEmptyChannelFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Connect successfully
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Get WebSocket connection
            var ws = mockWs.Connections[0];

            // Server sends ERROR with empty channel (connection-level error) and closes connection
            // Empty = connection-level error
            ws.SendToClientAndClose(ProtocolMessages.ErrorMessage(50000, "Internal error", 500));

            // Should transition to FAILED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(2));

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(500);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15a/unexpected-transport-disconnect-0
        [UtsFact]
        [Trait("spec", "RTN15a")]
        public async Task RTN15a_UnexpectedTransportDisconnect()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First connection succeeds
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Resume succeeds
                    conn.RespondWithSuccess();

                    // Same ID = resumed
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Connect successfully
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;

            // Get WebSocket connection
            var ws = mockWs.Connections[0];

            // NOTE: DISCONNECTED and CONNECTING are transient (an immediate reconnect follows); the states are recorded before the stimulus and verified in order
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Simulate unexpected disconnect (no protocol message)
            ws.SimulateDisconnect();

            // Should transition to DISCONNECTED
            // Should automatically attempt reconnect
            // Should resume successfully
            await UtsWait.PollUntil(
                () => HasSequence(states, ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(7),
                description: "connection went DISCONNECTED, CONNECTING, CONNECTED");

            // Assertions
            // Successfully reconnected
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Connection was resumed (same ID)
            client.Connection.Id.Should().Be(originalConnectionId);

            // Two connection attempts made
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15b/successful-resume-0
        [UtsFact]
        [Trait("spec", "RTN15b")]
        public async Task RTN15b_SuccessfulResume()
        {
            // Setup
            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new CaptureList<PendingWebSocketConnection>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                capturedConnectionAttempts.Add(conn);

                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Resume succeeds (same connectionId)
                    // Same ID indicates successful resume
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1-updated", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Initial connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;
            originalConnectionId.Should().Be("connection-1");

            // Force disconnect
            var ws = mockWs.Connections[0];
            var states = UtsRecord.ConnectionStates(client.Connection);
            ws.SimulateDisconnect();

            // Wait for reconnection
            // NOTE: AWAIT_STATE would return at once (already CONNECTED); the record is polled for a CONNECTED entered after the stimulus and for the second connection attempt
            await UtsWait.PollUntil(
                () => states.Snapshot().Count(s => s == ConnectionState.Connected) >= 1 && Volatile.Read(ref connectionAttemptCount) >= 2,
                timeout: TimeSpan.FromSeconds(5),
                description: "re-entered CONNECTED after the forced disconnect");

            // Assertions
            // Connection resumed (same ID)
            client.Connection.Id.Should().Be("connection-1");

            // Connection key was updated (RTN15e)
            client.Connection.Key.Should().Be("key-1-updated");

            // Second connection attempt included resume parameter (RTN15b1)
            capturedConnectionAttempts[1].QueryParams["resume"].Should().Be("key-1");

            // Two connection attempts total
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15c7/failed-resume-new-id-0
        [UtsFact]
        [Trait("spec", "RTN15c7")]
        public async Task RTN15c7_FailedResumeNewId()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Resume failed (new connectionId + error)
                    // Different ID = failed resume
                    var connected = ProtocolMessages.ConnectedMessage("connection-2", "key-2", connectionStateTtl: 120000, maxIdleInterval: 15000);
                    connected["error"] = ProtocolMessages.ErrorObject(80008, "Unable to recover connection", 400);
                    conn.SendToClient(connected);
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Initial connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;

            // Force disconnect
            var ws = mockWs.Connections[0];
            var states = UtsRecord.ConnectionStates(client.Connection);
            ws.SimulateDisconnect();

            // Wait for reconnection
            // NOTE: AWAIT_STATE would return at once (already CONNECTED); the record is polled for a CONNECTED entered after the stimulus and for the second connection attempt
            await UtsWait.PollUntil(
                () => states.Snapshot().Count(s => s == ConnectionState.Connected) >= 1 && Volatile.Read(ref connectionAttemptCount) >= 2,
                timeout: TimeSpan.FromSeconds(5),
                description: "re-entered CONNECTED after the forced disconnect");

            // Assertions
            // New connection (different ID)
            client.Connection.Id.Should().Be("connection-2");
            client.Connection.Id.Should().NotBe(originalConnectionId);

            // Connection key updated
            client.Connection.Key.Should().Be("key-2");

            // Error reason set (indicates why resume failed)
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);

            // Connection is still CONNECTED (despite error)
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15e/connection-key-updated-0
        [UtsFact]
        [Trait("spec", "RTN15e")]
        public async Task RTN15e_ConnectionKeyUpdated()
        {
            // NOTE: the spec says this is covered by the RTN15b, RTN15c6 test and gives only the assertion below; it is rendered with the RTN15b scenario (a resume where CONNECTED carries a new connectionKey)
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1-updated", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var ws = mockWs.Connections[0];
            var states = UtsRecord.ConnectionStates(client.Connection);
            ws.SimulateDisconnect();

            // NOTE: AWAIT_STATE would return at once (already CONNECTED); the record is polled for a CONNECTED entered after the stimulus and for the second connection attempt
            await UtsWait.PollUntil(
                () => states.Snapshot().Count(s => s == ConnectionState.Connected) >= 1 && Volatile.Read(ref connectionAttemptCount) >= 2,
                timeout: TimeSpan.FromSeconds(5),
                description: "re-entered CONNECTED after the forced disconnect");

            // Assertions
            client.Connection.Key.Should().Be("key-1-updated");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN14h/resume-after-ttl-0
        [UtsFact]
        [Trait("spec", "RTN14h")]
        public async Task RTN14h_ResumeAfterTtl()
        {
            // Setup
            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new CaptureList<PendingWebSocketConnection>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                capturedConnectionAttempts.Add(conn);

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection succeeds
                    conn.RespondWithSuccess();

                    // 5 seconds TTL
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 5000, maxIdleInterval: 15000));
                }
                else
                {
                    // All reconnection attempts fail, so the connection stays suspended
                    conn.RespondWithRefused();
                }
            });
            InstallMock(mockWs);

            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200, "yes")); // Connectivity check
            InstallMock(mockHttp);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(2000),
                AutoConnect = false,
                FallbackHosts = new string[0],
            });

            // Test Steps
            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Initial connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Force disconnect - triggers immediate reconnect per RTN15a
            var ws = mockWs.Connections[0];
            ws.SimulateDisconnect();

            // Reconnection attempts keep failing (connection refused).
            // Advance time well past the connectionStateTtl so the connection becomes
            // suspended and then keeps attempting to reconnect.
            // TTL is 5000ms, disconnectedRetryTimeout is 1000ms,
            // suspendedRetryTimeout is 2000ms.
            for (var i = 0; i < 15; i++)
            {
                await AdvanceTime(2500);
            }

            // Assertions
            // Verify the connection became suspended
            stateChanges.Snapshot().Should().Contain(ConnectionState.Suspended);

            // RTN14h: every reconnection attempt after the first should still attempt a
            // resume, carrying the original connectionKey, even those made after the TTL
            // expired and the connection became suspended.
            var reconnectAttempts = capturedConnectionAttempts.Snapshot().Skip(1).ToList();
            reconnectAttempts.Should().NotBeEmpty();
            foreach (var attempt in reconnectAttempts)
            {
                attempt.QueryParams["resume"].Should().Be("key-1");
            }

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN15c5/token-error-during-resume-0
        [UtsFact(Skip = "UTS spec error RTN15c5: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN15c5")]
        public Task RTN15c5_TokenErrorDuringResume()
        {
            // SPEC ERROR RTN15c5: the spec asserts two token requests ("Initial + renewal") for a client built with only key:, but
            // RSA2 makes Basic auth the default when an API key exists and RSA4 uses token auth only with useTokenAuth, authUrl,
            // authCallback, token or tokenDetails, so no initial token request is made. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN15c5 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN15c4/fatal-error-during-resume-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN15c4", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN15c4")]
        public async Task RTN15c4_FatalErrorDuringResume()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Resume attempt fails with fatal error
                    conn.SendToClient(ProtocolMessages.ErrorMessage(50000, "Internal server error", 500));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Initial connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Force disconnect (will trigger resume attempt)
            var ws = mockWs.Connections[0];
            ws.SimulateDisconnect();

            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);

            // Only two connection attempts (no retry after fatal error)
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);
            await CloseClient(client);
        }
    }
}
