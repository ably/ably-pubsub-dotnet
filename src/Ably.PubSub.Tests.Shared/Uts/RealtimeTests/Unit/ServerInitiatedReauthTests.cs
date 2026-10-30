// Server-Initiated Re-authentication Tests (RTN22, RTN22a)
// Derived from uts/realtime/unit/connection/server_initiated_reauth_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: no fake timers (the spec has none): token expiry uses the real clock, now() + 3600000 rendered as DateTimeOffset.UtcNow plus one hour

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
    public class ServerInitiatedReauthTests : UtsUnitTestBase
    {
        public ServerInitiatedReauthTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static JObject ConnectedFrame(string connectionId, string connectionKey)
        {
            return new JObject
            {
                ["action"] = ProtocolMessages.Connected,
                ["connectionId"] = connectionId,
                ["connectionKey"] = connectionKey,
                ["connectionDetails"] = new JObject
                {
                    ["connectionKey"] = connectionKey,
                    ["maxIdleInterval"] = 15000,
                    ["connectionStateTtl"] = 120000,
                },
            };
        }

        // UTS: realtime/unit/RTN22/server-auth-triggers-reauth-0
        [UtsFact]
        [Trait("spec", "RTN22")]
        public async Task RTN22_ServerAuthTriggersReauth()
        {
            // Setup
            var authCallbackCount = 0;
            var capturedAuthMessages = new CaptureList<ProtocolMessage>();

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref authCallbackCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { AuthCallback = authCallback, AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record state changes during reauth
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // When client sends AUTH back, record it and respond with CONNECTED (update)
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    capturedAuthMessages.Add(msg);
                    mockWs.ActiveConnection.SendToClient(ConnectedFrame("connection-id", "connection-key-2"));
                }
            });

            // Server requests re-authentication
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            // Wait for the UPDATE event that signals reauth completion
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(c => c.Event == ConnectionEvent.Update),
                description: "an UPDATE event signalling reauth completion");

            // Assertions
            // authCallback was called twice: once for initial connect, once for reauth
            Volatile.Read(ref authCallbackCount).Should().Be(2);

            // Client sent AUTH message back with new token
            capturedAuthMessages.Count.Should().Be(1);
            capturedAuthMessages[0].Auth.Should().NotBeNull();
            capturedAuthMessages[0].Auth.AccessToken.Should().Be("token-2");

            // Connection stayed CONNECTED throughout (no state transitions, only UPDATE)
            var connectedToOther = stateChanges.Snapshot().Where(c => c.Current != ConnectionState.Connected).ToList();
            connectedToOther.Count.Should().Be(0);

            // UPDATE event was emitted (RTN24)
            var updateEvents = stateChanges.Snapshot().Where(c => c.Event == ConnectionEvent.Update).ToList();
            updateEvents.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN22/stays-connected-during-reauth-1
        [UtsFact]
        [Trait("spec", "RTN22")]
        public async Task RTN22_StaysConnectedDuringReauth()
        {
            // Setup
            var authCallbackCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref authCallbackCount);
                return Task.FromResult<object>(new TokenDetails("reauth-token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("conn-1", "key-1"));
            });
            InstallMock(mockWs);

            // Auto-respond to AUTH with CONNECTED
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.ActiveConnection.SendToClient(ConnectedFrame("conn-1", "key-1-updated"));
                }
            });

            var client = Realtime(new ClientOptions { AuthCallback = authCallback, AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // Server sends AUTH
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            // Wait for UPDATE event
            await UtsWait.PollUntil(
                () => stateChanges.Count >= 1,
                description: "a state change event (the UPDATE)");

            // Assertions
            // Connection never left CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Only an UPDATE event, no state change events
            stateChanges.Count.Should().Be(1);
            stateChanges[0].Event.Should().Be(ConnectionEvent.Update);
            stateChanges[0].Current.Should().Be(ConnectionState.Connected);
            stateChanges[0].Previous.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN22a/forced-disconnect-reauth-failure-0
        [UtsFact]
        [Trait("spec", "RTN22a")]
        public async Task RTN22a_ForcedDisconnectReauthFailure()
        {
            // Setup
            var authCallbackCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref authCallbackCount);
                return Task.FromResult<object>(new TokenDetails("recovery-token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("conn-1", "key-1"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { AuthCallback = authCallback, AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // Server forcibly disconnects with token error (simulating reauth timeout)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.DisconnectedMessage(40142, "Token expired", 401));

            // Wait for client to transition to DISCONNECTED
            // NOTE: DISCONNECTED may be left again at once (token-error recovery reconnects), so AWAIT_STATE could miss it; the record is polled instead
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(c => c.Current == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Assertions
            // Client transitioned to DISCONNECTED with the token error
            var disconnectedChange = stateChanges.Snapshot().FirstOrDefault(c => c.Current == ConnectionState.Disconnected);
            disconnectedChange.Should().NotBeNull();
            disconnectedChange.Reason.Code.Should().Be(40142);

            // The client should attempt to reconnect (RTN15h token-error recovery
            // will obtain a new token and reconnect)
            await CloseClient(client);
        }
    }
}
