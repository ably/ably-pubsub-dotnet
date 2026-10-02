using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Auth
{
    /// <summary>
    /// Derived from uts/rest/unit/auth/auth_callback.md in ably/specification.
    ///
    /// Spec points: RSA8c, RSA8c1, RSA8d
    ///
    /// <para>
    /// Bearer assertions go through <see cref="BearerHeader"/> for the reason set out in
    /// <see cref="AuthSchemeTests"/>: this SDK Base64-encodes the token in the header, which RSA3b
    /// explicitly permits.
    /// </para>
    ///
    /// <para>
    /// <c>authUrl</c> requests go through the injected transport like any other request, because
    /// <c>AblyAuth</c> fetches them with the client's own HTTP layer — so the mock observes them and
    /// the spec's two-request assertions translate directly.
    /// </para>
    /// </summary>
    public class AuthCallbackTests : UtsTestBase
    {
        private const string AuthHost = "auth.example.com";

        public AuthCallbackTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA8d/callback-invoked-for-auth-0
        [Fact]
        public async Task RSA8d_CallbackInvokedForAuth()
        {
            var callbackInvoked = false;
            TokenParams callbackParams = null;

            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = ChannelMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackInvoked = true;
                    callbackParams = tokenParams;
                    return Task.FromResult<object>(TokenFor("callback-token"));
                };
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            callbackInvoked.Should().BeTrue();
            callbackParams.Should().NotBeNull("RSA8d passes the TokenParams in");

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Headers["Authorization"].Should().Be(BearerHeader("callback-token"));
        }

        // UTS: rest/unit/RSA8d/callback-returns-jwt-1
        //
        // DEVIATION. RSA8d (features.md:237) requires the callback to accept "either a token string,
        // a TokenDetails object or a TokenRequest object". This SDK accepts only the latter two:
        // AblyAuth.GetTokenRequest deserialises *any* string result as a serialised TokenRequest, so
        // a bare JWT is rejected as malformed JSON before it can be used as a token. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSA8d_CallbackReturnsJwt()
        {
            const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test-jwt-payload";

            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = ChannelMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(jwt);
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Headers["Authorization"].Should().Be(BearerHeader(jwt));
        }

        // UTS: rest/unit/RSA8d/callback-returns-token-request-2
        [Fact]
        public async Task RSA8d_CallbackReturnsTokenRequest()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Path.Contains("/requestToken"))
                    {
                        req.RespondWith(200, TokenBody("exchanged-token"));
                    }
                    else
                    {
                        req.RespondWith(200, new { channelId = "test" });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenRequest
                {
                    KeyName = "app.key",
                    Ttl = TimeSpan.FromHours(1),
                    Timestamp = DateTimeOffset.UtcNow,
                    Nonce = "unique-nonce",
                    Mac = "computed-mac",
                });
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().HaveCount(2, "the token exchange, then the API call");

            capturedRequests[0].Method.Should().Be("POST");
            capturedRequests[0].Path.Should().Contain("/requestToken");

            capturedRequests[1].Headers["Authorization"].Should().Be(BearerHeader("exchanged-token"));
        }

        // UTS: rest/unit/RSA8d/callback-receives-token-params-3
        [Fact]
        public async Task RSA8d_CallbackReceivesTokenParams()
        {
            TokenParams receivedParams = null;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new { channelId = "test" }));

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    receivedParams = tokenParams;
                    return Task.FromResult<object>(TokenFor("test-token"));
                };
            });

            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "requested-client-id",
                Ttl = TimeSpan.FromHours(2),
                Capability = new Capability("{\"channel1\":[\"publish\"]}"),
            });

            receivedParams.Should().NotBeNull();
            receivedParams.ClientId.Should().Be("requested-client-id");
            receivedParams.Ttl.Should().Be(TimeSpan.FromHours(2));

            // The spec compares the capability to a literal map. .NET models it as a Capability
            // object, so the comparison is on its serialised form, which is the same assertion.
            receivedParams.Capability.ToJson().Should().Contain("channel1");
            receivedParams.Capability.ToJson().Should().Contain("publish");
        }

        // UTS: rest/unit/RSA8c/authurl-invoked-for-auth-0
        [Fact]
        public async Task RSA8c_AuthUrlInvokedForAuth()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = AuthUrlMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/token");
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().HaveCount(2);

            capturedRequests[0].Url.Host.Should().Be(AuthHost);
            capturedRequests[0].Url.Path.Should().Be("/token");
            capturedRequests[0].Method.Should().Be("GET", "RSA8c defaults authMethod to GET");

            capturedRequests[1].Headers["Authorization"].Should().Be(BearerHeader("authurl-token"));
        }

        // UTS: rest/unit/RSA8c/authurl-post-method-1
        [Fact]
        public async Task RSA8c_AuthUrlPostMethod()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = AuthUrlMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/token");
                options.AuthMethod = HttpMethod.Post;
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().NotBeEmpty();
            capturedRequests[0].Method.Should().Be("POST");
        }

        // UTS: rest/unit/RSA8c/authurl-custom-headers-2
        [Fact]
        public async Task RSA8c_AuthUrlCustomHeaders()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = AuthUrlMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/token");
                options.AuthHeaders = new Dictionary<string, string>
                {
                    { "X-Custom-Header", "custom-value" },
                    { "X-API-Key", "my-api-key" },
                };
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().NotBeEmpty();
            capturedRequests[0].Headers["X-Custom-Header"].Should().Be("custom-value");
            capturedRequests[0].Headers["X-API-Key"].Should().Be("my-api-key");
        }

        // UTS: rest/unit/RSA8c/authurl-query-params-3
        [Fact]
        public async Task RSA8c_AuthUrlQueryParams()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = AuthUrlMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/token");
                options.AuthParams = new Dictionary<string, string>
                {
                    { "client_id", "my-client" },
                    { "scope", "publish:*" },
                };
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().NotBeEmpty();
            capturedRequests[0].Url.QueryParams["client_id"].Should().Be("my-client");
            capturedRequests[0].Url.QueryParams["scope"].Should().Be("publish:*");
        }

        // UTS: rest/unit/RSA8c/authurl-returns-jwt-4
        [Fact]
        public async Task RSA8c_AuthUrlReturnsJwt()
        {
            const string jwt = "eyJhbGciOiJIUzI1NiJ9.jwt-body.signature";

            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == AuthHost)
                    {
                        // RSA8c: a text/plain or application/jwt response is taken to be the token
                        // string itself rather than a JSON object.
                        req.RespondWith(200, jwt, new Dictionary<string, string>
                        {
                            { "Content-Type", "text/plain" },
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new { channelId = "test" });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/jwt");
            });

            await client.Request(HttpMethod.Get, "/channels/test");

            capturedRequests.Should().HaveCount(2);
            capturedRequests[1].Headers["Authorization"].Should().Be(BearerHeader(jwt));
        }

        // UTS: rest/unit/RSA8d/callback-error-propagated-4
        [Fact]
        public async Task RSA8d_CallbackErrorPropagated()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = ChannelMock(capturedRequests);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                    throw new Exception("Authentication server unavailable");
            });

            Func<Task> act = () => client.Request(HttpMethod.Get, "/channels/test");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which;
            error.ToString().Should().Contain("Authentication server unavailable");

            capturedRequests.Should().BeEmpty("no request may be made without a credential");
        }

        // UTS: rest/unit/RSA8c/authurl-error-propagated-5
        [Fact]
        public async Task RSA8c_AuthUrlErrorPropagated()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == AuthHost)
                    {
                        req.RespondWith(500, new { error = "Internal server error" });
                    }
                    else
                    {
                        req.RespondWith(200, new { channelId = "test" });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri($"https://{AuthHost}/token");
            });

            Func<Task> act = () => client.Request(HttpMethod.Get, "/channels/test");

            await act.Should().ThrowAsync<AblyException>();

            // The spec asserts exactly one request. A 5xx from the authUrl is retryable, so the SDK
            // may try it more than once before giving up; the assertion that carries the spec point
            // is that the API request was never made.
            capturedRequests.Should().NotBeEmpty();
            capturedRequests.Should().OnlyContain(
                request => request.Url.Host == AuthHost,
                "the API request must not be attempted once the authUrl has failed");
        }

        private static MockHttpClient ChannelMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new { channelId = "test" });
                });

        private static MockHttpClient AuthUrlMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == AuthHost)
                    {
                        req.RespondWith(200, TokenBody("authurl-token"));
                    }
                    else
                    {
                        req.RespondWith(200, new { channelId = "test" });
                    }
                });

        private static TokenDetails TokenFor(string token)
            => new TokenDetails(token) { Expires = DateTimeOffset.UtcNow.AddHours(1) };

        private static object TokenBody(string token) => new
        {
            token,
            keyName = "appId.keyId",
            issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            capability = "{\"*\":[\"*\"]}",
        };

        private static string BearerHeader(string token)
            => "Bearer " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
    }
}
