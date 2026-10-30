// UPDATE Events Tests (RTN24)
// Derived from uts/realtime/unit/connection/update_events_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: spec WAIT(n) after a server message rendered as ProcessPendingEvents (delivery wait, no fake clock)

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
    public class UpdateEventsTests : UtsUnitTestBase
    {
        public UpdateEventsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // A CONNECTED frame with the spec's connectionDetails fields (wire milliseconds); only the fields given are set.
        private static JObject ConnectedFrame(
            string connectionId,
            string connectionKey,
            long maxIdleInterval,
            long connectionStateTtl,
            string clientId = null,
            long? maxMessageSize = null,
            string serverId = null,
            JObject error = null,
            bool topLevelConnectionKey = false)
        {
            var details = new JObject
            {
                ["connectionKey"] = connectionKey,
                ["maxIdleInterval"] = maxIdleInterval,
                ["connectionStateTtl"] = connectionStateTtl,
            };

            if (clientId != null)
            {
                details["clientId"] = clientId;
            }

            if (maxMessageSize.HasValue)
            {
                details["maxMessageSize"] = maxMessageSize.Value;
            }

            if (serverId != null)
            {
                details["serverId"] = serverId;
            }

            var frame = new JObject
            {
                ["action"] = ProtocolMessages.Connected,
                ["connectionId"] = connectionId,
                ["connectionDetails"] = details,
            };

            if (topLevelConnectionKey)
            {
                frame["connectionKey"] = connectionKey;
            }

            if (error != null)
            {
                frame["error"] = error;
            }

            return frame;
        }

        // UTS: realtime/unit/RTN24/connected-emits-update-0
        [UtsFact]
        [Trait("spec", "RTN24")]
        public async Task RTN24_ConnectedEmitsUpdate()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 15000, 120000, clientId: "client-123", topLevelConnectionKey: true));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Track events
            var connectedEvents = new CaptureList<ConnectionStateChange>();
            var updateEvents = new CaptureList<ConnectionStateChange>();

            client.Connection.On(ConnectionEvent.Connected, change => connectedEvents.Add(change));
            client.Connection.On(ConnectionEvent.Update, change => updateEvents.Add(change));

            // Start connection
            client.Connect();

            // Wait for initial CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Verify initial connection
            connectedEvents.Count.Should().Be(1);
            updateEvents.Count.Should().Be(0);

            // Server sends another CONNECTED message (e.g., after reauth)
            // Note: connectionId is a top-level ProtocolMessage field, NOT inside
            // connectionDetails, so it never changes for an in-progress connection.
            mockWs.ActiveConnection.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 20000, 120000, clientId: "client-123"));

            // Wait for event to be processed
            // NOTE: spec WAIT(100) is a delivery wait: ProcessPendingEvents drains the queued CONNECTED
            await ProcessPendingEvents();

            // Assertions
            // State remains CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // No additional CONNECTED event was emitted
            connectedEvents.Count.Should().Be(1);

            // UPDATE event was emitted
            updateEvents.Count.Should().Be(1);

            // UPDATE event has correct structure
            var updateChange = updateEvents[0];
            updateChange.Previous.Should().Be(ConnectionState.Connected);
            updateChange.Current.Should().Be(ConnectionState.Connected);
            updateChange.Reason.Should().BeNull();

            // connection.id and connection.key are unchanged — connectionId is a
            // top-level ProtocolMessage field not inside connectionDetails, so RTN24's
            // "connectionDetails must override stored details" does not apply to it.
            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN24/update-event-with-error-1
        [UtsFact]
        [Trait("spec", "RTN24")]
        public async Task RTN24_UpdateEventWithError()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 15000, 120000, topLevelConnectionKey: true));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Track UPDATE events
            var updateEvents = new CaptureList<ConnectionStateChange>();

            client.Connection.On(ConnectionEvent.Update, change => updateEvents.Add(change));

            // Start connection
            client.Connect();

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Server sends CONNECTED with error (e.g., token was renewed due to expiry)
            mockWs.ActiveConnection.SendToClient(ConnectedFrame(
                "connection-id-1",
                "connection-key-1",
                15000,
                120000,
                error: ProtocolMessages.ErrorObject(40142, "Token expired; renewed automatically", 401)));

            // Wait for event to be processed
            // NOTE: spec WAIT(100) is a delivery wait: ProcessPendingEvents drains the queued CONNECTED
            await ProcessPendingEvents();

            // Assertions
            // UPDATE event was emitted
            updateEvents.Count.Should().Be(1);

            // UPDATE event has error reason
            var updateChange = updateEvents[0];
            updateChange.Previous.Should().Be(ConnectionState.Connected);
            updateChange.Current.Should().Be(ConnectionState.Connected);
            updateChange.Reason.Should().NotBeNull();
            updateChange.Reason.Code.Should().Be(40142);
            ((int?)updateChange.Reason.StatusCode).Should().Be(401);
            updateChange.Reason.Message.Should().Contain("Token expired");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN24/connection-details-override-2
        [UtsFact]
        [Trait("spec", "RTN24")]
        public async Task RTN24_ConnectionDetailsOverride()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 10000, 60000, clientId: "client-original", maxMessageSize: 16384, serverId: "server-1"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Verify initial connection
            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");

            // Server sends new CONNECTED with different connectionDetails (RTN24)
            // connectionId stays the same — the server never changes it for an
            // in-progress connection.
            mockWs.ActiveConnection.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 20000, 120000, clientId: "client-updated", maxMessageSize: 32768, serverId: "server-2"));

            // Wait for update to be processed
            // NOTE: spec WAIT(100) is a delivery wait: ProcessPendingEvents drains the queued CONNECTED
            await ProcessPendingEvents();

            // Assertions
            // connection.id is unchanged (not inside connectionDetails)
            client.Connection.Id.Should().Be("connection-id-1");
            client.Connection.Key.Should().Be("connection-key-1");

            // connectionDetails fields were overridden (RTN21)
            // The exact accessors for these details may vary by implementation.
            // The effect can be observed indirectly — e.g., the heartbeat timeout
            // changes when maxIdleInterval is overridden.
            // NOTE: no assertion: the spec names no accessor; Connection.ConnectionStateTtl is public, so it is asserted as the observable override
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromMilliseconds(120000));

            // State remains CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN24/no-duplicate-connected-event-3
        [UtsFact]
        [Trait("spec", "RTN24")]
        public async Task RTN24_NoDuplicateConnectedEvent()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 15000, 120000, topLevelConnectionKey: true));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Track all events
            var allEvents = new CaptureList<Tuple<string, ConnectionEvent, ConnectionStateChange>>();

            // Subscribe to all connection events
            foreach (var state in new[]
            {
                ConnectionEvent.Initialized,
                ConnectionEvent.Connecting,
                ConnectionEvent.Connected,
                ConnectionEvent.Disconnected,
                ConnectionEvent.Suspended,
                ConnectionEvent.Closing,
                ConnectionEvent.Closed,
                ConnectionEvent.Failed,
            })
            {
                var capturedState = state;
                client.Connection.On(capturedState, change => allEvents.Add(Tuple.Create("state", capturedState, change)));
            }

            // Also subscribe to UPDATE
            client.Connection.On(ConnectionEvent.Update, change => allEvents.Add(Tuple.Create("update", ConnectionEvent.Update, change)));

            // Start connection
            client.Connect();

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Record event count after initial connection
            var initialEventCount = allEvents.Count;

            // Send multiple CONNECTED messages (same connectionId — it never changes)
            foreach (var i in new[] { 1, 2, 3 })
            {
                mockWs.ActiveConnection.SendToClient(ConnectedFrame("connection-id-1", "connection-key-1", 15000, 120000));
                await ProcessPendingEvents();
            }

            // Assertions
            // Exactly 3 UPDATE events were added (one per subsequent CONNECTED message)
            var newEvents = allEvents.Snapshot().Skip(initialEventCount).ToList();
            newEvents.Count.Should().Be(3);

            // All new events are UPDATE events, not CONNECTED state events
            foreach (var newEvent in newEvents)
            {
                newEvent.Item1.Should().Be("update");
                newEvent.Item3.Previous.Should().Be(ConnectionState.Connected);
                newEvent.Item3.Current.Should().Be(ConnectionState.Connected);
            }

            // No additional CONNECTED state events were emitted
            var connectedStateEvents = allEvents.Snapshot().Where(e => e.Item1 == "state" && e.Item2 == ConnectionEvent.Connected).ToList();
            connectedStateEvents.Count.Should().Be(1); // Only the initial one
            await CloseClient(client);
        }
    }
}
