using System;
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
    /// Derived from uts/realtime/unit/connection/connection_id_key_test.md in ably/specification.
    ///
    /// Spec points: RTN8, RTN8a, RTN8b, RTN8c, RTN8d, RTN9, RTN9a, RTN9b, RTN9c, RTN9d
    ///
    /// Two translation notes that apply across the file:
    ///
    /// The spec's "id IS null" / "key IS null" after a terminal state is asserted here as
    /// <c>BeNullOrEmpty()</c>. The id and key are genuinely null before a client has ever connected,
    /// which is what RTN8a and RTN9a assert; but once a connection has existed,
    /// <c>RealtimeState.ConnectionData.ClearKeyAndId()</c> writes <c>string.Empty</c> rather than
    /// null, which is how this SDK represents an absent string throughout (the same choice is
    /// documented on <c>Connection.CreateRecoveryKey</c>). The behaviour RTN8d and RTN9d are about —
    /// the value being cleared rather than retained — is what the assertion pins: it still fails if
    /// the SDK kept "conn-id-1".
    ///
    /// The spec's <c>CLOSE_CLIENT(client)</c> is <see cref="UtsTestBase"/>'s teardown, which disposes
    /// every client built through <c>RealtimeClient(...)</c>, including when the test throws.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionIdKeyTests : UtsTestBase
    {
        public ConnectionIdKeyTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN8a/id-unset-until-connected-0
        [Fact]
        public async Task RTN8a_IdUnsetUntilConnected()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "unique-conn-id-1",
                        connectionKey: "conn-key-1")));

            var client = RealtimeClient(mockWs);

            // Before connecting, id should be null.
            client.Connection.Id.Should().BeNull();

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.Id.Should().Be("unique-conn-id-1");
        }

        // UTS: realtime/unit/RTN9a/key-unset-until-connected-0
        [Fact]
        public async Task RTN9a_KeyUnsetUntilConnected()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "unique-conn-id-1",
                        connectionKey: "conn-key-1")));

            var client = RealtimeClient(mockWs);

            // Before connecting, key should be null.
            client.Connection.Key.Should().BeNull();

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.Key.Should().Be("conn-key-1");
        }

        // UTS: realtime/unit/RTN8b/id-unique-per-connection-0
        [Fact]
        public async Task RTN8b_IdUniquePerConnection()
        {
            var connectionCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionCount++;
                conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: $"conn-id-{connectionCount}",
                        connectionKey: $"conn-key-{connectionCount}"));
            });

            // One mock serves both clients, as the spec's single install_mock(mock_ws) does.
            var client1 = RealtimeClient(mockWs);
            var client2 = RealtimeClient(mockWs);

            client1.Connect();
            await UtsClients.AwaitConnectionState(client1.Connection, ConnectionState.Connected);

            client2.Connect();
            await UtsClients.AwaitConnectionState(client2.Connection, ConnectionState.Connected);

            client1.Connection.Id.Should().NotBe(client2.Connection.Id);
            client1.Connection.Id.Should().Be("conn-id-1");
            client2.Connection.Id.Should().Be("conn-id-2");
        }

        // UTS: realtime/unit/RTN9b/key-unique-per-connection-0
        [Fact]
        public async Task RTN9b_KeyUniquePerConnection()
        {
            var connectionCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionCount++;
                conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: $"conn-id-{connectionCount}",
                        connectionKey: $"conn-key-{connectionCount}"));
            });

            var client1 = RealtimeClient(mockWs);
            var client2 = RealtimeClient(mockWs);

            client1.Connect();
            await UtsClients.AwaitConnectionState(client1.Connection, ConnectionState.Connected);

            client2.Connect();
            await UtsClients.AwaitConnectionState(client2.Connection, ConnectionState.Connected);

            client1.Connection.Key.Should().NotBe(client2.Connection.Key);
            client1.Connection.Key.Should().Be("conn-key-1");
            client2.Connection.Key.Should().Be("conn-key-2");
        }

        // UTS: realtime/unit/RTN8d/id-null-after-closed-0
        [Fact]
        public async Task RTN8d_IdNullAfterClosed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Id.Should().Be("conn-id-1");

            // The spec's AWAIT client.close(). Close() only sends a CLOSE and waits for the server's
            // CLOSED, so the test has to answer it or the connection sits in CLOSING until the
            // realtimeRequestTimeout expires.
            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());
            await closed;

            client.Connection.Id.Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN9d/key-null-after-closed-0
        [Fact]
        public async Task RTN9d_KeyNullAfterClosed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-id-1",
                        connectionKey: "conn-key-1")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Key.Should().Be("conn-key-1");

            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());
            await closed;

            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN8d/id-key-null-after-failed-1
        [Fact]
        public async Task RTN8d_IdAndKeyNullAfterFailed()
        {
            // respond_with_error: the socket opens and the server then sends a connection-level ERROR
            // and closes. A statusCode of 400 is not retryable, so CONNECTING converts it to FAILED
            // rather than DISCONNECTED.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithError(
                    ProtocolMessages.ErrorMessage(80000, "Fatal error", 400)));

            var client = RealtimeClient(mockWs);

            // Registered before Connect(): the workflow consumes its command queue on its own thread,
            // so FAILED can be reached before the next statement runs.
            var failed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Failed);
            client.Connect();
            await failed;

            client.Connection.Id.Should().BeNullOrEmpty();
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN8d/id-key-retained-in-suspended-2
        [Fact]
        public async Task RTN8d_IdAndKeyRetainedInSuspended()
        {
            // Connect successfully on the first attempt, then refuse all reconnection attempts so the
            // connection ends up suspended.
            var attempt = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (attempt == 0)
                {
                    // maxIdleInterval 0 disables RTN23a idle detection, which measures against the
                    // same clock this test advances and would otherwise drop the connection itself.
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "conn-id-1",
                            connectionKey: "conn-key-1",
                            maxIdleInterval: 0));
                }
                else
                {
                    conn.RespondWithRefused();
                }

                attempt = attempt + 1;
            });

            // The spec's enable_fake_timers() / ADVANCE_TIME(121s), split the two ways the skill's
            // Timers section prescribes. RTN14e's connectionStateTtl is time the SDK *measures* —
            // AttemptsHelpers.ShouldSuspend compares ClientOptions.NowFunc against the first recorded
            // attempt — so TestClock reaches it and the ttl stays at its real 120s default. The wait
            // in DISCONNECTED before retrying is time the SDK *schedules* on a real timer, so it is
            // shortened through DisconnectedRetryTimeout instead.
            //
            // suspendedRetryTimeout is deliberately left at its default rather than the spec's 100ms:
            // on a real timer a 100ms retry would leave SUSPENDED before the assertions ran.
            // fallbackHosts is already empty by UtsClients default.
            var clock = new TestClock();
            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Id.Should().Be("conn-id-1");
            client.Connection.Key.Should().Be("conn-key-1");

            var suspended = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(15));

            // Drop the transport; subsequent attempts are refused, moving to DISCONNECTED.
            mockWs.SimulateDisconnect();

            // The RTN14e deadline is measured from the attempt the drop records, so the advance has to
            // come after that record exists — advancing first would move the recorded time along with
            // the clock and nothing would ever elapse. A second connection attempt proves the record
            // was taken, because the retry that produces it is queued after it.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "a second connection attempt after the transport dropped");

            clock.Advance(121000);

            await suspended;

            // RTN8d, RTN9d: id and key are retained, not cleared.
            client.Connection.Id.Should().Be("conn-id-1");
            client.Connection.Key.Should().Be("conn-key-1");
        }
    }
}
