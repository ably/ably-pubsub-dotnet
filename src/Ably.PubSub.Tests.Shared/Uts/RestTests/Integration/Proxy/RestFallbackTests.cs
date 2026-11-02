// REST Fallback Proxy Integration Tests (RSC15l, RSC15l2, RSC15l4, RSL1k4)
// Derived from uts/rest/integration/proxy/rest_fallback.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/rest/unit/fallback.md, uts/rest/unit/channel/idempotency.md
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); the spec's token_auth_callback(api_key) is SandboxTokenCallback(ApiKey) (a real TokenDetails requested directly from the sandbox over TLS, bypassing the proxy)

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

namespace Ably.PubSub.Tests.Uts.RestTests.Integration.Proxy
{
    /// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("type", "integration")]
    public class RestFallbackTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 60000;

        // AFTER EACH TEST: IF session IS NOT null: session.close() — done by the base teardown.
        public RestFallbackTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/proxy/RSC15l2/timeout-triggers-fallback-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l2")]
        public async Task RSC15l2_TimeoutTriggersFallback()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpDelay(20000),
                    times: 1,
                    comment: "RSC15l2: Delay first /time request beyond httpRequestTimeout"),
            });

            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                FallbackHosts = new[] { "localhost" },
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
                HttpRequestTimeout = TimeSpan.FromMilliseconds(3000),
            });

            // Test Steps
            var result = await client.TimeAsync();

            // Assertions
            // The request should succeed (retried on fallback after timeout)
            // ASSERT result IS number: guaranteed at compile time (TimeAsync returns DateTimeOffset, the SDK's server time)
            result.Should().NotBe(default(DateTimeOffset));

            // Proxy event log shows at least two HTTP requests to /time
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/time");
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(2);
        }

        // UTS: rest/proxy/RSC15l4/cloudfront-header-fallback-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC15l4", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l4")]
        public async Task RSC15l4_CloudfrontHeaderFallback()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpRespond(
                        403,
                        new JObject { ["error"] = new JObject { ["message"] = "Forbidden", ["code"] = 40300, ["statusCode"] = 403 } },
                        new Dictionary<string, string> { ["Server"] = "CloudFront" }),
                    times: 1,
                    comment: "RSC15l4: CloudFront 403 on first /time request"),
            });

            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                FallbackHosts = new[] { "localhost" },
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            var result = await client.TimeAsync();

            // Assertions
            // The request should succeed (retried on fallback after CloudFront error)
            // ASSERT result IS number: guaranteed at compile time (TimeAsync returns DateTimeOffset, the SDK's server time)
            result.Should().NotBe(default(DateTimeOffset));

            // Proxy event log shows at least two HTTP requests to /time
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/time");
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(2);

            // First response was the injected 403 with CloudFront header
            var httpResponses = log.HttpResponses();
            httpResponses[0].Status.Should().Be(403);
        }

        // UTS: rest/proxy/RSC15l/unreachable-endpoint-error-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_UnreachableEndpointError()
        {
            // Setup
            // No proxy session needed for this test.

            // Pick a port that is not listening (e.g. 19999).
            var nonListeningPort = 19999;

            // Use token auth via authCallback so the SDK can authenticate without
            // contacting the dead endpoint. The inner Rest client talks directly to the
            // sandbox to obtain a token.
            var client = Rest(new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                Port = nonListeningPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            Func<Task> time = () => client.TimeAsync();
            var error = (await time.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // The error is an ErrorInfo-like object with a statusCode or code
            // (the exact code/statusCode depends on the SDK's HTTP layer, but it must
            // be present and non-null so callers can programmatically handle it)
            error.Should().NotBeNull();

            // NOTE: ErrorInfo.Code is a non-nullable int; "error.code IS NOT null" is read as "a code is set" (non-zero)
            (error.StatusCode.HasValue || error.Code != 0).Should().BeTrue("error.statusCode or error.code is set (statusCode: {0}, code: {1})", error.StatusCode, error.Code);
        }

        // UTS: rest/proxy/RSC15l/connection-drop-fallback-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_ConnectionDropFallback()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpDrop(),
                    times: 1,
                    comment: "Drop TCP connection on first /time request (ECONNRESET)"),
            });

            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                FallbackHosts = new[] { "localhost" },
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            var result = await client.TimeAsync();

            // Assertions
            // The request should succeed (retried on fallback after connection drop)
            // ASSERT result IS number: guaranteed at compile time (TimeAsync returns DateTimeOffset, the SDK's server time)
            result.Should().NotBe(default(DateTimeOffset));

            // Proxy event log shows at least two HTTP requests to /time
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/time");
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(2);
        }

        // UTS: rest/proxy/RSC15l/http-5xx-json-error-parsed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_Http5xxJsonErrorParsed()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpRespond(
                        503,
                        new JObject { ["error"] = new JObject { ["code"] = 50300, ["statusCode"] = 503, ["message"] = "Service temporarily unavailable" } }),
                    times: 1,
                    comment: "Return 503 with JSON error body on first /time request"),
            });

            // No fallbackHosts -- endpoint="localhost" disables fallback (REC2c2)
            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            Func<Task> time = () => client.TimeAsync();
            var error = (await time.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // The SDK parsed the error fields from the JSON response body
            error.Code.Should().Be(50300);
            ((int?)error.StatusCode).Should().Be(503);
            error.Message.Should().Contain("Service temporarily unavailable");
        }

        // UTS: rest/proxy/RSC15l/http-5xx-no-json-synthesized-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_Http5xxNoJsonSynthesized()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpRespond(503, new JObject()),
                    times: 1,
                    comment: "Return 503 with empty JSON body (no error field) on first /time request"),
            });

            // No fallbackHosts -- endpoint="localhost" disables fallback (REC2c2)
            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            Func<Task> time = () => client.TimeAsync();
            var error = (await time.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // The SDK synthesized an error from the HTTP status code
            ((int?)error.StatusCode).Should().Be(503);
        }

        // UTS: rest/proxy/RSC15l/http-4xx-not-retried-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_Http4xxNotRetried()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest(pathContains: "/time"),
                    ProxyRules.HttpRespond(
                        403,
                        new JObject { ["error"] = new JObject { ["code"] = 40300, ["statusCode"] = 403, ["message"] = "Forbidden" } }),
                    times: 1,
                    comment: "Return 403 with JSON error body on first /time request"),
            });

            // Fallback hosts ARE configured -- but 403 should NOT trigger fallback
            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                FallbackHosts = new[] { "localhost" },
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            Func<Task> time = () => client.TimeAsync();
            var error = (await time.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // The SDK parsed the error fields from the JSON response body
            error.Code.Should().Be(40300);
            ((int?)error.StatusCode).Should().Be(403);

            // Proxy event log shows exactly 1 HTTP request to /time (no fallback retry)
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/time");
            httpRequests.Should().HaveCount(1);
        }

        // UTS: rest/proxy/RSL1k4/idempotent-retry-dedup-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1k4")]
        public async Task RSL1k4_IdempotentRetryDedup()
        {
            // Setup
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.HttpRequest("POST", "/channels/"),
                    ProxyRules.HttpReplaceResponse(
                        503,
                        new JObject { ["error"] = new JObject { ["code"] = 50300, ["statusCode"] = 503, ["message"] = "Service temporarily unavailable" } }),
                    times: 1,
                    comment: "RSL1k4: Forward first publish to server, then return fake 503 to client"),
            });

            var client = ProxyRest(session, new ClientOptions
            {
                AuthCallback = SandboxTokenCallback(ApiKey),
                Endpoint = "localhost",
                FallbackHosts = new[] { "localhost" },
                Port = session.ProxyPort,
                Tls = false,
                UseBinaryProtocol = false,
                IdempotentRestPublishing = true,
            });

            var channelName = "test-RSL1k4-idempotent-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish a message -- first attempt succeeds server-side but client sees 503,
            // SDK retries, server deduplicates the retry
            await channel.PublishAsync("test", "data");

            // Assertions
            // The publish completed successfully (SDK retried after the fake 503)
            // No error thrown

            // Verify via history that only one copy of the message exists
            // (server deduplicated the retry based on the library-generated message id)
            var history = await channel.HistoryAsync();
            var matching = history.Items.Where(m => m.Name == "test" && Equals(m.Data, "data")).ToList();
            matching.Should().HaveCount(1);

            // Proxy event log shows at least two POST requests to /channels/
            var log = await session.GetLogAsync();
            var httpRequests = log.HttpRequests("/channels/").Where(e => e.Method == "POST").ToList();
            httpRequests.Count.Should().BeGreaterThanOrEqualTo(2);
        }
    }
}
