// Connection Resume and Recovery Proxy Integration Tests (RTN15, RTN16) (RTN14h, RTN15a, RTN15b, RTN15c6, RTN15c7, RTN15h1, RTN15h3, RTN15j, RTN16d, RTN16l, RTN19a, RTN19a2)
// Derived from uts/realtime/integration/proxy/connection_resume.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/connection/connection_failures_test.md, uts/realtime/unit/connection/connection_recovery_test.md
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); JWT authCallbacks return TokenDetails (a string return is read as a TokenRequest, RSA8d); recorded state changes replace AWAIT_STATE for states the connection may leave again at once

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration.Proxy
{
    /// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("type", "integration")]
    public class ConnectionResumeTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 120000;

        public ConnectionResumeTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The specs' authCallback: (params) => RETURN generate_jwt(keyName: key_name, keySecret: key_secret)
        // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
        private Func<TokenParams, Task<object>> JwtAuthCallback()
        {
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);
            return tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret)));
        }

        // A frame match with the proxy's 1-based occurrence "count" (ProxyRules has no count parameter for frames).
        private static JObject WithCount(JObject match, int count)
        {
            match["count"] = count;
            return match;
        }

        // Polls the recorded states until they contain the sequence in order (a subsequence), for transient states.
        private static Task AwaitStateSequence(CaptureList<ConnectionState> states, TimeSpan timeout, string description, params ConnectionState[] sequence)
        {
            return UtsWait.PollUntil(
                () =>
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
                },
                timeout: timeout,
                interval: TimeSpan.FromMilliseconds(100),
                description: description);
        }

        // UTS: realtime/proxy/RTN15a/disconnect-triggers-resume-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15a")]
        public async Task RTN15a_DisconnectTriggersResume()
        {
            // Setup
            // Proxy rules: Close the WebSocket connection after a 1-second delay. This simulates an unexpected disconnect after the SDK has connected.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTN15a: Close WebSocket after 1s to trigger unexpected disconnect"),
            });

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // Register state listener BEFORE connecting so we capture all state transitions
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Connect through proxy
            client.Connect();

            // Wait for first connected (rule fires after 1s, then proxy closes connection)
            // SDK should reconnect and resume
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Count(s => s == ConnectionState.Connected) >= 2,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                description: "connected at least twice");

            // Assertions
            // State changes should include: connecting, connected, disconnected, connecting, connected
            var states = stateChanges.Snapshot();
            var disconnectedIdx = states.IndexOf(ConnectionState.Disconnected);
            disconnectedIdx.Should().BeGreaterThanOrEqualTo(0);

            // After the disconnected, there should be another connecting and connected
            var postDisconnectConnectingIdx = states.IndexOf(ConnectionState.Connecting, disconnectedIdx);
            postDisconnectConnectingIdx.Should().BeGreaterThan(disconnectedIdx);

            UtsAssert.ContainsInOrder(states, new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected });

            // Verify resume was attempted via proxy log
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);

            // Second WebSocket connection should include resume query parameter
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN15a/tcp-close-triggers-resume-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15a")]
        public async Task RTN15a_TcpCloseTriggersResume()
        {
            // Setup
            // Proxy rules: Close the underlying TCP connection (no WebSocket close frame) after a 1-second delay.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.Disconnect(),
                    times: 1,
                    comment: "RTN15a: Close TCP (no close frame) after 1s to trigger unexpected disconnect"),
            });

            // SDK config: Same as Test 6.
            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // Same as Test 6.
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);
            client.Connect();
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Count(s => s == ConnectionState.Connected) >= 2,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                description: "connected at least twice");

            // Assertions
            // Same as Test 6.
            var states = stateChanges.Snapshot();
            var disconnectedIdx = states.IndexOf(ConnectionState.Disconnected);
            disconnectedIdx.Should().BeGreaterThanOrEqualTo(0);

            var postDisconnectConnectingIdx = states.IndexOf(ConnectionState.Connecting, disconnectedIdx);
            postDisconnectConnectingIdx.Should().BeGreaterThan(disconnectedIdx);

            UtsAssert.ContainsInOrder(states, new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected });

            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // Cleanup
            // Same as Test 6.
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN15b/resume-preserves-connid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15b")]
        public async Task RTN15b_ResumePreservesConnid()
        {
            // Setup
            // Proxy rules: Close the WebSocket connection after a 1-second delay.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTN15b/c6: Close WebSocket after 1s to trigger resume"),
            });

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // NOTE: states are recorded from before connect, so the brief DISCONNECTED cannot be missed (AWAIT_STATE disconnected would hang if the state was already left)
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Record connection identity before disconnect
            var originalConnectionId = client.Connection.Id;
            var originalConnectionKey = client.Connection.Key;
            originalConnectionId.Should().NotBeNull();
            originalConnectionKey.Should().NotBeNull();

            // Proxy closes connection after 1s; wait for disconnected then reconnected
            await AwaitStateSequence(states, TimeSpan.FromSeconds(10), "connection entered DISCONNECTED", ConnectionState.Connected, ConnectionState.Disconnected);

            // Wait for SDK to resume
            await AwaitStateSequence(states, TimeSpan.FromSeconds(15), "connection re-entered CONNECTED", ConnectionState.Disconnected, ConnectionState.Connected);

            // Assertions
            // RTN15c6: Connection ID is preserved (successful resume)
            client.Connection.Id.Should().Be(originalConnectionId);

            // RTN15b: Second ws_connect URL includes resume={connectionKey}
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
            wsConnects[1].QueryParams["resume"].Should().Be(originalConnectionKey);

            // No error reason on successful resume
            client.Connection.ErrorReason.Should().BeNull();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN15c7/failed-resume-new-connid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15c7")]
        public async Task RTN15c7_FailedResumeNewConnid()
        {
            // Setup
            // Proxy rules: Two rules work together:
            // 1. Close the WebSocket connection after 1 second (fires once) to trigger a resume attempt.
            // 2. Replace the 2nd CONNECTED message (the resume response) with a crafted one that has a different connectionId and an error, simulating a failed resume.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTN15c7: Close WebSocket after 1s to trigger resume attempt"),
                ProxyRules.Rule(
                    WithCount(ProxyRules.WsFrameToClient(ProtocolMessages.Connected), 2),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = 4,
                        ["connectionId"] = "proxy-injected-new-id",
                        ["connectionKey"] = "proxy-injected-new-key",
                        ["connectionDetails"] = new JObject
                        {
                            ["connectionKey"] = "proxy-injected-new-key",
                            ["clientId"] = JValue.CreateNull(),
                            ["maxMessageSize"] = 65536,
                            ["maxInboundRate"] = 250,
                            ["maxOutboundRate"] = 100,
                            ["maxFrameSize"] = 524288,
                            ["serverId"] = "test-server",
                            ["connectionStateTtl"] = 120000,
                            ["maxIdleInterval"] = 15000,
                        },
                        ["error"] = new JObject { ["code"] = 80008, ["statusCode"] = 400, ["message"] = "Unable to recover connection" },
                    }),
                    times: 1,
                    comment: "RTN15c7: Replace 2nd CONNECTED with failed resume (different connectionId + error 80008)"),
            });

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // NOTE: states are recorded from before connect, so the brief DISCONNECTED cannot be missed (AWAIT_STATE disconnected would hang if the state was already left)
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Connect through proxy — first CONNECTED passes through normally
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Record original identity
            var originalConnectionId = client.Connection.Id;
            originalConnectionId.Should().NotBeNull();
            originalConnectionId.Should().NotBe("proxy-injected-new-id");

            // Proxy closes connection after 1s; wait for disconnected then reconnected
            // SDK reconnects, but proxy replaces the CONNECTED response with a new connectionId
            // SDK should still reach CONNECTED, but with the new identity
            await AwaitStateSequence(states, TimeSpan.FromSeconds(10), "connection entered DISCONNECTED", ConnectionState.Connected, ConnectionState.Disconnected);

            await AwaitStateSequence(states, TimeSpan.FromSeconds(15), "connection re-entered CONNECTED", ConnectionState.Disconnected, ConnectionState.Connected);

            // Assertions
            // RTN15c7: Connection ID changed (resume failed, got new connection)
            client.Connection.Id.Should().Be("proxy-injected-new-id");
            client.Connection.Id.Should().NotBe(originalConnectionId);

            // Connection key updated to the new one
            client.Connection.Key.Should().Be("proxy-injected-new-key");

            // Error reason is set indicating why resume failed
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);

            // Connection is still CONNECTED (not FAILED — the server gave a new connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Verify resume was attempted in the proxy log
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN15h1/token-error-nonrenewable-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN15h1", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15h1")]
        public async Task RTN15h1_TokenErrorNonrenewableFailed()
        {
            // Setup
            // Proxy rules: After the initial WebSocket connection is established, wait 1 second then inject a DISCONNECTED message with token error and close the connection.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.InjectToClientAndClose(new JObject
                    {
                        ["action"] = 6,
                        ["error"] = new JObject { ["code"] = 40142, ["statusCode"] = 401, ["message"] = "Token expired" },
                    }),
                    times: 1,
                    comment: "RTN15h1: Inject DISCONNECTED with token error (40142) after 1s"),
            });

            // Token provisioning: Obtain a real token from the sandbox so the initial connection succeeds, then use it without any renewal capability.
            var tokenDetails = await RequestTokenFromSandbox();
            var tokenString = tokenDetails.Token;

            // SDK config: Use the token string directly — no key, no authCallback. This makes the token non-renewable.
            var client = ProxyRealtime(session, new ClientOptions { Token = tokenString, AutoConnect = false });

            // Test Steps
            // Connect through proxy — initial connection succeeds with the real token
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Record state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // After 1s the proxy injects DISCONNECTED with 40142 and closes the socket.
            // The SDK has a non-renewable token, so it cannot renew -> FAILED.
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            // RTN15h1: Ended in FAILED state
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason reflects the token error
            // NOTE: ably-js reports error code 40171 ("Token not renewable") rather than the injected
            // 40142, because the SDK detects it has no means to renew (no key, no authCallback, no
            // authUrl) and substitutes a more specific error code before transitioning to FAILED.
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40171);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);

            // State changes should show the transition to FAILED
            // (may pass through DISCONNECTED briefly before FAILED)
            stateChanges.Snapshot().Should().Contain(ConnectionState.Failed);

            // Cleanup
            // No need to close — already in FAILED state
        }

        // UTS: realtime/proxy/RTN15h3/non-token-error-reconnects-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15h3")]
        public async Task RTN15h3_NonTokenErrorReconnects()
        {
            // Setup
            // Proxy rules: After the initial WebSocket connection, wait 1 second then inject a DISCONNECTED message with a non-token error and close the connection. Only fire once — the reconnection attempt passes through cleanly.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.InjectToClientAndClose(new JObject
                    {
                        ["action"] = 6,
                        ["error"] = new JObject { ["code"] = 80003, ["statusCode"] = 500, ["message"] = "Service temporarily unavailable" },
                    }),
                    times: 1,
                    comment: "RTN15h3: Inject DISCONNECTED with non-token error (80003) after 1s, once only"),
            });

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Record state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // After 1s the proxy injects DISCONNECTED with non-token error and closes.
            // The rule fires once, so the reconnection attempt passes through to the real server.

            // Wait for DISCONNECTED (from the injected message)
            // NOTE: the recorded states are polled: DISCONNECTED may be left again at once, so AWAIT_STATE disconnected could be missed
            await AwaitStateSequence(stateChanges, TimeSpan.FromSeconds(10), "connection entered DISCONNECTED", ConnectionState.Disconnected);

            // SDK should automatically reconnect
            await AwaitStateSequence(stateChanges, TimeSpan.FromSeconds(15), "connection re-entered CONNECTED", ConnectionState.Disconnected, ConnectionState.Connected);

            // Assertions
            // RTN15h3: SDK reconnected successfully (not FAILED)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // State changes should show: disconnected -> connecting -> connected
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected });

            // Verify resume was attempted
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // No error reason after successful reconnection
            client.Connection.ErrorReason.Should().BeNull();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN15j/fatal-error-established-conn-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN15j")]
        public async Task RTN15j_FatalErrorEstablishedConn()
        {
            // Setup
            // Proxy rules: None initially (passthrough). The ERROR is injected imperatively after connection and channel attachment complete.
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Attach two channels in parallel
            var channelA = client.Channels.Get(UniqueChannelName("fatal-error-a"));
            var channelB = client.Channels.Get(UniqueChannelName("fatal-error-b"));
            var attachResults = await UtsWait.WithTimeout(Task.WhenAll(channelA.AttachAsync(), channelB.AttachAsync()), TimeSpan.FromSeconds(15), "both channels attach");
            AssertSucceeded(attachResults[0], "channel_a.attach()");
            AssertSucceeded(attachResults[1], "channel_b.attach()");

            // Record state changes
            var connectionStateChanges = UtsRecord.ConnectionStates(client.Connection);
            var channelAStateChanges = UtsRecord.ChannelStates(channelA);
            var channelBStateChanges = UtsRecord.ChannelStates(channelB);

            // Inject a connection-level ERROR via proxy imperative action
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject
            {
                ["action"] = 9,
                ["error"] = new JObject { ["code"] = 50000, ["statusCode"] = 500, ["message"] = "Internal server error" },
            }));

            // SDK should transition to FAILED
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            // RTN15j: Connection is in FAILED state
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Connection errorReason has the injected error
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(500);

            // Both channels transitioned to FAILED
            channelA.State.Should().Be(ChannelState.Failed);
            channelB.State.Should().Be(ChannelState.Failed);

            // Channel errors match the connection error
            channelA.ErrorReason.Should().NotBeNull();
            channelA.ErrorReason.Code.Should().Be(50000);
            channelB.ErrorReason.Should().NotBeNull();
            channelB.ErrorReason.Code.Should().Be(50000);

            // State change sequences
            connectionStateChanges.Snapshot().Should().Contain(ConnectionState.Failed);
            channelAStateChanges.Snapshot().Should().Contain(ChannelState.Failed);
            channelBStateChanges.Snapshot().Should().Contain(ChannelState.Failed);

            // No reconnection attempted — only the original ws_connect in the proxy log
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Should().HaveCount(1);

            // Cleanup
            // No need to close — already in FAILED state
        }

        // UTS: realtime/proxy/RTN14h/resume-after-ttl-expiry-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14h", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14h")]
        public async Task RTN14h_ResumeAfterTtlExpiry()
        {
            // Setup
            // Proxy rules: Three rules work together:
            // 1. Replace the first CONNECTED message to inject short connectionStateTtl (2000ms) and maxIdleInterval (15000ms) into connectionDetails, and set a known connectionId so we can verify the final id differs.
            // 2. Close the WebSocket connection after 1 second (fires once). At this point the client enters DISCONNECTED with connectionStateTtl=2000ms.
            // 3. Refuse the second ws_connect (fires once). This keeps the client in DISCONNECTED while the TTL clock runs out, so when the TTL expires the client transitions to SUSPENDED. The third ws_connect (when the suspendedRetryTimeout fires) should be a fresh connection.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    WithCount(ProxyRules.WsFrameToClient(ProtocolMessages.Connected), 1),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = 4,
                        ["connectionId"] = "proxy-ttl-test-id",
                        ["connectionKey"] = "__PASSTHROUGH__",
                        ["connectionDetails"] = new JObject
                        {
                            ["connectionKey"] = "__PASSTHROUGH__",
                            ["clientId"] = JValue.CreateNull(),
                            ["maxMessageSize"] = 65536,
                            ["maxInboundRate"] = 250,
                            ["maxOutboundRate"] = 100,
                            ["maxFrameSize"] = 524288,
                            ["serverId"] = "test-server",
                            ["connectionStateTtl"] = 2000,
                            ["maxIdleInterval"] = 15000,
                        },
                    }),
                    times: 1,
                    comment: "RTN14h: Replace 1st CONNECTED to set short connectionStateTtl (2s) and known connectionId"),
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(1000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTN14h: Close connection after 1s — client enters DISCONNECTED with 2s TTL"),
                ProxyRules.Rule(
                    ProxyRules.WsConnect(count: 2),
                    ProxyRules.RefuseConnection(),
                    times: 1,
                    comment: "RTN14h: Refuse 2nd ws_connect — keeps client disconnected until TTL expires and SUSPENDED fires"),
            });

            // SDK config: Use a short suspendedRetryTimeout so the test doesn't wait long after SUSPENDED.
            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(1000),
            });

            // Test Steps
            // NOTE: states are recorded from before connect: SUSPENDED lasts only suspendedRetryTimeout, so the record is polled instead of AWAIT_STATE
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Connect through proxy — first CONNECTED is replaced with short TTL and known connectionId
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Verify proxy-injected connectionId
            client.Connection.Id.Should().Be("proxy-ttl-test-id");

            // T=1s: proxy closes connection -> DISCONNECTED
            // T=1-3s: retry attempt is refused -> stays DISCONNECTED
            // T=3s: connectionStateTtl(2s) expires -> SUSPENDED
            // T=4s: suspendedRetryTimeout(1s) fires -> ws_connect that still attempts a
            //       resume (RTN14h); the server has discarded the state, so it responds
            //       with a new connectionId -> CONNECTED
            await AwaitStateSequence(states, TimeSpan.FromSeconds(15), "connection entered SUSPENDED", ConnectionState.Suspended);

            // After suspended, SDK makes a fresh connection
            await AwaitStateSequence(states, TimeSpan.FromSeconds(15), "connection re-entered CONNECTED after SUSPENDED", ConnectionState.Suspended, ConnectionState.Connected);

            // Assertions
            // The server discarded the connection state, so the resume failed server-side
            // and the connection ID changed.
            client.Connection.Id.Should().NotBe("proxy-ttl-test-id");

            // Verify the proxy log shows at least 3 ws_connects
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(3);

            // First ws_connect: initial — no resume
            wsConnects[0].QueryParams.ContainsKey("resume").Should().BeFalse();

            // RTN14h: the reconnection made after the TTL expired and the connection became
            // suspended still attempts a resume, so it carries the resume query param.
            var lastWsConnect = wsConnects[wsConnects.Count - 1];
            lastWsConnect.QueryParams.ContainsKey("resume").Should().BeTrue();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN19a/unacked-resent-on-resume-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN19a")]
        public async Task RTN19a_UnackedResentOnResume()
        {
            // Setup
            // Proxy rules: Suppress the first ACK (action 1) from the server. This causes the SDK to have an unacknowledged message when the disconnect occurs.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Ack),
                    ProxyRules.Suppress(),
                    times: 1,
                    comment: "RTN19a: Suppress the first ACK so the SDK has a pending unacked message"),
            });

            var client = ProxyRealtime(session, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Test Steps
            // NOTE: states are recorded from before connect: AWAIT_STATE connected would return at once (the connection is still CONNECTED until the proxy close is seen), so the re-entry is polled on the record
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Attach a channel
            var channel = client.Channels.Get("test-resend-unacked");
            _ = channel.AttachAsync();
            await AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // Start a publish — do NOT await it yet.
            // The message is sent to the server, but the ACK is suppressed by the proxy rule.
            var publishFuture = channel.PublishAsync("event", "test-data");

            // Poll the proxy log until we can confirm both:
            //   (a) the MESSAGE frame has been sent client->server (action==15)
            //   (b) the ACK frame has been suppressed server->client (action==1 with ruleMatched)
            // This avoids a fixed sleep and ensures the disconnect fires at the right moment.
            await UtsWait.PollUntil(
                async () =>
                {
                    var log = await session.GetLogAsync();
                    var messageSent = log.FramesToServer(ProtocolMessages.Message).Count > 0;
                    var ackSuppressed = log.FramesToClient(ProtocolMessages.Ack).Any(e => e.RuleMatched != null);
                    return messageSent && ackSuppressed;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "MESSAGE frame sent and ACK suppressed");

            // Close the connection — the SDK has an unacked message pending
            await session.TriggerActionAsync(ProxyRules.Close());

            // SDK reconnects and resumes (the ACK suppression rule already fired once,
            // so the reconnected session passes ACKs through normally)
            await AwaitStateSequence(states, TimeSpan.FromSeconds(15), "connection reconnected after the proxy close", ConnectionState.Connected, ConnectionState.Disconnected, ConnectionState.Connected);

            // Now await the publish — it should complete successfully after the message
            // is resent on the new transport and ACKed
            var publishResult = await UtsWait.WithTimeout(publishFuture, TimeSpan.FromSeconds(15), "the publish completes after the resend");

            // Assertions
            // The publish completed successfully (no exception thrown)
            publishFuture.IsCompleted.Should().BeTrue();
            publishResult.IsSuccess.Should().BeTrue("AWAIT publish_future succeeds ({0})", publishResult.Error?.Message);

            // Verify resume occurred
            var finalLog = await session.GetLogAsync();
            var wsConnects = finalLog.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // RTN19a: The MESSAGE frame was sent on both transports (original + resend)
            // NOTE: the spec's log type "ws_frame_to_server" is the typed log's client_to_server ws_frame
            var messageFrames = finalLog.FramesToServer(ProtocolMessages.Message);
            messageFrames.Count.Should().BeGreaterThanOrEqualTo(2);

            // RTN19a2: On successful resume, the resent message has the same msgSerial
            ((long?)messageFrames[0].Message["msgSerial"]).Should().Be((long?)messageFrames[1].Message["msgSerial"]);

            // Successful resume: connectionId preserved
            wsConnects[1].QueryParams.ContainsKey("resume").Should().BeTrue();

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTN16d/recovery-preserves-connid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN16d")]
        public async Task RTN16d_RecoveryPreservesConnid()
        {
            // Setup
            // Step 1: Establish an initial connection and obtain a recovery key.
            // Use a direct proxy session (passthrough, no rules) to connect to the sandbox, attach a channel, and capture the recovery key.
            var session1 = await CreateProxySession();

            var client1 = ProxyRealtime(session1, new ClientOptions { AuthCallback = JwtAuthCallback(), AutoConnect = false });

            // Step 2: Create a second client using the recovery key.
            // A second proxy session is used so we can inspect the recover query parameter in the log.
            var session2 = await CreateProxySession();

            // Test Steps
            // --- Phase 1: Obtain recovery key from first client ---
            // NOTE: states are recorded from before connect, so the DISCONNECTED after the proxy close cannot be missed
            var states1 = UtsRecord.ConnectionStates(client1.Connection);
            client1.Connect();
            await AwaitConnectionState(client1.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            var originalConnectionId = client1.Connection.Id;
            var originalConnectionKey = client1.Connection.Key;
            originalConnectionId.Should().NotBeNull();

            // Attach a channel so it appears in the recovery key
            var channel1 = client1.Channels.Get(UniqueChannelName("recovery-test"));
            _ = channel1.AttachAsync();
            await AwaitChannelState(channel1, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // Get the recovery key
            var recoveryKey = client1.Connection.CreateRecoveryKey();
            recoveryKey.Should().NotBeNull();

            // Close the first client's transport WITHOUT closing the Ably connection gracefully.
            // We want the server to keep the connection state alive for recovery.
            // Use session_1.trigger_action to forcibly close the WebSocket.
            await session1.TriggerActionAsync(ProxyRules.Close());

            // Wait for the client to detect the disconnect
            await AwaitStateSequence(states1, TimeSpan.FromSeconds(10), "client_1 entered DISCONNECTED", ConnectionState.Connected, ConnectionState.Disconnected);

            // Close client_1 without allowing it to reconnect
            client1.Connection.Close();
            await AwaitConnectionState(client1.Connection, ConnectionState.Closed, TimeSpan.FromSeconds(10));

            // session_1.close() is done by the base teardown

            // --- Phase 2: Recover using the recovery key ---
            var client2 = ProxyRealtime(session2, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
                Recover = recoveryKey,
            });

            client2.Connect();
            await AwaitConnectionState(client2.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            // RTN16d: Connection ID is preserved (same as original connection)
            client2.Connection.Id.Should().Be(originalConnectionId);

            // RTN16d: Connection key is updated (new key from server)
            client2.Connection.Key.Should().NotBeNull();
            client2.Connection.Key.Should().NotBe(originalConnectionKey);

            // RTN16k: Verify the recover query parameter was sent via proxy log
            var log = await session2.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(1);
            wsConnects[0].QueryParams["recover"].Should().Be(originalConnectionKey);

            // No resume param (this is recovery, not resume)
            wsConnects[0].QueryParams.ContainsKey("resume").Should().BeFalse();

            // No error on successful recovery
            client2.Connection.ErrorReason.Should().BeNull();

            // Cleanup
            await CloseClient(client2);
        }

        // UTS: realtime/proxy/RTN16l/recovery-failure-fresh-conn-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN16l")]
        public async Task RTN16l_RecoveryFailureFreshConn()
        {
            // Setup
            // Proxy rules: Replace the first CONNECTED response with one that has a different connectionId and an error, simulating the server rejecting the recovery attempt.
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    WithCount(ProxyRules.WsFrameToClient(ProtocolMessages.Connected), 1),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = 4,
                        ["connectionId"] = "recovery-failed-new-id",
                        ["connectionKey"] = "recovery-failed-new-key",
                        ["connectionDetails"] = new JObject
                        {
                            ["connectionKey"] = "recovery-failed-new-key",
                            ["clientId"] = JValue.CreateNull(),
                            ["maxMessageSize"] = 65536,
                            ["maxInboundRate"] = 250,
                            ["maxOutboundRate"] = 100,
                            ["maxFrameSize"] = 524288,
                            ["serverId"] = "test-server",
                            ["connectionStateTtl"] = 120000,
                            ["maxIdleInterval"] = 15000,
                        },
                        ["error"] = new JObject { ["code"] = 80008, ["statusCode"] = 400, ["message"] = "Unable to recover connection" },
                    }),
                    times: 1,
                    comment: "RTN16l: Replace CONNECTED with recovery failure (new connectionId + error 80008)"),
            });

            // SDK config: Use a fabricated recovery key. The connectionKey doesn't need to be valid since the proxy will replace the server response anyway.
            var fabricatedRecoveryKey = JsonConvert.SerializeObject(new JObject
            {
                ["connectionKey"] = "stale-old-key",
                ["msgSerial"] = 99,
                ["channelSerials"] = new JObject { ["old-channel"] = "old-serial" },
            });

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
                Recover = fabricatedRecoveryKey,
            });

            // Test Steps
            // Connect with the fabricated recovery key
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            // RTN16l + RTN15c7: Connection got a new ID (recovery failed)
            client.Connection.Id.Should().Be("recovery-failed-new-id");
            client.Connection.Key.Should().Be("recovery-failed-new-key");

            // RTN15c7: Error is set on the connection indicating recovery failure
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);

            // Connection is still CONNECTED (not FAILED — the server gave a new connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Verify the recover param was sent via proxy log
            var log = await session.GetLogAsync();
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(1);
            wsConnects[0].QueryParams["recover"].Should().Be("stale-old-key");

            // Cleanup
            await CloseClient(client);
        }
    }
}
