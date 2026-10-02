using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/request.md in ably/specification.
    ///
    /// Spec points: RSC19, RSC19b, RSC19c, RSC19d, RSC19e, RSC19f, RSC19f1, HP1, HP3, HP4, HP5,
    /// HP6, HP7, HP8
    ///
    /// Two file-wide translation decisions:
    ///
    /// The spec passes <c>version: 3</c> to every call. .NET's
    /// <c>PubSubHttpClient.Request(HttpMethod, path, requestParams, body, headers)</c> — and its
    /// string-bodied sibling <c>RequestV2</c> — has no version parameter; X-Ably-Version is set
    /// once per client from <c>Defaults.ProtocolVersion</c>. The argument is therefore dropped
    /// everywhere, and RSC19f1 (version-param-sets-header-0), whose whole subject is that
    /// parameter, is not derived.
    ///
    /// msgpack is compiled out of this build — <c>ClientOptions.UseBinaryProtocol</c>'s getter is a
    /// hard-coded false and <c>Defaults.Protocol</c> is JSON — so every "Protocol Variants" pair in
    /// this spec has exactly one runnable variant here. Only the JSON half is derived; the binary
    /// half is a build-configuration limitation rather than a deviation.
    /// </summary>
    public class RequestTests : UtsTestBase
    {
        public RequestTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC19f/supports-http-methods-0
        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task RSC19f_SupportsHttpMethods(string method)
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);

            // The method is spelled through the string constructor rather than the HttpMethod
            // statics so the table reads as the spec writes it.
            await client.Request(new HttpMethod(method), "/test");

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be(method);
            capturedRequests[0].Url.Path.Should().Be("/test");
        }

        // UTS: rest/unit/RSC19f/query-params-passed-1
        [Fact]
        public async Task RSC19f_QueryParamsPassed()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Get,
                "/channels/test/messages",
                requestParams: new Dictionary<string, string>
                {
                    { "limit", "10" },
                    { "direction", "backwards" },
                });

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Url.QueryParams["limit"].Should().Be("10");
            request.Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSC19f/custom-headers-passed-2
        [Fact]
        public async Task RSC19f_CustomHeadersPassed()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string>
                {
                    { "X-Custom-Header", "custom-value" },
                    { "X-Another", "another-value" },
                });

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Headers["X-Custom-Header"].Should().Be("custom-value");
            request.Headers["X-Another"].Should().Be("another-value");
        }

        // UTS: rest/unit/RSC19f/request-body-sent-3
        [Fact]
        public async Task RSC19f_RequestBodySent()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { id = "123" });
                });

            // The spec sets useBinaryProtocol: false for easier inspection. JSON is the only
            // protocol this build has, so there is nothing to configure.
            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Post,
                "/channels/test/messages",
                body: new JObject
                {
                    { "name", "event" },
                    { "data", "payload" },
                });

            capturedRequests.Should().HaveCount(1);
            var body = JObject.Parse(capturedRequests[0].BodyText);

            body.Value<string>("name").Should().Be("event");
            body.Value<string>("data").Should().Be("payload");
        }

        // UTS: rest/unit/RSC19f/path-leading-slash-handling-4
        [Fact]
        public async Task RSC19f_PathLeadingSlashHandlingWithSlash()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, Array.Empty<object>());

            var client = RestClient(mockHttp);

            await client.Request(HttpMethod.Get, "/channels/test");

            mockHttp.CapturedRequests.Should().HaveCount(1);
            mockHttp.CapturedRequests[0].Url.Path.Should().Be("/channels/test");
        }

        // UTS: rest/unit/RSC19f/path-leading-slash-handling-4
        //
        // UTS SPEC ERROR, not an SDK deviation — recorded under "UTS Spec Errors" in
        // Uts/deviations.md.
        //
        // The features spec's RSC19f says only that `path` is "the path component of the URL such as
        // '/channels'" — the example carries the leading slash and nothing requires tolerating its
        // absence. The leniency requirement exists only in the UTS spec, which adds a second case
        // passing "channels/test". So the spec asserts a requirement its own authority does not
        // state.
        //
        // What the SDK does with it is nonetheless poor: AblyHttpRequester.GetRequestUrl
        // (AblyHttpRequester.cs:472) interpolates scheme, host, port and path with no separator, so
        // the URI becomes "https://rest.ably.io:443channels/test", whose authority has no parseable
        // port, and the call dies with an AblyException wrapping a UriFormatException that says
        // nothing about the real cause. That is worth a low-priority robustness fix — either prepend
        // the slash or raise a clear error — but it is a DX defect, not non-compliance.
        //
        // Kept env-gated rather than made a fail-fast spec-error test, which is what
        // writing-derived-tests.md prescribes for a 2a. The classification rests on the features
        // spec being *silent*, not on a contradiction, so hard-failing the suite on it would claim
        // more confidence than the evidence supports. The deviations entry carries the question to
        // put upstream.
        [DeviationFact]
        public async Task RSC19f_PathLeadingSlashHandlingWithoutSlash()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, Array.Empty<object>());

            var client = RestClient(mockHttp);

            await client.Request(HttpMethod.Get, "channels/test");

            mockHttp.CapturedRequests.Should().HaveCount(1);
            mockHttp.CapturedRequests[0].Url.Path.Should().Be("/channels/test");
        }

        // UTS: rest/unit/RSC19b/uses-configured-auth-0
        [Fact]
        public async Task RSC19b_UsesConfiguredAuthBasic()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);

            await client.Request(HttpMethod.Get, "/test");

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Headers.Should().ContainKey("Authorization");
            var authorization = request.Headers["Authorization"];
            authorization.Should().StartWith("Basic ");

            // Fully qualified: the sibling Uts/Rest/Unit/Encoding directory puts an
            // Ably.PubSub.Tests.Uts.Rest.Unit.Encoding namespace in scope, which shadows System.Text.
            var credentials = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(authorization.Substring("Basic ".Length)));
            credentials.Should().Be(UtsClients.ValidKey);
        }

        // UTS: rest/unit/RSC19b/uses-configured-auth-0
        [Fact]
        public async Task RSC19b_UsesConfiguredAuthToken()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            // The spec constructs ClientOptions(token: "my-token-string") with no key, so the
            // harness default key has to be cleared for the client to choose token auth.
            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.Token = "my-token-string";
            });

            await client.Request(HttpMethod.Get, "/test");

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Headers.Should().ContainKey("Authorization");
            request.Headers["Authorization"].Should().StartWith("Bearer ");
        }

        // UTS: rest/unit/RSC19b/cannot-override-auth-1
        [Fact]
        public async Task RSC19b_CannotOverrideAuth()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, Array.Empty<object>());

            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string>
                {
                    { "Authorization", "Bearer malicious-token" },
                });

            mockHttp.CapturedRequests.Should().HaveCount(1);
            var authorization = mockHttp.CapturedRequests[0].Headers["Authorization"];

            authorization.Should().StartWith("Basic ");
            authorization.Should().NotBe("Bearer malicious-token");
        }

        // UTS: rest/unit/RSC19c/protocol-headers-json-0
        [Fact]
        public async Task RSC19c_ProtocolHeadersJson()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, Array.Empty<object>());

            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Post,
                "/test",
                body: new JObject { { "data", "test" } });

            mockHttp.CapturedRequests.Should().HaveCount(1);
            var request = mockHttp.CapturedRequests[0];

            request.Headers["Accept"].Should().Be("application/json");
            request.Headers["Content-Type"].Should().Be("application/json");
        }

        // UTS: rest/unit/RSC19c/body-encoded-per-protocol-2
        [Fact]
        public async Task RSC19c_BodyEncodedPerProtocolJson()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, Array.Empty<object>());

            var client = RestClient(mockHttp);

            await client.Request(
                HttpMethod.Post,
                "/test",
                body: new JObject
                {
                    { "name", "event" },
                    { "data", new JObject { { "nested", "value" } } },
                });

            mockHttp.CapturedRequests.Should().HaveCount(1);
            var body = JObject.Parse(mockHttp.CapturedRequests[0].BodyText);

            body.Value<string>("name").Should().Be("event");
            body["data"].Value<string>("nested").Should().Be("value");
        }

        // UTS: rest/unit/RSC19c/response-decoded-by-content-type-3
        [Fact]
        public async Task RSC19c_ResponseDecodedByContentTypeJson()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                200,
                new object[]
                {
                    new { id = "1", name = "item1" },
                    new { id = "2", name = "item2" },
                },
                new Dictionary<string, string> { { "Content-Type", "application/json" } });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            // The spec reads response.items(); the .NET rendering is the Items collection.
            response.Items.Should().HaveCount(2);
            response.Items[0].Value<string>("id").Should().Be("1");
            response.Items[1].Value<string>("name").Should().Be("item2");
        }

        // UTS: rest/unit/RSC19d/response-status-code-0
        [Theory]
        [InlineData(200)]
        [InlineData(201)]
        [InlineData(400)]
        [InlineData(404)]
        [InlineData(500)]
        public async Task RSC19d_ResponseStatusCode(int statusCode)
        {
            var mockHttp = new MockHttpClient();
            QueueStatus(mockHttp, statusCode);

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            ((int)response.StatusCode).Should().Be(statusCode);
        }

        // UTS: rest/unit/RSC19d/response-success-indicator-1
        [Theory]
        [InlineData(200, true)]
        [InlineData(201, true)]
        [InlineData(204, true)]
        [InlineData(299, true)]
        [InlineData(300, false)]
        [InlineData(400, false)]
        [InlineData(500, false)]
        public async Task RSC19d_ResponseSuccessIndicator(int statusCode, bool expectedSuccess)
        {
            var mockHttp = new MockHttpClient();
            QueueStatus(mockHttp, statusCode);

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            response.Success.Should().Be(expectedSuccess);
        }

        // UTS: rest/unit/RSC19d/response-error-code-header-2
        [Fact]
        public async Task RSC19d_ResponseErrorCodeHeader()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                401,
                new { error = new { code = 40101, message = "Unauthorized" } },
                new Dictionary<string, string> { { "X-Ably-Errorcode", "40101" } });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            response.ErrorCode.Should().Be(40101);
        }

        // UTS: rest/unit/RSC19d/response-error-message-header-3
        //
        // SPEC: errorMessage is taken from the X-Ably-Errormessage header, so this is
        // "Token expired".
        // SDK: the HttpPaginatedResponse constructor reads ErrorMessage out of the error *code*
        // header — its second TryGetValues call passes AblyErrorCodeHeader, not an error-message
        // header name — so ErrorMessage comes back as "40101" and X-Ably-Errormessage is never
        // read at all. Env-gated because this reads as a copy/paste defect rather than an
        // intentional divergence.
        [DeviationFact]
        public async Task RSC19d_ResponseErrorMessageHeader()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                401,
                new { error = new { code = 40101, message = "Unauthorized" } },
                new Dictionary<string, string>
                {
                    { "X-Ably-Errorcode", "40101" },
                    { "X-Ably-Errormessage", "Token expired" },
                });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            response.ErrorMessage.Should().Be("Token expired");
        }

        // UTS: rest/unit/RSC19d/response-headers-accessible-4
        [Fact]
        public async Task RSC19d_ResponseHeadersAccessible()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                200,
                Array.Empty<object>(),
                new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "X-Request-Id", "req-123" },
                    { "X-Custom-Header", "custom-value" },
                });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/test");

            HeaderValue(response, "X-Request-Id").Should().Be("req-123");
            HeaderValue(response, "X-Custom-Header").Should().Be("custom-value");

            // ADAPTED: the spec reads headers["Content-Type"] from the same map as the other two.
            // HttpPaginatedResponse.Headers is a System.Net.Http.Headers.HttpHeaders carrying the
            // *response* headers, and .NET files Content-Type on the content headers instead, so
            // it is never in that map. The SDK does surface it, on AblyResponse.ContentType, which
            // is what ContentTypeOf reads — so the third assertion is kept rather than dropped.
            ContentTypeOf(response).Should().Be("application/json");
        }

        // UTS: rest/unit/RSC19d/response-items-decoded-5
        [Fact]
        public async Task RSC19d_ResponseItemsDecoded()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                200,
                new object[]
                {
                    new { id = "msg1", name = "event1", data = "data1" },
                    new { id = "msg2", name = "event2", data = "data2" },
                });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/channels/test/messages");

            response.Items.Should().HaveCount(2);
            response.Items[0].Value<string>("id").Should().Be("msg1");
            response.Items[1].Value<string>("id").Should().Be("msg2");
        }

        // UTS: rest/unit/RSC19d/pagination-with-link-headers-6
        [Fact]
        public async Task RSC19d_PaginationWithLinkHeaders()
        {
            var mockHttp = new MockHttpClient();

            // First page.
            mockHttp.QueueResponse(
                200,
                new object[] { new { id = "1" }, new { id = "2" } },
                new Dictionary<string, string>
                {
                    { "Link", "</channels/test/messages?page=2>; rel=\"next\"" },
                });

            // Second page: no "next" link, so it is the last.
            mockHttp.QueueResponse(200, new object[] { new { id = "3" } });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/channels/test/messages");

            response.Items.Should().HaveCount(2);
            response.HasNext.Should().BeTrue();

            response = await response.NextAsync();

            response.Items.Should().HaveCount(1);
            response.Items[0].Value<string>("id").Should().Be("3");
            response.HasNext.Should().BeFalse();

            mockHttp.CapturedRequests.Should().HaveCount(2);
        }

        // UTS: rest/unit/RSC19d/non-array-response-handling-7
        [Fact]
        public async Task RSC19d_NonArrayResponseHandling()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new { time = 1234567890000L });

            var client = RestClient(mockHttp);

            var response = await client.Request(HttpMethod.Get, "/time");

            // NOTE: the spec allows either shape ("items.length == 1 OR items[time] == ..."), since
            // an implementation may wrap or return the object directly. This SDK takes the wrapping
            // branch, so both halves can be asserted exactly.
            response.Items.Should().HaveCount(1);
            response.Items[0].Value<long>("time").Should().Be(1234567890000L);
        }

        // UTS: rest/unit/RSC19d/empty-response-handling-8
        [Fact]
        public async Task RSC19d_EmptyResponseHandling()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(204);

            var client = RestClient(mockHttp);

            var response = await client.Request(
                new HttpMethod("DELETE"),
                "/channels/test/messages/123");

            ((int)response.StatusCode).Should().Be(204);
            response.Success.Should().BeTrue();
            response.Items.Should().BeEmpty();
        }

        // UTS: rest/unit/RSC19e/network-error-propagated-0
        [Fact]
        public async Task RSC19e_NetworkErrorPropagated()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithRefused());

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = Array.Empty<string>();
            });

            Func<Task> act = () => client.Request(HttpMethod.Get, "/test");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // NOTE: the spec assertion is a disjunction, and this SDK satisfies the message half:
            // a refused connection surfaces as code 50000 (ErrorCodes.InternalError) with the
            // message "Error executing request. Connection refused by rest.ably.io".
            var message = error.Message ?? string.Empty;
            var matched = error.Code == 80000
                          || message.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0
                          || message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0;

            matched.Should().BeTrue(
                "the network failure should be recognisable from the error, but got code "
                + error.Code + " and message " + message);
        }

        // UTS: rest/unit/RSC19e/timeout-error-handling-1
        [Fact]
        public async Task RSC19e_TimeoutErrorHandling()
        {
            var mockHttp = new MockHttpClient();

            // The spec pairs a 5000ms delay with a 1000ms httpRequestTimeout. The REST deadline is
            // enforced by HttpClient.Timeout, which no clock seam reaches, so the interval is
            // shortened rather than advanced — see "Timers" in the uts-to-csharp skill. The 5x
            // ratio between delay and deadline is preserved.
            mockHttp.QueueDelayedResponse(
                TimeSpan.FromMilliseconds(1000),
                200,
                Array.Empty<object>());

            var client = RestClient(mockHttp, configure: options =>
            {
                options.HttpRequestTimeout = TimeSpan.FromMilliseconds(200);
                options.FallbackHosts = Array.Empty<string>();
            });

            Func<Task> act = () => client.Request(HttpMethod.Get, "/test");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // NOTE: as above, the spec assertion is a disjunction. This SDK reports code 50000 and
            // surfaces the cancellation text HttpClient itself produces, which names the elapsed
            // Timeout.
            var message = error.Message ?? string.Empty;
            var matched = error.Code == 50003
                          || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

            matched.Should().BeTrue(
                "the request timeout should be recognisable from the error, but got code "
                + error.Code + " and message " + message);
        }

        // UTS: rest/unit/RSC19e/http-error-no-fallback-2
        [Fact]
        public async Task RSC19e_HttpErrorNoFallback()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                400,
                new { error = new { code = 40000, message = "Bad request" } },
                new Dictionary<string, string> { { "X-Ably-Errorcode", "40000" } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = new[] { "a.ably-realtime.com", "b.ably-realtime.com" };
            });

            var response = await client.Request(HttpMethod.Get, "/test");

            ((int)response.StatusCode).Should().Be(400);
            response.Success.Should().BeFalse();
            response.ErrorCode.Should().Be(40000);

            mockHttp.CapturedRequests.Should().HaveCount(
                1,
                "a 4xx carrying a valid Ably error is returned rather than retried on a fallback");
        }

        // UTS: rest/unit/RSC19e/fallback-on-server-error-3
        [Fact]
        public async Task RSC19e_FallbackOnServerError()
        {
            var mockHttp = new MockHttpClient();

            // Primary host fails with a non-Ably 500.
            mockHttp.QueueResponse(
                500,
                "Internal Server Error",
                new Dictionary<string, string> { { "Content-Type", "text/plain" } });

            // Fallback succeeds.
            mockHttp.QueueResponse(200, new object[] { new { id = "1" } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = new[] { "fallback.ably-realtime.com" };
            });

            var response = await client.Request(HttpMethod.Get, "/test");

            ((int)response.StatusCode).Should().Be(200);
            response.Success.Should().BeTrue();

            mockHttp.CapturedRequests.Should().HaveCount(2);
            mockHttp.CapturedRequests[1].Url.Host.Should().Be("fallback.ably-realtime.com");
        }

        /// <summary>
        /// The per-status setup the two table-driven RSC19d specs share: an error status gets an
        /// Ably error body, anything else an empty collection.
        /// </summary>
        private static void QueueStatus(MockHttpClient mockHttp, int statusCode)
        {
            if (statusCode >= 400)
            {
                mockHttp.QueueResponse(
                    statusCode,
                    new { error = new { code = statusCode * 100, message = "Error" } });
            }
            else
            {
                mockHttp.QueueResponse(statusCode, Array.Empty<object>());
            }
        }

        /// <summary>
        /// The specs' <c>response.headers["name"]</c>. HttpHeaders has no indexer, and a header can
        /// carry several values, so the first is taken.
        /// </summary>
        private static string HeaderValue(HttpPaginatedResponse response, string name)
            => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        /// <summary>
        /// The adaptation for <c>response.headers["Content-Type"]</c>, kept in one place: .NET
        /// files Content-Type on the content headers, which HttpPaginatedResponse.Headers does not
        /// include, and the SDK surfaces it on the (internal) AblyResponse instead.
        /// </summary>
        private static string ContentTypeOf(HttpPaginatedResponse response)
            => response.Response.ContentType;
    }
}
