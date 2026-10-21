// Connection Auto Connect Tests (RTN3)
// Derived from uts/realtime/unit/connection/auto_connect_test.md
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
    public class AutoConnectTests : UtsUnitTestBase
    {
        public AutoConnectTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN3/auto-connect-true-0
        [UtsFact]
        [Trait("spec", "RTN3")]
        public async Task RTN3_AutoConnectTrue()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Test Steps
            // Create client with default autoConnect (true) — do NOT call connect()
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Wait for connection to complete
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection was established automatically
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be("connection-id");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN3/auto-connect-false-1
        [UtsFact]
        [Trait("spec", "RTN3")]
        public async Task RTN3_AutoConnectFalse()
        {
            // Setup
            var connectionAttempted = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Exchange(ref connectionAttempted, 1);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Test Steps
            // Create client with autoConnect: false
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Wait briefly to confirm no connection attempt is made
            await Task.Delay(500); // NOTE: spec real-time WAIT kept: a drain would weaken the negative check

            // Assertions
            // No connection was attempted
            (Volatile.Read(ref connectionAttempted) == 1).Should().BeFalse();

            // State remains INITIALIZED
            client.Connection.State.Should().Be(ConnectionState.Initialized);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN3/explicit-connect-after-false-2
        [UtsFact]
        [Trait("spec", "RTN3")]
        public async Task RTN3_ExplicitConnectAfterFalse()
        {
            // Setup
            var connectionAttempted = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Exchange(ref connectionAttempted, 1);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Test Steps
            // Create client with autoConnect: false
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Verify no connection yet
            client.Connection.State.Should().Be(ConnectionState.Initialized);
            (Volatile.Read(ref connectionAttempted) == 1).Should().BeFalse();

            // Explicitly connect
            client.Connect();

            // Wait for connection to complete
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection was established after explicit connect()
            (Volatile.Read(ref connectionAttempted) == 1).Should().BeTrue();
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }
    }
}
