// Presence Re-entry Proxy Integration Tests (RTP17i, RTP17g)
// Derived from uts/realtime/integration/proxy/presence_reentry.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/presence/realtime_presence_reentry.md (RTP17i, RTP17g)
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
    public class PresenceReentryTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 90000;

        public PresenceReentryTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTP17i/reenter-on-non-resumed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP17i")]
        public async Task RTP17i_ReenterOnNonResumed()
        {
            // Setup
            var channelName = UniqueChannelName("test-rtp17i");

            // Extract key name and key secret from the provisioned API key
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);

            // Create proxy session with clean passthrough (no fault rules)
            var session = await CreateProxySession();

            // client: the presence member, connects through the proxy so we can inject ATTACHED
            // Needs a clientId for presence — use authCallback with JWT that includes clientId
            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret, clientId: "client-a"))),
                AutoConnect = false,
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Phase 1 — Establish real presence state
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            AssertSucceeded(await channel.Presence.EnterAsync("hello"), "channel.presence.enter()");

            // Phase 2 — Count PRESENCE frames in the log before injection
            var logBefore = await session.GetLogAsync();
            var presenceFramesBefore = logBefore.FramesToServer(ProtocolMessages.Presence).Count;

            // Phase 3 — Inject ATTACHED with resumed=false (flags=0)
            // This triggers RTP17i re-entry without needing an actual disconnect
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject
            {
                ["action"] = ProtocolMessages.Attached,
                ["channel"] = channelName,
                ["flags"] = 0,
                ["error"] = new JObject { ["code"] = 91001, ["statusCode"] = 500, ["message"] = "Continuity lost" },
            }));

            // Phase 4 — Poll until a new PRESENCE frame appears in the log
            await UtsWait.PollUntil(
                async () =>
                {
                    var log = await session.GetLogAsync();
                    return log.FramesToServer(ProtocolMessages.Presence).Count > presenceFramesBefore;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "a new PRESENCE frame was sent after the injected ATTACHED");

            // Assertions
            // Get final proxy log
            var logAfter = await session.GetLogAsync();
            var allPresenceFrames = logAfter.FramesToServer(ProtocolMessages.Presence);

            // At least one new PRESENCE frame was sent after the injection
            allPresenceFrames.Count.Should().BeGreaterThan(presenceFramesBefore);

            // The last (most recent) re-enter frame should contain the presence data
            var reenterFrame = allPresenceFrames[allPresenceFrames.Count - 1];
            reenterFrame.Message["presence"].Should().NotBeNull();
            ((JArray)reenterFrame.Message["presence"]).Count.Should().BeGreaterThanOrEqualTo(1);

            // RTP17g: re-enter uses stored clientId, data, and ENTER action
            var reenterMsg = reenterFrame.Message["presence"][0];
            ((string)reenterMsg["clientId"]).Should().Be("client-a");
            ((string)reenterMsg["data"]).Should().Be("hello");

            // ENTER
            ((int)reenterMsg["action"]).Should().Be(2);

            // Channel should still be attached and connection still connected
            channel.State.Should().Be(ChannelState.Attached);
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Cleanup
            await CloseClient(client);
        }

        // UTS: realtime/proxy/RTP17i/reenter-after-disconnect-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTP17i", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP17i")]
        public async Task RTP17i_ReenterAfterDisconnect()
        {
            // Setup
            var channelName = UniqueChannelName("test-rtp17i-real");

            // Extract key name and key secret from the provisioned API key
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);

            // Create proxy session with two fault rules:
            // 1. Close the WebSocket 3s after connect (giving time to attach + enter presence)
            // 2. Replace the 2nd ATTACHED on the channel with a non-resumed one
            var secondAttachedMatch = ProxyRules.WsFrameToClient(ProtocolMessages.Attached, channelName);
            secondAttachedMatch["count"] = 2;
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.DelayAfterWsConnect(3000),
                    ProxyRules.Close(),
                    times: 1,
                    comment: "RTP17i: Close WebSocket after 3s to trigger reconnect"),
                ProxyRules.Rule(
                    secondAttachedMatch,
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = ProtocolMessages.Attached,
                        ["channel"] = channelName,
                        ["flags"] = 0,
                        ["error"] = new JObject { ["code"] = 91001, ["statusCode"] = 500, ["message"] = "Continuity lost" },
                    }),
                    times: 1,
                    comment: "RTP17i: Replace 2nd ATTACHED with non-resumed to trigger re-entry"),
            });

            // client_a: the presence member, connects through the proxy
            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var clientA = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret, clientId: "client-a"))),
                AutoConnect = false,
            });

            var channelA = clientA.Channels.Get(channelName);

            // Test Steps
            // Phase 1 — Establish presence before the proxy closes the connection
            // NOTE: states are recorded from before connect: DISCONNECTED may be left again at once, so AWAIT_STATE disconnected could miss it
            var states = UtsRecord.ConnectionStates(clientA.Connection);
            clientA.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");
            AssertSucceeded(await channelA.Presence.EnterAsync("hello"), "channel_a.presence.enter()");

            // Phase 2 — Wait for the temporal trigger to fire (at T+3s) and for reconnect
            // The proxy's delay_after_ws_connect rule closes the WebSocket at T+3s
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(100),
                description: "connection entered DISCONNECTED after the proxy closed the WebSocket");
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(100),
                description: "connection re-entered CONNECTED after DISCONNECTED");

            // Wait for the channel to reattach (the 2nd ATTACHED will be replaced with non-resumed)
            await AwaitChannelState(channelA, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // Phase 3 — Poll until a PRESENCE frame appears in the log after the 2nd ws_connect
            await UtsWait.PollUntil(
                async () =>
                {
                    var log = await session.GetLogAsync();
                    var wsConnects = log.WsConnects();
                    if (wsConnects.Count < 2)
                    {
                        return false;
                    }

                    var secondConnectTime = wsConnects[1].Timestamp;
                    var presenceAfterReconnect = log.FramesToServer(ProtocolMessages.Presence).Where(e => e.Timestamp > secondConnectTime).ToList();
                    return presenceAfterReconnect.Count > 0;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "a PRESENCE frame was sent after the second ws_connect");

            // Assertions
            // Get final proxy log
            var finalLog = await session.GetLogAsync();
            var finalWsConnects = finalLog.WsConnects();
            var finalSecondConnectTime = finalWsConnects[1].Timestamp;

            var reenterFrames = finalLog.FramesToServer(ProtocolMessages.Presence).Where(e => e.Timestamp > finalSecondConnectTime).ToList();

            reenterFrames.Count.Should().BeGreaterThanOrEqualTo(1);

            // Check the first re-enter frame
            var reenterFrame = reenterFrames[0];
            reenterFrame.Message["presence"].Should().NotBeNull();
            ((JArray)reenterFrame.Message["presence"]).Count.Should().BeGreaterThanOrEqualTo(1);

            // RTP17g: re-enter uses stored clientId, data, and ENTER action
            var reenterMsg = reenterFrame.Message["presence"][0];
            ((string)reenterMsg["clientId"]).Should().Be("client-a");
            ((string)reenterMsg["data"]).Should().Be("hello");

            // ENTER
            ((int)reenterMsg["action"]).Should().Be(2);

            // Channel is still attached and connection is still connected
            channelA.State.Should().Be(ChannelState.Attached);
            clientA.Connection.State.Should().Be(ConnectionState.Connected);

            // Cleanup
            await CloseClient(clientA);
        }
    }
}
