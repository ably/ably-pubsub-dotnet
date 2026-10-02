using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Derived from uts/realtime/unit/connection/update_events_test.md in ably/specification.
    ///
    /// Spec points: RTN24, and through it RTN4h, RTN21
    ///
    /// <para>
    /// The spec's <c>client.connection.on(ConnectionState.connected, ...)</c> and
    /// <c>client.connection.on(ConnectionEvent.update, ...)</c> are one overload here:
    /// <c>Connection</c> is an <c>EventEmitter&lt;ConnectionEvent, ConnectionStateChange&gt;</c> and
    /// every event, UPDATE included, is a member of <c>ConnectionEvent</c>. Listeners are registered
    /// before <c>Connect()</c> so the initial CONNECTED is observed rather than missed.
    /// </para>
    ///
    /// <para>
    /// Each spec step that ends in <c>WAIT(100)</c> or <c>WAIT(50)</c> becomes a polled premise on
    /// the recorded UPDATE events instead: the injected CONNECTED is handled on the realtime
    /// workflow queue, so a real delay would be both slower and less reliable than waiting for the
    /// event the step is waiting for.
    /// </para>
    ///
    /// <para>
    /// <c>ProtocolMessages.ConnectedMessage</c> covers connectionId, connectionKey, clientId,
    /// connectionStateTtl and maxIdleInterval. The two connectionDetails fields the spec's
    /// RTN24-override case adds on top - maxMessageSize and serverId - are set on the returned
    /// JObject directly, which is the whole point of the templates being wire-shaped.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class UpdateEventsTests : UtsTestBase
    {
        public UpdateEventsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN24/connected-emits-update-0
        [Fact]
        public async Task RTN24_ConnectedEmitsUpdate()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id-1",
                    connectionKey: "connection-key-1",
                    clientId: "client-123",
                    connectionStateTtl: 120000,
                    maxIdleInterval: 15000));
            });

            var client = RealtimeClient(mockWs);

            var connectedEvents = new List<ConnectionStateChange>();
            var updateEvents = new List<ConnectionStateChange>();

            client.Connection.On(ConnectionEvent.Connected, change => Record(connectedEvents, change));
            client.Connection.On(ConnectionEvent.Update, change => Record(updateEvents, change));

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            UtsClients.Snapshot(connectedEvents).Should().HaveCount(1);
            UtsClients.Snapshot(updateEvents).Should().BeEmpty();

            // The server sends another CONNECTED, as it does after a reauth. connectionId is a
            // top-level ProtocolMessage field, not part of connectionDetails, so it never changes for
            // an in-progress connection.
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage(
                connectionId: "connection-id-1",
                connectionKey: "connection-key-1",
                clientId: "client-123",
                connectionStateTtl: 120000,
                maxIdleInterval: 20000));

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(updateEvents).Count >= 1,
                "the UPDATE event for the second CONNECTED",
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Connected);

            // No additional CONNECTED event was emitted (RTN4h).
            UtsClients.Snapshot(connectedEvents).Should().HaveCount(1);

            var updates = UtsClients.Snapshot(updateEvents);
            updates.Should().HaveCount(1);

            updates[0].Previous.Should().Be(ConnectionState.Connected);
            updates[0].Current.Should().Be(ConnectionState.Connected);
            updates[0].Reason.Should().BeNull();

            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");
        }

        // UTS: realtime/unit/RTN24/update-event-with-error-1
        [Fact]
        public async Task RTN24_UpdateEventWithError()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id-1",
                    connectionKey: "connection-key-1",
                    connectionStateTtl: 120000,
                    maxIdleInterval: 15000));
            });

            var client = RealtimeClient(mockWs);

            var updateEvents = new List<ConnectionStateChange>();
            client.Connection.On(ConnectionEvent.Update, change => Record(updateEvents, change));

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            // CONNECTED carrying an error: the token was renewed because it had expired.
            var connectedWithError = ProtocolMessages.ConnectedMessage(
                connectionId: "connection-id-1",
                connectionKey: "connection-key-1",
                connectionStateTtl: 120000,
                maxIdleInterval: 15000);
            connectedWithError["error"] =
                ProtocolMessages.ErrorObject(40142, "Token expired; renewed automatically", 401);

            mockWs.ActiveConnection.SendToClient(connectedWithError);

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(updateEvents).Count >= 1,
                "the UPDATE event for the second CONNECTED",
                TimeSpan.FromSeconds(5));

            var updates = UtsClients.Snapshot(updateEvents);
            updates.Should().HaveCount(1);

            updates[0].Previous.Should().Be(ConnectionState.Connected);
            updates[0].Current.Should().Be(ConnectionState.Connected);
            updates[0].Reason.Should().NotBeNull();
            updates[0].Reason.Code.Should().Be(40142);
            ((int)updates[0].Reason.StatusCode.Value).Should().Be(401);
            updates[0].Reason.Message.Should().Contain("Token expired");
        }

        // UTS: realtime/unit/RTN24/connection-details-override-2
        [Fact]
        public async Task RTN24_ConnectionDetailsOverride()
        {
            var initialConnected = ProtocolMessages.ConnectedMessage(
                connectionId: "connection-id-1",
                connectionKey: "connection-key-1",
                clientId: "client-original",
                connectionStateTtl: 60000,
                maxIdleInterval: 10000);
            var initialDetails = (JObject)initialConnected["connectionDetails"];
            initialDetails["maxMessageSize"] = 16384;
            initialDetails["serverId"] = "server-1";

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(initialConnected);
            });

            var client = RealtimeClient(mockWs);

            // NOTE: the spec registers no listener here and ends the step with WAIT(100). RTN24's own
            // UPDATE event is the witness that the second CONNECTED has been processed, so it is
            // recorded instead of sleeping.
            var updateEvents = new List<ConnectionStateChange>();
            client.Connection.On(ConnectionEvent.Update, change => Record(updateEvents, change));

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromMilliseconds(60000));

            // A new CONNECTED with different connectionDetails. connectionId stays the same - the
            // server never changes it for an in-progress connection.
            var updatedConnected = ProtocolMessages.ConnectedMessage(
                connectionId: "connection-id-1",
                connectionKey: "connection-key-1",
                clientId: "client-updated",
                connectionStateTtl: 120000,
                maxIdleInterval: 20000);
            var updatedDetails = (JObject)updatedConnected["connectionDetails"];
            updatedDetails["maxMessageSize"] = 32768;
            updatedDetails["serverId"] = "server-2";

            mockWs.ActiveConnection.SendToClient(updatedConnected);

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(updateEvents).Count >= 1,
                "the UPDATE event for the second CONNECTED",
                TimeSpan.FromSeconds(5));

            // connection.id is unchanged: it is not inside connectionDetails. connection.key was
            // re-sent with the same value.
            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");

            // The spec leaves the connectionDetails accessors to the implementation and suggests
            // observing the override indirectly. .NET surfaces one of the overridden fields publicly,
            // so RTN21's "must override any stored details" is asserted through it directly.
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromMilliseconds(120000));

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTN24/no-duplicate-connected-event-3
        [Fact]
        public async Task RTN24_NoDuplicateConnectedEvent()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id-1",
                    connectionKey: "connection-key-1",
                    connectionStateTtl: 120000,
                    maxIdleInterval: 15000));
            });

            var client = RealtimeClient(mockWs);

            var allEvents = new List<(string Kind, ConnectionStateChange Change)>();
            var stateEvents = new[]
            {
                ConnectionEvent.Initialized,
                ConnectionEvent.Connecting,
                ConnectionEvent.Connected,
                ConnectionEvent.Disconnected,
                ConnectionEvent.Suspended,
                ConnectionEvent.Closing,
                ConnectionEvent.Closed,
                ConnectionEvent.Failed,
            };

            foreach (var stateEvent in stateEvents)
            {
                client.Connection.On(stateEvent, change =>
                {
                    lock (allEvents)
                    {
                        allEvents.Add(("state", change));
                    }
                });
            }

            client.Connection.On(ConnectionEvent.Update, change =>
            {
                lock (allEvents)
                {
                    allEvents.Add(("update", change));
                }
            });

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            var initialEventCount = UtsClients.Snapshot(allEvents).Count;

            for (var i = 0; i < 3; i++)
            {
                mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id-1",
                    connectionKey: "connection-key-1",
                    connectionStateTtl: 120000,
                    maxIdleInterval: 15000));

                // The spec's WAIT(50) between messages, as a polled premise.
                var expected = initialEventCount + i + 1;
                await UtsClients.PollUntil(
                    () => UtsClients.Snapshot(allEvents).Count >= expected,
                    $"connection event number {expected}",
                    TimeSpan.FromSeconds(5));
            }

            var newEvents = UtsClients.Snapshot(allEvents).Skip(initialEventCount).ToList();

            // Exactly one UPDATE per subsequent CONNECTED message, and nothing else.
            newEvents.Should().HaveCount(3);

            foreach (var observed in newEvents)
            {
                observed.Kind.Should().Be("update");
                observed.Change.Previous.Should().Be(ConnectionState.Connected);
                observed.Change.Current.Should().Be(ConnectionState.Connected);
            }

            // No additional CONNECTED state events were emitted: only the initial one.
            UtsClients.Snapshot(allEvents)
                .Where(e => e.Kind == "state" && e.Change.Current == ConnectionState.Connected)
                .Should().HaveCount(1);
        }

        private static void Record(List<ConnectionStateChange> changes, ConnectionStateChange change)
        {
            lock (changes)
            {
                changes.Add(change);
            }
        }
    }
}
