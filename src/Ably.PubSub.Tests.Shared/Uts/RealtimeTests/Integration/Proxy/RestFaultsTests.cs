// REST Fault Proxy Integration Tests (RSC10, RSC15m, REC2c2, RTL6)
// Derived from uts/realtime/integration/proxy/rest_faults.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/rest/unit/auth/token_renewal.md (RSC10), uts/rest/unit/fallback.md (RSC15m, REC2c2), uts/realtime/unit/channels/channel_publish.md (RTL6)
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
    public class RestFaultsTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 60000;

        public RestFaultsTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RSC10/token-renewal-on-401-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC10")]
        public async Task RSC10_TokenRenewalOn401()
        {
            // Setup
            // Track authCallback invocations
            var authCallbackCount = 0;

            // Create proxy session that returns 401 on the first channel request
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/channels/"),
                    ProxyRules.HttpRespond(401, new JObject { ["error"] = new JObject { ["code"] = 40142, ["statusCode"] = 401, ["message"] = "Token expired" } }),
                    times: 1,
                    comment: "RSC10: Return 401 on first channel request, then passthrough"),
            });

            // Use token auth with authCallback so the SDK can renew.
            // The authCallback creates its own inner Rest client pointed directly at the sandbox
            // to obtain a token, bypassing the proxy entirely.
            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(onInvoked: _ => Interlocked.Increment(ref authCallbackCount)),
            });

            var channelName = "test-RSC10-token-renewal-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish a message -- first request gets 401, SDK renews token, retries
            Func<Task> publish = () => channel.PublishAsync("test-event", "hello");

            // The publish should succeed (SDK transparently renewed and retried)
            await publish.Should().NotThrowAsync();

            // Assertions
            // Publish completed successfully (no error thrown)
            // ASSERT result IS successful (asserted above: the awaited publish did not throw)

            // authCallback was called at least twice (initial token + renewal after 401)
            Volatile.Read(ref authCallbackCount).Should().BeGreaterThanOrEqualTo(2);

            // Proxy event log shows two HTTP requests to the channel endpoint
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/channels/");
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(2);

            // First request was intercepted (got 401), second request passed through (got 2xx)
            var httpResponses = log.HttpResponses();
            httpResponses.Count.Should().BeGreaterThanOrEqualTo(2);
            httpResponses[0].Status.Should().Be(401);
            ((int)httpResponses[1].Status.Value).Should().BeOneOf(200, 201);
        }

        // UTS: realtime/proxy/RSC15m/http-503-no-fallback-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15m")]
        public async Task RSC15m_Http503NoFallback()
        {
            // Setup
            // Create proxy session that returns 503 on the first channel request
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/channels/"),
                    ProxyRules.HttpRespond(503, new JObject { ["error"] = new JObject { ["code"] = 50300, ["statusCode"] = 503, ["message"] = "Service temporarily unavailable" } }),
                    times: 1,
                    comment: "RSC15m: Return 503 on first channel request"),
            });

            // Use token auth with authCallback (Basic auth is prohibited over non-TLS per RSC18).
            // The authCallback creates its own inner Rest client pointed directly at the sandbox
            // to obtain a token, bypassing the proxy entirely.
            var client = ProxyRest(session, new ClientOptions { AuthCallback = SandboxTokenCallback() });

            var channelName = "test-RSC15m-503-error-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Try to publish a message -- should fail with 503 error
            Func<Task> publish = () => channel.PublishAsync("test-event", "hello");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // The error propagates to the caller with the correct error code
            error.Code.Should().Be(50300);
            ((int?)error.StatusCode).Should().Be(503);

            // Proxy event log shows only one HTTP request to the channel endpoint
            // (no fallback attempts since endpoint="localhost" disables fallback hosts)
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/channels/");
            httpRequests.Should().HaveCount(1);
        }

        // UTS: realtime/proxy/RTL6/publish-history-through-proxy-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL6")]
        public async Task RTL6_PublishHistoryThroughProxy()
        {
            // Setup
            // Create proxy session with no rules (pure passthrough)
            var session = await CreateProxySession();

            // Derive key parts for JWT signing
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);

            // Create Realtime client through proxy for publishing.
            // Uses a JWT authCallback: the callback signs a JWT locally (no outbound request needed).
            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var realtimeClient = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret))),
                AutoConnect = false,
            });

            // Create REST client through proxy for history retrieval.
            // Also uses JWT authCallback for the same reason.
            var restClient = ProxyRest(session, new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret))),
            });

            var channelName = "test-RTL6-publish-history-" + RandomId();
            var realtimeChannel = realtimeClient.Channels.Get(channelName);
            var restChannel = restClient.Channels.Get(channelName);

            // Test Steps
            // Connect Realtime client through proxy and wait until connected
            // connectAndWait() calls realtime_client.connect() and resolves once the connection
            // reaches the CONNECTED state (or rejects on FAILED/SUSPENDED).
            realtimeClient.Connect();
            await AwaitConnectionState(realtimeClient.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Attach to the channel
            AssertSucceeded(await realtimeChannel.AttachAsync(), "realtime_channel.attach()");
            await AwaitChannelState(realtimeChannel, ChannelState.Attached, TimeSpan.FromSeconds(10));

            // Publish a message via Realtime
            AssertSucceeded(await realtimeChannel.PublishAsync("test-msg", "hello world"), "realtime_channel.publish()");

            // Poll history via REST until the published message appears.
            // History is eventually consistent so a single immediate read may return nothing.
            var history = await UtsWait.PollUntil<PaginatedResult<Message>>(
                async () =>
                {
                    var result = await restChannel.HistoryAsync();
                    return result.Items.Count > 0 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "REST history has the published message");

            // Assertions
            // History contains the published message
            history.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            // Find the published message in history
            var publishedMsg = history.Items.FirstOrDefault(m => m.Name == "test-msg");
            publishedMsg.Should().NotBeNull();
            publishedMsg.Data.Should().Be("hello world");

            // Proxy event log shows both WebSocket and HTTP traffic
            var log = await session.GetLogAsync();

            // At least one WebSocket connection was made (Realtime client)
            var wsConnects = log.WsConnects();
            wsConnects.Count.Should().BeGreaterThanOrEqualTo(1);

            // At least one HTTP request was made (REST history call + token requests)
            var httpRequests = log.HttpRequests();
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(1);

            // Cleanup
            await CloseClient(realtimeClient);
        }
    }
}
