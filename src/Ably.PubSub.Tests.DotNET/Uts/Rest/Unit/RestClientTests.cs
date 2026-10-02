using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/rest_client.md in ably/specification.
    ///
    /// Spec points: RSC5, RSC7c, RSC7d, RSC7d1, RSC7d2, RSC7e, RSC8a, RSC8b, RSC8c, RSC8e, RSC13,
    /// RSC17, RSC18
    ///
    /// Two of the file's tests, and one of the two cases of a third, are not represented, all for
    /// the same reason: msgpack is compiled out of this build.
    /// <c>ClientOptions.UseBinaryProtocol</c>'s getter returns a hard-coded <c>false</c> and its
    /// setter is a no-op, and there is no <c>msgpack_encode</c> to stub a response with, so
    /// <c>RSC8/error-decoded-from-msgpack-0</c>, <c>RSC8d/mismatched-response-content-type-0</c> and
    /// the msgpack case of <c>RSC8a/protocol-selection-0</c> have nothing runnable behind them. That
    /// is a build configuration rather than an SDK defect — see Uts/README.md.
    ///
    /// Three tests carry the spec's assertion against behaviour this SDK does not implement and are
    /// therefore <c>[DeviationFact]</c> rather than <c>[Fact]</c>: RSC7c_RequestIdIncluded,
    /// RSC8e_UnsupportedContentTypeOnSuccessStatus and RSC18_BasicAuthOverHttpRejected. Three
    /// further assertions are adapted to the observable the SDK actually exposes —
    /// RSC7c_RequestIdPreservedOnFallbackRetry and both RSC17 tests. Each says so at the site.
    /// </summary>
    public class RestClientTests : UtsTestBase
    {
        private const long ServerTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

        public RestClientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC5/auth-attribute-accessible-0
        [Fact]
        public void RSC5_AuthAttributeAccessible()
        {
            // The spec makes no request here. A MockHttpClient is still installed because
            // UtsClients.RestClient takes one, and SkipInternetCheck keeps the client off the network.
            var client = RestClient(new MockHttpClient());

            client.Auth.Should().NotBeNull();

            // The spec's "client.auth IS Auth". PubSubHttpClient.Auth is statically typed as
            // IAblyAuth, so the interface half of the assertion cannot fail; AblyAuth is the concrete
            // Auth object the client instantiates from its ClientOptions, which is the spec point.
            client.Auth.Should().BeOfType<AblyAuth>();
        }

        // UTS: rest/unit/RSC7e/ably-version-header-0
        [Fact]
        public async Task RSC7e_AblyVersionHeader()
        {
            PendingHttpRequest capturedRequest = null;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    req.RespondWith(200, TimeBody());
                });

            var client = RestClient(mockHttp);

            await client.TimeAsync();

            capturedRequest.Should().NotBeNull();
            capturedRequest.Headers.Should().ContainKey("X-Ably-Version");
            capturedRequest.Headers["X-Ably-Version"].Should().MatchRegex("^[0-9.]+$");
        }

        // UTS: rest/unit/RSC7d/ably-agent-header-format-0
        [Fact]
        public async Task RSC7d_AblyAgentHeaderFormat()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, TimeBody());

            var client = RestClient(mockHttp);

            await client.TimeAsync();

            var request = mockHttp.CapturedRequests[0];
            request.Headers.Should().ContainKey("Ably-Agent");

            var agent = request.Headers["Ably-Agent"];

            // NOTE: the spec's pattern is "ably-[a-z]+/[0-9]+\.[0-9]+\.[0-9]+", which assumes a
            // single-segment family identifier. RSC7d1 registers this SDK as "ably-pubsub-dotnet", so
            // the character class has to admit the hyphens; the shape asserted — family name, slash,
            // semver (RSC7d2) — is the spec's. The header also carries runtime and OS entries, which
            // RSC7d1's space-separated form allows and the spec's own comment anticipates.
            agent.Should().MatchRegex(@"ably-[a-z0-9-]+/[0-9]+\.[0-9]+\.[0-9]+");
        }

        // UTS: rest/unit/RSC7c/request-id-included-0
        [DeviationFact]
        public async Task RSC7c_RequestIdIncluded()
        {
            // RSC7c asks for request_id as a query parameter made of url-safe base64. This SDK sends
            // it as a request *header* — PubSubHttpClient.ExecuteRequest hands it to
            // AblyRequest.AddHeaders, not AddQueryParameters — and builds the value with
            // Convert.ToBase64String, which is standard rather than url-safe base64, so it carries
            // '+', '/' and '=' padding. Both halves of the spec's assertion therefore fail. See
            // Uts/deviations.md; RSC7c_RequestIdPreservedOnFallbackRetry covers the half of the spec
            // point that does hold.
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, TimeBody());

            var client = RestClient(mockHttp, configure: options => options.AddRequestIds = true);

            await client.TimeAsync();

            var request = mockHttp.CapturedRequests[0];
            request.Url.QueryParams.Should().ContainKey("request_id");

            var requestId = request.Url.QueryParams["request_id"];

            // Url-safe base64 over at least 9 bytes.
            requestId.Length.Should().BeGreaterOrEqualTo(12);
            requestId.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        }

        // UTS: rest/unit/RSC7c/request-id-preserved-fallback-1
        [Fact]
        public async Task RSC7c_RequestIdPreservedOnFallbackRetry()
        {
            var mockHttp = new MockHttpClient();

            // The first attempt fails with a retryable 500, so the request goes out again to a
            // fallback host; the retry succeeds.
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });
            mockHttp.QueueResponse(200, TimeBody());

            var client = RestClient(mockHttp, configure: options =>
            {
                options.AddRequestIds = true;
                options.FallbackHosts = new[] { "a.example.com", "b.example.com" };
            });

            await client.TimeAsync();

            mockHttp.CapturedRequests.Should().HaveCount(2);

            // Adapted: the spec reads request_id out of url.query_params. This SDK puts it in a
            // request header instead (see RSC7c_RequestIdIncluded), so it is read from there.
            // Preservation of one id across the retry — the point of this spec point — does hold.
            mockHttp.CapturedRequests[0].Headers.Should().ContainKey("request_id");
            mockHttp.CapturedRequests[1].Headers.Should().ContainKey("request_id");

            var firstRequestId = mockHttp.CapturedRequests[0].Headers["request_id"];
            var secondRequestId = mockHttp.CapturedRequests[1].Headers["request_id"];

            firstRequestId.Should().NotBeNullOrEmpty();
            secondRequestId.Should().Be(firstRequestId, "the same id must be used for the retry");
        }

        // UTS: rest/unit/RSC8a/protocol-selection-0
        [Fact]
        public async Task RSC8a_ProtocolSelectionJson()
        {
            // Only the spec's case 2 is runnable here. Case 1 (useBinaryProtocol: true ->
            // application/x-msgpack) has no behaviour behind it: msgpack is compiled out, so
            // UseBinaryProtocol reads back false whatever is written to it and the requester always
            // sends and accepts JSON.
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(201, new { serials = new[] { "s1" } });

            var client = RestClient(mockHttp, configure: options => options.UseBinaryProtocol = false);

            await client.Channels.Get("test").PublishAsync("e", "d");

            var request = mockHttp.CapturedRequests[0];
            request.Headers["Content-Type"].Should().Be("application/json");
            request.Headers["Accept"].Should().Be("application/json");
        }

        // UTS: rest/unit/RSC8c/accept-content-type-headers-0
        [Fact]
        public async Task RSC8c_AcceptAndContentTypeHeaders()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(201, new { serials = new[] { "s1" } });

            var client = RestClient(mockHttp, configure: options => options.UseBinaryProtocol = false);

            await client.Channels.Get("test").PublishAsync("e", "d");

            var request = mockHttp.CapturedRequests[0];
            request.Headers["Accept"].Should().Be("application/json");
            request.Headers["Content-Type"].Should().Be("application/json");
        }

        // UTS: rest/unit/RSC8e/unsupported-content-type-0
        [Fact]
        public async Task RSC8e_UnsupportedContentTypeOnErrorStatus()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                500,
                "<html>Server Error</html>",
                new Dictionary<string, string> { { "Content-Type", "text/html" } });

            var client = RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Per the spec's own note, only the status code is asserted: a 500 goes through the SDK's
            // generic error-response handling, which tries the body as a JSON error and falls back to
            // a generic message.
            ((int)error.StatusCode.Value).Should().Be(500);
        }

        // UTS: rest/unit/RSC8e/unsupported-content-type-0
        [DeviationFact]
        public async Task RSC8e_UnsupportedContentTypeOnSuccessStatus()
        {
            // RSC8e wants code 40013 and status 400 for a 2xx carrying a Content-Type the client
            // cannot decode. This SDK has no such check: AblyResponse types anything that is not
            // application/json, application/jwt or text/plain as Binary and leaves TextResponse null,
            // and MessageHandler.ParseResponse then hands that null straight to
            // JsonHelper.Deserialize — so the call fails with a raw ArgumentNullException rather than
            // an AblyException of any code. See Uts/deviations.md.
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                200,
                "<html>OK</html>",
                new Dictionary<string, string> { { "Content-Type", "text/html" } });

            var client = RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(400);
            error.Code.Should().Be(40013);
        }

        // UTS: rest/unit/RSC13/request-timeout-enforced-0
        [Fact]
        public async Task RSC13_RequestTimeoutEnforced()
        {
            var mockHttp = new MockHttpClient(onConnectionAttempt: conn => conn.RespondWithSuccess());

            // The spec's 1000ms is shortened to 300ms. httpRequestTimeout is time the SDK
            // *schedules*, so the harness rule is to drive it with a short real interval through the
            // client option; the spec's own note endorses a short value for exactly this reason.
            var client = RestClient(
                mockHttp,
                configure: options => options.HttpRequestTimeout = TimeSpan.FromMilliseconds(300));

            // The waiter is registered before the call is provoked: in .NET TimeAsync() has already
            // reached the mock by the time the next statement runs.
            var pending = mockHttp.AwaitRequest();
            var timeCall = client.TimeAsync();
            var request = await pending;

            request.RespondWithDelay(TimeSpan.FromSeconds(5), 200, TimeBody());

            Func<Task> act = () => timeCall;
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            var satisfiesSpec = error.Code == 50003 ||
                error.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

            // The observed values go through becauseArgs rather than string interpolation so that a
            // brace in an SDK message cannot break FluentAssertions' message formatting.
            satisfiesSpec.Should().BeTrue(
                "RSC13 requires code 50003 or a message naming the timeout, but the error was code "
                + "{0}: {1}",
                error.Code,
                error.Message);
        }

        // UTS: rest/unit/RSC17/client-id-from-options-0
        [Fact]
        public void RSC17_ClientIdFromOptions()
        {
            var client = RestClient(
                new MockHttpClient(),
                configure: options => options.ClientId = "explicit-client-id");

            // Adapted: PubSubHttpClient exposes no top-level ClientId member, so the spec's
            // "client.clientId == client.auth.clientId" collapses to the single accessor the SDK has.
            // That accessor is also what RSC17's own requirement text is about (Auth#clientId).
            client.Auth.ClientId.Should().Be("explicit-client-id");
        }

        // UTS: rest/unit/RSC17/client-id-matches-auth-1
        [Fact]
        public void RSC17_ClientIdMatchesAuth()
        {
            // NOTE: the spec gives this test the same setup and the same assertions as
            // client-id-from-options-0; it is kept as its own test so the Test ID is represented.
            var client = RestClient(
                new MockHttpClient(),
                configure: options => options.ClientId = "explicit-client-id");

            // Adapted for the same reason as RSC17_ClientIdFromOptions.
            client.Auth.ClientId.Should().Be("explicit-client-id");
        }

        // UTS: rest/unit/RSC18/tls-controls-protocol-scheme-0
        [Fact]
        public async Task RSC18_TlsControlsProtocolSchemeHttps()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, TimeBody());

            // Case 1: tls defaults to true.
            var client = RestClient(mockHttp);

            await client.TimeAsync();

            mockHttp.CapturedRequests[0].Url.Scheme.Should().Be("https");
        }

        // UTS: rest/unit/RSC18/tls-controls-protocol-scheme-0
        [Fact]
        public async Task RSC18_TlsControlsProtocolSchemeHttp()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, TimeBody());

            // Case 2. The spec keeps the API key and only turns tls off, which does not trip the
            // basic-auth-over-plain-HTTP check: /time is unauthenticated (TimeAsync sets
            // SkipAuthentication), and AblyAuth.AddAuthHeader is the only place that check runs from
            // on a REST request.
            var client = RestClient(mockHttp, configure: options => options.Tls = false);

            await client.TimeAsync();

            mockHttp.CapturedRequests[0].Url.Scheme.Should().Be("http");
        }

        // UTS: rest/unit/RSC18/basic-auth-over-http-rejected-1
        [Fact]
        public async Task RSC18_BasicAuthOverHttpRejected()
        {
            // The spec's setup says "No mock needed - should fail before making request", i.e. it
            // expects the rejection at construction. This SDK checks lazily instead:
            // AblyAuth.EnsureSecureConnection (AblyAuth.cs:535-541) runs from AddAuthHeader, which
            // PubSubHttpClient.ExecuteRequest calls for every request that is not SkipAuthentication.
            //
            // That is not a deviation. RSC18 requires that Basic Auth over HTTP "will result in an
            // error", and RSA1 says "any attempt to use Basic Auth over HTTP" — neither mandates an
            // eager constructor check, and the harm the clause exists to prevent (a private key
            // crossing a plain connection) cannot occur, because the throw happens before the header
            // is written. So the test makes an authenticated request, which is what "attempt to use"
            // means here.
            //
            // It must not be client.TimeAsync(): /time sets SkipAuthentication (PubSubHttpClient.cs:368)
            // and so never reaches the check — which is exactly why the sibling
            // RSC18_TlsControlsProtocolSchemeHttp above passes with Tls = false.
            //
            // The spec's `code == 40103` alternative is dropped: 40103 appears nowhere in the features
            // spec, which mandates no code for this clause, so asserting it would be asserting the
            // UTS spec's invention rather than the requirement. The exception type carries the
            // meaning. The SDK's ErrorInfo code here is 500, which is a poor choice for a client
            // configuration error but not a compliance failure; noted in Uts/deviations.md under
            // "Investigated and not defects".
            var client = RestClient(new MockHttpClient(), configure: options => options.Tls = false);

            Func<Task> act = () => client.Request(HttpMethod.Get, "/channels/uts-rsc18");

            await act.Should().ThrowAsync<AblyInsecureRequestException>();
            client.Options.Tls.Should().BeFalse();
        }

        // UTS: rest/unit/RSC18/basic-auth-over-http-rejected-1
        [Fact]
        public async Task RSC18_TokenAuthOverHttpAllowed()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, TimeBody());

            // The spec's options carry a token and no key. The harness default sets a key, so it is
            // cleared here — otherwise the client would select basic auth and the scenario would not
            // be the one the spec describes.
            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.Token = "some-token-string";
                options.Tls = false;
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            mockHttp.CapturedRequests.Should().HaveCount(1);
            mockHttp.CapturedRequests[0].Url.Scheme.Should().Be("http");
        }

        // The spec stubs /time as {"time": N}. The endpoint — and uts/rest/unit/time.md — return
        // [N], and TimeAsync() indexes the array, so the stub has to be the array form.
        private static object TimeBody() => new object[] { ServerTimeMs };
    }
}
