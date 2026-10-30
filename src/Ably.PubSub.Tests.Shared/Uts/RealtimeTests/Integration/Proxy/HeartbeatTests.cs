// Realtime Heartbeat — Proxy Integration Tests (RTN23a)
// Derived from uts/realtime/integration/proxy/heartbeat.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/connection/heartbeat_test.md
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Tests.Uts.Infra.Integration.Proxy;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration.Proxy
{
    /// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("type", "integration")]
    public class HeartbeatTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 75000;

        public HeartbeatTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN23a/heartbeat-starvation-reconnect-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN23a")]
        public async Task RTN23a_HeartbeatStarvationReconnect()
        {
            // Setup
            // Create proxy session that closes the WebSocket after 2s to simulate transport failure
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(2000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTN23a: Close WebSocket after 2s to simulate transport failure"),
            });

            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);

            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret))),
                AutoConnect = false,
            });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // SDK receives real CONNECTED from Ably (within the 2s before close fires)
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Capture connection details from the first connection
            var firstConnectionId = client.Connection.Id;
            var firstConnectionKey = client.Connection.Key;
            firstConnectionId.Should().NotBeNull();

            // The proxy closes the WebSocket after 2s. The SDK detects the close frame
            // immediately and transitions to DISCONNECTED, then automatically reconnects.
            // The close rule has times=1, so the second WS connection is unaffected.

            // Wait for DISCONNECTED
            await AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(15));

            // Wait for successful reconnection
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(30));

            // Assertions
            // Connection is re-established with new connection details
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNull();
            client.Connection.Key.Should().NotBeNull();

            // State sequence shows: connected -> disconnected -> reconnecting -> connected
            UtsAssert.ContainsInOrder(
                stateChanges.Snapshot(),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                });

            // Proxy event log confirms two WebSocket connections
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);

            // Second connection should include resume parameter (RTN15c)
            wsConnects[1].QueryParams.Should().ContainKey("resume");
            wsConnects[1].QueryParams["resume"].Should().NotBeNull();

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }
    }
}
