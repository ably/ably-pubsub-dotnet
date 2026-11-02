// Connection Opening Failures — Proxy Integration Tests (RTN14a, RTN14b, RTN14c, RTN14d, RTN14g)
// Derived from uts/realtime/integration/proxy/connection_open_failures.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/connection/connection_open_failures_test.md
// Disclosures: token auth via SandboxTokenCallback (basic auth needs TLS; the proxy hop is plain); infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

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
    public class ConnectionOpenFailuresTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ConnectionOpenFailuresTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN14a/fatal-connect-error-0
        // DEVIATION: see deviations.md
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14a")]
        public async Task RTN14a_FatalConnectError()
        {
            // Setup
            // Create proxy session that replaces the first CONNECTED with a fatal ERROR
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Connected),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = ProtocolMessages.Error,
                        ["error"] = new JObject { ["code"] = 40005, ["statusCode"] = 400, ["message"] = "Invalid key" },
                    }),
                    times: 1,
                    comment: "RTN14a: Replace CONNECTED with fatal ERROR"),
            });

            var client = ProxyRealtime(session, new ClientOptions { Key = ApiKey, AutoConnect = false });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // Wait for FAILED state
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set from the injected ERROR message
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40005);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(400);

            // State sequence includes CONNECTING -> FAILED
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Failed });

            // Connection ID/key not set (never received real CONNECTED)
            // DEVIATION: spec says null, SDK returns an empty string before the first CONNECTED - see deviations.md
            client.Connection.Id.Should().BeEmpty();
            client.Connection.Key.Should().BeEmpty();
        }

        // UTS: realtime/proxy/RTN14b/token-error-renew-reconnect-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14b")]
        public async Task RTN14b_TokenErrorRenewReconnect()
        {
            // Setup
            // Track authCallback invocations
            var authCallbackCount = 0;

            // Create proxy session that injects token error on first CONNECTED only
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Connected),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = ProtocolMessages.Error,
                        ["error"] = new JObject { ["code"] = 40142, ["statusCode"] = 401, ["message"] = "Token expired" },
                    }),
                    times: 1,
                    comment: "RTN14b: Token error on first connect, renewal should succeed"),
            });

            // Use token auth with authCallback so the SDK can renew
            // Request a token from the sandbox using the API key
            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey, onInvoked: _ => Interlocked.Increment(ref authCallbackCount)),
                AutoConnect = false,
            });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // SDK should see token error, renew token, reconnect, and reach CONNECTED
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(30));

            // Assertions
            // Successfully connected after token renewal
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Connection properties are set (from the real CONNECTED on second attempt)
            client.Connection.Id.Should().NotBeNull();
            client.Connection.Key.Should().NotBeNull();

            // authCallback was called at least twice (initial token + renewal)
            Volatile.Read(ref authCallbackCount).Should().BeGreaterThanOrEqualTo(2);

            // State sequence shows the SDK went through CONNECTING, then back to CONNECTING after error,
            // and finally reached CONNECTED
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });

            // Proxy event log shows two WebSocket connections
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);

            // No residual error reason on successful connection
            client.Connection.ErrorReason.Should().BeNull();

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTN14d/retry-after-refused-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14d")]
        public async Task RTN14d_RetryAfterRefused()
        {
            // Setup
            // Create proxy session that refuses the first WebSocket connection
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsConnect(count: 1),
                    ProxyRules.RefuseConnection(),
                    times: 1,
                    comment: "RTN14d: Refuse first WebSocket connection"),
            });

            var client = ProxyRealtime(session, new ClientOptions
            {
                Key = ApiKey,
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(2000),
            });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // SDK should fail on first attempt, go DISCONNECTED, retry, then reach CONNECTED
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(30));

            // Assertions
            // Successfully connected after retry
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Connection properties are set
            client.Connection.Id.Should().NotBeNull();
            client.Connection.Key.Should().NotBeNull();

            // State sequence shows CONNECTING -> DISCONNECTED -> CONNECTING -> CONNECTED
            UtsAssert.ContainsInOrder(
                stateChanges.Snapshot(),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                });

            // Proxy event log shows two WebSocket connection attempts
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTN14g/server-error-causes-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14g", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14g")]
        public async Task RTN14g_ServerErrorCausesFailed()
        {
            // Setup
            // Create proxy session that replaces the first CONNECTED with a server ERROR
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Connected),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = ProtocolMessages.Error,
                        ["error"] = new JObject { ["code"] = 50000, ["statusCode"] = 500, ["message"] = "Internal server error" },
                    }),
                    times: 1,
                    comment: "RTN14g: Connection-level ERROR (server error) during open"),
            });

            var client = ProxyRealtime(session, new ClientOptions { Key = ApiKey, AutoConnect = false });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // Wait for FAILED state
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set from the injected ERROR message
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(500);
            client.Connection.ErrorReason.Message.Should().Be("Internal server error");

            // State sequence includes CONNECTING -> FAILED
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Failed });

            // Connection ID/key not set
            client.Connection.Id.Should().BeNull();
            client.Connection.Key.Should().BeNull();
        }

        // UTS: realtime/proxy/RTN14c/connection-timeout-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14c", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14c")]
        public async Task RTN14c_ConnectionTimeout()
        {
            // Setup
            // Create proxy session that suppresses all CONNECTED messages
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Connected),
                    ProxyRules.Suppress(),
                    comment: "RTN14c: Suppress CONNECTED to force timeout"),
            });

            var client = ProxyRealtime(session, new ClientOptions
            {
                Key = ApiKey,
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(3000),
            });

            // Test Steps
            // Record state changes for sequence verification
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // SDK should time out waiting for CONNECTED and transition to DISCONNECTED
            await AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(15));

            // Assertions
            // Connection timed out and transitioned to DISCONNECTED
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // Error reason indicates timeout
            client.Connection.ErrorReason.Should().NotBeNull();
            var errorReason = client.Connection.ErrorReason;
            (errorReason.Message.Contains("timeout") || errorReason.Code == 50003 || errorReason.Code == 80003).Should().BeTrue("error reason indicates a timeout: {0}", errorReason);

            // State sequence includes CONNECTING -> DISCONNECTED
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Disconnected });

            // Connection ID/key not set (CONNECTED was never received)
            client.Connection.Id.Should().BeNull();
            client.Connection.Key.Should().BeNull();
        }
    }
}
