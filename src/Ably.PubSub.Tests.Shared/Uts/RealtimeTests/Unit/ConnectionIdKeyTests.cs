// Connection ID and Key Tests (RTN8, RTN8a, RTN8b, RTN8c, RTN8d, RTN9, RTN9a, RTN9b, RTN9c, RTN9d)
// Derived from uts/realtime/unit/connection/connection_id_key_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving)

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
    public class ConnectionIdKeyTests : UtsUnitTestBase
    {
        public ConnectionIdKeyTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN8a/id-unset-until-connected-0
        [UtsFact]
        [Trait("spec", "RTN8a")]
        public async Task RTN8a_IdUnsetUntilConnected()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage("unique-conn-id-1", "conn-key-1")));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Before connecting, id should be null
            client.Connection.Id.Should().BeNull();

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            client.Connection.Id.Should().Be("unique-conn-id-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN9a/key-unset-until-connected-0
        [UtsFact]
        [Trait("spec", "RTN9a")]
        public async Task RTN9a_KeyUnsetUntilConnected()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage("unique-conn-id-1", "conn-key-1")));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Before connecting, key should be null
            client.Connection.Key.Should().BeNull();

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            client.Connection.Key.Should().Be("conn-key-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN8b/id-unique-per-connection-0
        [UtsFact]
        [Trait("spec", "RTN8b")]
        public async Task RTN8b_IdUniquePerConnection()
        {
            // Setup
            var connectionCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var count = Interlocked.Increment(ref connectionCount);
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage($"conn-id-{count}", $"conn-key-{count}"));
            });
            InstallMock(mockWs);

            var client1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var client2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client1.Connect();
            await UtsWait.AwaitConnectionState(client1.Connection, ConnectionState.Connected);

            client2.Connect();
            await UtsWait.AwaitConnectionState(client2.Connection, ConnectionState.Connected);

            // Assertions
            client1.Connection.Id.Should().NotBe(client2.Connection.Id);
            client1.Connection.Id.Should().Be("conn-id-1");
            client2.Connection.Id.Should().Be("conn-id-2");
            await CloseClient(client1);
            await CloseClient(client2);
        }

        // UTS: realtime/unit/RTN9b/key-unique-per-connection-0
        [UtsFact]
        [Trait("spec", "RTN9b")]
        public async Task RTN9b_KeyUniquePerConnection()
        {
            // Setup
            var connectionCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var count = Interlocked.Increment(ref connectionCount);
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage($"conn-id-{count}", $"conn-key-{count}"));
            });
            InstallMock(mockWs);

            var client1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var client2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client1.Connect();
            await UtsWait.AwaitConnectionState(client1.Connection, ConnectionState.Connected);

            client2.Connect();
            await UtsWait.AwaitConnectionState(client2.Connection, ConnectionState.Connected);

            // Assertions
            client1.Connection.Key.Should().NotBe(client2.Connection.Key);
            client1.Connection.Key.Should().Be("conn-key-1");
            client2.Connection.Key.Should().Be("conn-key-2");
            await CloseClient(client1);
            await CloseClient(client2);
        }

        // UTS: realtime/unit/RTN8d/id-null-after-closed-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RTN8d")]
        public async Task RTN8d_IdNullAfterClosed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Id.Should().Be("conn-id-1");

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Assertions
            // DEVIATION: spec requires null, the SDK returns an empty string (SDK-wide) - see deviations.md
            client.Connection.Id.Should().BeEmpty();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN9d/key-null-after-closed-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RTN9d")]
        public async Task RTN9d_KeyNullAfterClosed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(
                ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1")));
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Key.Should().Be("conn-key-1");

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Assertions
            // DEVIATION: spec requires null, the SDK returns an empty string (SDK-wide) - see deviations.md
            client.Connection.Key.Should().BeEmpty();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN8d/id-key-null-after-failed-1
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RTN8d")]
        public async Task RTN8d_IdKeyNullAfterFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithError(
                ProtocolMessages.ErrorMessage(80000, "Fatal error", 400)));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Assertions
            // DEVIATION: spec requires null, the SDK returns an empty string (SDK-wide) - see deviations.md
            client.Connection.Id.Should().BeEmpty();
            client.Connection.Key.Should().BeEmpty();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN8d/id-key-retained-in-suspended-2
        [UtsFact]
        [Trait("spec", "RTN8d")]
        public async Task RTN8d_IdKeyRetainedInSuspended()
        {
            // Setup
            EnableFakeTimers();

            // Connect successfully on the first attempt, then refuse all reconnection
            // attempts so the connection ends up suspended.
            var attempt = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Volatile.Read(ref attempt) == 0)
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-id-1", "conn-key-1"));
                }
                else
                {
                    conn.RespondWithRefused();
                }

                Interlocked.Increment(ref attempt);
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100),
                FallbackHosts = new string[0],
            });

            // NOTE: AWAIT_STATE disconnected / suspended could miss a transient state (each is left again on the next retry); the states are recorded before the stimulus and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.Id.Should().Be("conn-id-1");
            client.Connection.Key.Should().Be("conn-key-1");

            // Drop the transport; subsequent attempts are refused, moving to DISCONNECTED
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Advance past connectionStateTtl to reach SUSPENDED
            await AdvanceTime(121000);
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Suspended),
                description: "connection entered SUSPENDED");

            // Assertions
            // RTN8d, RTN9d: id and key are retained, not cleared
            client.Connection.Id.Should().Be("conn-id-1");
            client.Connection.Key.Should().Be("conn-key-1");
            await CloseClient(client);
        }
    }
}
