using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration.Proxy
{
    /// <summary>
    /// Derived from uts/rest/integration/proxy/rest_fallback.md in ably/specification.
    ///
    /// Spec points: RSC15l, RSC15l2, RSC15l4, RSL1k4
    ///
    /// <para>
    /// These are the fallback decisions a mock cannot settle. Whether a response is retryable is
    /// decided inside the real HTTP client, from a real socket error or a real status and header
    /// set, so the only way to test it is to put a programmable proxy between the SDK and the
    /// sandbox and make the first response go wrong. The second request goes to the "fallback"
    /// host, which is the same proxy under another name, so both attempts land in one event log
    /// and can simply be counted.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>endpoint: "localhost"</c> is REC1b2 and has no counterpart here, so the base
    /// class sets <c>RestHost</c> and <c>RealtimeHost</c> instead - see <c>UtsProxyTestBase</c>.
    /// The important consequence is the same either way: an explicit host empties the default
    /// fallback list (<c>ClientOptions.GetFallbackHosts</c>, ClientOptions.cs:195-205), and an
    /// explicit <c>FallbackHosts</c> puts one back.
    /// </para>
    /// </summary>
    public class RestFallbackTests : UtsProxyTestBase
    {
        /// <summary>
        /// Long enough that the proxy's delay is unambiguously past it, short enough that the test
        /// does not sit out the delay itself. The spec uses the same figure.
        /// </summary>
        private static readonly TimeSpan ShortHttpTimeout = TimeSpan.FromMilliseconds(3000);

        public RestFallbackTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/proxy/RSC15l2/timeout-triggers-fallback-0
        [ProxyFact]
        public async Task RSC15l2_RequestTimeoutTriggersFallback()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    DelayAction(20000),
                    "RSC15l2: delay the first /time past httpRequestTimeout"),
            });

            var client = ProxyRestClient(session, await JwtAuthCallback(), options =>
            {
                options.FallbackHosts = new[] { session.ProxyHost };
                options.HttpRequestTimeout = ShortHttpTimeout;
            });

            var serverTime = await client.TimeAsync();

            serverTime.Year.Should().BeGreaterThan(2000, "the retry succeeded");

            var requests = ProxyLog.HttpRequests(await session.GetLog(), "/time");
            requests.Count.Should().BeGreaterOrEqualTo(
                2,
                "RSC15l2 - the timed-out request was retried on the fallback host");
        }

        // UTS: rest/proxy/RSC15l4/cloudfront-header-fallback-0
        //
        // DEVIATION, D2 - already found at the unit tier by
        // FallbackTests.RSC15l4_CloudFrontErrorTriggersFallback, and confirmed here against a real
        // response from a real proxy: nothing in the SDK reads the Server header, so the 403 is
        // handed to the caller instead of being retried. See Uts/deviations.md.
        [ProxyDeviationFact]
        public async Task RSC15l4_CloudFrontHeaderTriggersFallback()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    RespondAction(403, ErrorBody(40300, 403, "Forbidden"), cloudFront: true),
                    "RSC15l4: CloudFront 403 on the first /time"),
            });

            var client = ProxyRestClient(session, await JwtAuthCallback(), options =>
                options.FallbackHosts = new[] { session.ProxyHost });

            var serverTime = await client.TimeAsync();

            serverTime.Year.Should().BeGreaterThan(2000, "the retry succeeded");

            var log = await session.GetLog();

            ProxyLog.HttpRequests(log, "/time").Count.Should().BeGreaterOrEqualTo(
                2,
                "RSC15l4 - a CloudFront 403 is retryable even though 403 alone is not");

            var responses = ProxyLog.HttpResponses(log);
            responses.Should().NotBeEmpty();
            ((int)responses[0]["status"]).Should().Be(403, "the injected response came first");
        }

        // UTS: rest/proxy/RSC15l/unreachable-endpoint-error-0
        [ProxyFact]
        public async Task RSC15l_UnreachableEndpointSurfacesAUsableError()
        {
            // No proxy session: the point is a port with nothing behind it. The attribute still
            // gates the test on a proxy being available, so it runs with the rest of the tier.
            const int NonListeningPort = 19999;

            var authCallback = await JwtAuthCallback();
            var client = new PubSubHttpClient(new ClientOptions
            {
                RestHost = "localhost",
                RealtimeHost = "localhost",
                Port = NonListeningPort,
                TlsPort = NonListeningPort,
                Tls = false,
                AuthCallback = authCallback,
            });

            var error = await Assert.ThrowsAsync<AblyException>(() => client.TimeAsync());

            error.ErrorInfo.Should().NotBeNull();

            var hasCode = error.ErrorInfo.Code != 0;
            var hasStatus = error.ErrorInfo.StatusCode.HasValue;

            (hasCode || hasStatus).Should().BeTrue(
                "a connection refused has to reach the caller as something it can branch on, "
                + "not as a bare exception");
        }

        // UTS: rest/proxy/RSC15l/connection-drop-fallback-1
        [ProxyFact]
        public async Task RSC15l_ConnectionDropIsRetriedOnTheFallback()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    new JObject { ["type"] = "http_drop" },
                    "drop the TCP connection on the first /time"),
            });

            var client = ProxyRestClient(session, await JwtAuthCallback(), options =>
                options.FallbackHosts = new[] { session.ProxyHost });

            var serverTime = await client.TimeAsync();

            serverTime.Year.Should().BeGreaterThan(2000, "the retry succeeded");

            ProxyLog.HttpRequests(await session.GetLog(), "/time").Count.Should()
                .BeGreaterOrEqualTo(
                    2,
                    "RSC15l - a dropped connection is a retryable error");
        }

        // UTS: rest/proxy/RSC15l/http-5xx-json-error-parsed-0
        [ProxyFact]
        public async Task RSC15l_A5xxWithAJsonErrorBodyIsParsed()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    RespondAction(503, ErrorBody(50300, 503, "Service temporarily unavailable")),
                    "503 with a JSON error body on the first /time"),
            });

            // No fallback hosts, so the error reaches the caller rather than being retried away.
            var client = ProxyRestClient(session, await JwtAuthCallback());

            var error = await Assert.ThrowsAsync<AblyException>(() => client.TimeAsync());

            error.ErrorInfo.Code.Should().Be(50300);
            error.ErrorInfo.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
            error.ErrorInfo.Message.Should().Contain("Service temporarily unavailable");
        }

        // UTS: rest/proxy/RSC15l/http-5xx-no-json-synthesized-1
        [ProxyFact]
        public async Task RSC15l_A5xxWithoutAnErrorBodyStillProducesAUsableError()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    RespondAction(503, new JObject()),
                    "503 with an empty JSON body on the first /time"),
            });

            var client = ProxyRestClient(session, await JwtAuthCallback());

            var error = await Assert.ThrowsAsync<AblyException>(() => client.TimeAsync());

            error.ErrorInfo.Should().NotBeNull();
            error.ErrorInfo.StatusCode.Should().Be(
                System.Net.HttpStatusCode.ServiceUnavailable,
                "the status alone is enough to build an error from");
        }

        // UTS: rest/proxy/RSC15l/http-4xx-not-retried-0
        [ProxyFact]
        public async Task RSC15l_A4xxIsNotRetriedEvenWithFallbacksConfigured()
        {
            var session = await ProxySession(new JArray
            {
                Rule(
                    RespondAction(403, ErrorBody(40300, 403, "Forbidden")),
                    "403 with a JSON error body on the first /time"),
            });

            // Fallbacks are configured precisely so that not using them means something.
            var client = ProxyRestClient(session, await JwtAuthCallback(), options =>
                options.FallbackHosts = new[] { session.ProxyHost });

            var error = await Assert.ThrowsAsync<AblyException>(() => client.TimeAsync());

            error.ErrorInfo.Code.Should().Be(40300);
            error.ErrorInfo.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

            ProxyLog.HttpRequests(await session.GetLog(), "/time").Should().HaveCount(
                1,
                "RSC15l - a 4xx says the request was wrong, so repeating it on another host "
                + "cannot help");
        }

        // UTS: rest/proxy/RSL1k4/idempotent-retry-dedup-0
        [ProxyFact]
        public async Task RSL1k4_AnIdempotentPublishRetryIsDeduplicated()
        {
            var channelName = "test-RSL1k4-idempotent-" + UtsSandbox.RandomId();

            // http_replace_response forwards the publish upstream - so the server really does
            // store it - and then hands the client a 503 anyway. The retry that provokes is the
            // whole point: the server must recognise it by the library-generated message id.
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "http_request",
                        ["method"] = "POST",
                        ["pathContains"] = "/channels/",
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "http_replace_response",
                        ["status"] = 503,
                        ["body"] = ErrorBody(50300, 503, "Service temporarily unavailable"),
                    },
                    ["times"] = 1,
                    ["comment"] = "RSL1k4: forward the first publish, then fake a 503",
                },
            });

            var client = ProxyRestClient(session, await JwtAuthCallback(), options =>
            {
                options.FallbackHosts = new[] { session.ProxyHost };
                options.IdempotentRestPublishing = true;
            });

            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("test", "data");

            var history = await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var page = await channel.HistoryAsync();
                    var matching = page.Items.Where(m => m.Name == "test").ToList();
                    return matching.Count >= 1 ? matching : null;
                },
                "the published message to appear in history");

            history.Should().HaveCount(
                1,
                "RSL1k4 - the server deduplicated the retry on the library-generated id");

            history[0].Data.Should().Be("data");

            var publishes = ProxyLog.HttpRequests(await session.GetLog(), "/channels/");
            publishes.Count.Should().BeGreaterOrEqualTo(
                2,
                "the fake 503 has to have provoked a retry, or the test proves nothing");
        }

        /// <summary>
        /// The specs' rule shape, which is the same every time here: match a <c>/time</c> request,
        /// do something to it once.
        /// </summary>
        private static JObject Rule(JObject action, string comment)
            => new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "http_request",
                    ["pathContains"] = "/time",
                },
                ["action"] = action,
                ["times"] = 1,
                ["comment"] = comment,
            };

        private static JObject DelayAction(int delayMs)
            => new JObject { ["type"] = "http_delay", ["delayMs"] = delayMs };

        private static JObject RespondAction(int status, JObject body, bool cloudFront = false)
        {
            var action = new JObject
            {
                ["type"] = "http_respond",
                ["status"] = status,
                ["body"] = body,
            };

            if (cloudFront)
            {
                action["headers"] = new JObject { ["Server"] = "CloudFront" };
            }

            return action;
        }

        private static JObject ErrorBody(int code, int statusCode, string message)
            => new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["statusCode"] = statusCode,
                    ["message"] = message,
                },
            };
    }
}
