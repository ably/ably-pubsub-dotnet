using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/server_initiated_reauth_test.md in ably/specification.
    ///
    /// Spec points: RTN22, RTN22a
    ///
    /// <para>
    /// The spec's <c>mock_ws.on_client_message</c> is <c>MockWebSocket.OnMessageFromClient</c>, and the
    /// AUTH the client sends back is read off the messages the mock decodes — so the spec's
    /// <c>msg.auth.accessToken</c> is <c>message.Auth.AccessToken</c>. The handler answers it by
    /// injecting a CONNECTED, which this SDK treats as an RTN24 update rather than a reconnection, and
    /// that update is the event the spec waits for.
    /// </para>
    /// <para>
    /// The auth callback returns a <c>TokenDetails</c> with an hour of life left, as the spec does. The
    /// SDK reads the clock through <c>ClientOptions.NowFunc</c> and no <c>TestClock</c> is installed, so
    /// the token stays valid for the whole test and nothing re-requests it behind the reauth under test.
    /// <c>UtsClients.Options</c> also supplies the tier's default <c>Key</c>; with an
    /// <c>AuthCallback</c> set, <c>AblyAuth</c> selects token auth regardless (RSA4), which is what these
    /// tests need.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ServerInitiatedReauthTests : UtsTestBase
    {
        public ServerInitiatedReauthTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN22/server-auth-triggers-reauth-0
        [Fact]
        public async Task RTN22_ServerAuthTriggersReauth()
        {
            var authCallbackCount = 0;
            var capturedAuthMessages = new List<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id",
                    connectionKey: "connection-key"));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Recorded only now, as the spec does: the assertions below are about what the reauth emits,
            // not about the initial connect.
            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // When the client sends AUTH back, record it and answer with a CONNECTED carrying the new key.
            mockWs.OnMessageFromClient = message =>
            {
                if (message.Action != ProtocolMessage.MessageAction.Auth)
                {
                    return;
                }

                lock (capturedAuthMessages)
                {
                    capturedAuthMessages.Add(message);
                }

                mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id",
                    connectionKey: "connection-key-2"));
            };

            // The server requests re-authentication.
            mockWs.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges).Any(
                    change => change.Event == ConnectionEvent.Update),
                "the UPDATE event that signals reauth completion");

            // Called twice: once for the initial connect, once for the reauth.
            Volatile.Read(ref authCallbackCount).Should().Be(2);

            var observedAuth = UtsClients.Snapshot(capturedAuthMessages);
            observedAuth.Should().HaveCount(1);
            observedAuth[0].Auth.Should().NotBeNull();
            observedAuth[0].Auth.AccessToken.Should().Be("token-2");

            var observed = UtsClients.Snapshot(stateChanges);

            // The connection stayed CONNECTED throughout: no transition to any other state.
            observed.Where(change => change.Current != ConnectionState.Connected).Should().BeEmpty();

            // RTN24 - exactly one UPDATE event.
            observed.Where(change => change.Event == ConnectionEvent.Update).Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTN22/stays-connected-during-reauth-1
        [Fact]
        public async Task RTN22_StaysConnectedDuringReauth()
        {
            var authCallbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "conn-1",
                    connectionKey: "key-1"));
            });

            // Auto-respond to the client's AUTH with a CONNECTED, as the spec's setup does.
            mockWs.OnMessageFromClient = message =>
            {
                if (message.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-1",
                        connectionKey: "key-1-updated"));
                }
            };

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("reauth-token-" + count)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // The server sends AUTH.
            mockWs.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges).Count >= 1,
                "the first connection event raised by the server-initiated reauth");

            client.Connection.State.Should().Be(ConnectionState.Connected);

            var observed = UtsClients.Snapshot(stateChanges);
            observed.Should().HaveCount(1);
            observed[0].Event.Should().Be(ConnectionEvent.Update);
            observed[0].Current.Should().Be(ConnectionState.Connected);
            observed[0].Previous.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTN22a/forced-disconnect-reauth-failure-0
        [Fact]
        public async Task RTN22a_ForcedDisconnectReauthFailure()
        {
            var authCallbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "conn-1",
                    connectionKey: "key-1"));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("recovery-token-" + count)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // DISCONNECTED is transient here — RTN15h2 queues the CONNECTING that renews the token behind
            // it — so the waiter is registered before the message is injected rather than awaited after.
            var disconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Disconnected);

            // The server forcibly disconnects with a token error, simulating the reauth timeout.
            mockWs.SendToClient(ProtocolMessages.DisconnectedMessage(
                code: 40142,
                message: "Token expired",
                statusCode: 401));

            await disconnected;

            var observed = UtsClients.Snapshot(stateChanges);
            var disconnectedChange = observed.FirstOrDefault(
                change => change.Current == ConnectionState.Disconnected);

            disconnectedChange.Should().NotBeNull();
            disconnectedChange.Reason.Should().NotBeNull();
            disconnectedChange.Reason.Code.Should().Be(40142);
        }
    }
}
