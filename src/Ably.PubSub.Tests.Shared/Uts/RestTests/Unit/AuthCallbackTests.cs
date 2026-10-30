// Auth Callback Tests (RSA8c, RSA8d)
// Derived from uts/rest/unit/auth/auth_callback.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class AuthCallbackTests : UtsUnitTestBase
    {
        public AuthCallbackTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA8d/callback-invoked-for-auth-0
        [UtsFact]
        [Trait("spec", "RSA8d")]
        public async Task RSA8d_CallbackInvokedForAuth()
        {
            // Setup
            var callbackInvoked = false;
            TokenParams callbackParams = null;
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                callbackInvoked = true;
                callbackParams = tokenParams;
                return Task.FromResult<object>(new TokenDetails("callback-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            // Make a request that requires authentication
            var result = await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // authCallback was invoked
            callbackInvoked.Should().BeTrue();

            // Request used the token from authCallback
            capturedRequests.Should().HaveCount(1);
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            capturedRequests[0].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-token")));
        }

        // UTS: rest/unit/RSA8d/callback-returns-jwt-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA8d")]
        public async Task RSA8d_CallbackReturnsJwt()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                // Return raw JWT string instead of TokenDetails
                return Task.FromResult<object>("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test-jwt-payload");
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // Request used the JWT from authCallback
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            capturedRequests[0].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test-jwt-payload")));
        }

        // UTS: rest/unit/RSA8d/callback-returns-token-request-2
        [UtsFact]
        [Trait("spec", "RSA8d")]
        public async Task RSA8d_CallbackReturnsTokenRequest()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                // Return a TokenRequest (to be exchanged for token)
                return Task.FromResult<object>(new TokenRequest
                {
                    KeyName = "app.key",
                    Ttl = TimeSpan.FromMilliseconds(3600000),
                    Timestamp = DateTimeOffset.UtcNow,
                    Nonce = "unique-nonce",
                    Mac = "computed-mac",
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (Regex.IsMatch(req.Path, "/keys/.*/requestToken"))
                    {
                        // First request exchanges TokenRequest for TokenDetails
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "exchanged-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        // Second request is the actual API call
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // Two HTTP requests: token exchange + API call
            capturedRequests.Should().HaveCount(2);

            // First request was POST to /keys/{keyName}/requestToken
            var firstRequest = capturedRequests[0];
            firstRequest.Method.Should().Be("POST");
            firstRequest.Path.Should().MatchRegex("/keys/.*/requestToken");

            // Second request used the exchanged token
            var secondRequest = capturedRequests[1];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            secondRequest.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("exchanged-token")));
        }

        // UTS: rest/unit/RSA8d/callback-receives-token-params-3
        [UtsFact]
        [Trait("spec", "RSA8d")]
        public async Task RSA8d_CallbackReceivesTokenParams()
        {
            // Setup
            TokenParams receivedParams = null;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                receivedParams = tokenParams;
                return Task.FromResult<object>(new TokenDetails("test-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "requested-client-id",
                Ttl = TimeSpan.FromMilliseconds(7200000),
                Capability = new Capability("{\"channel1\":[\"publish\"]}"),
            });

            // Assertions
            // authCallback received the TokenParams
            receivedParams.ClientId.Should().Be("requested-client-id");
            receivedParams.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
            receivedParams.Capability.ToJson().Should().Be("{\"channel1\":[\"publish\"]}");
        }

        // UTS: rest/unit/RSA8c/authurl-invoked-for-auth-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlInvokedForAuth()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        // authUrl returns TokenDetails
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "authurl-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        // Actual API request
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthUrl = new Uri("https://auth.example.com/token") });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // First request was to authUrl
            var authRequest = capturedRequests[0];
            authRequest.Url.Host.Should().Be("auth.example.com");
            authRequest.Url.Path.Should().Be("/token");
            authRequest.Method.Should().Be("GET");

            // Second request used the token from authUrl
            var apiRequest = capturedRequests[1];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            apiRequest.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("authurl-token")));
        }

        // UTS: rest/unit/RSA8c/authurl-post-method-1
        [UtsFact]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlPostMethod()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "authurl-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthMethod = HttpMethod.Post,
            });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // authUrl request used POST method
            var authRequest = capturedRequests[0];
            authRequest.Method.Should().Be("POST");
        }

        // UTS: rest/unit/RSA8c/authurl-custom-headers-2
        [UtsFact]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlCustomHeaders()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "authurl-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthHeaders = new Dictionary<string, string>
                {
                    ["X-Custom-Header"] = "custom-value",
                    ["X-API-Key"] = "my-api-key",
                },
            });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var authRequest = capturedRequests[0];
            authRequest.Headers["X-Custom-Header"].Should().Be("custom-value");
            authRequest.Headers["X-API-Key"].Should().Be("my-api-key");
        }

        // UTS: rest/unit/RSA8c/authurl-query-params-3
        [UtsFact]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlQueryParams()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "authurl-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthParams = new Dictionary<string, string>
                {
                    ["client_id"] = "my-client",
                    ["scope"] = "publish:*",
                },
            });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var authRequest = capturedRequests[0];
            authRequest.Url.QueryParams["client_id"].Should().Be("my-client");
            authRequest.Url.QueryParams["scope"].Should().Be("publish:*");
        }

        // UTS: rest/unit/RSA8c/authurl-returns-jwt-4
        [UtsFact]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlReturnsJwt()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        // authUrl returns plain text JWT (not JSON)
                        req.RespondWith(
                            200,
                            "eyJhbGciOiJIUzI1NiJ9.jwt-body.signature",
                            new Dictionary<string, string> { ["Content-Type"] = "text/plain" });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthUrl = new Uri("https://auth.example.com/jwt") });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var apiRequest = capturedRequests[1];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            apiRequest.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("eyJhbGciOiJIUzI1NiJ9.jwt-body.signature")));
        }

        // UTS: rest/unit/RSA8d/callback-error-propagated-4
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4e", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA8d")]
        public async Task RSA8d_CallbackErrorPropagated()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            Func<TokenParams, Task<object>> authCallback = tokenParams => throw new Exception("Authentication server unavailable");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            Func<Task> act = () => client.Request(HttpMethod.Get, "/channels/test");
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Error should indicate auth failure
            error.Message.Should().Contain("Authentication server unavailable");

            // Assertions
            // No HTTP requests should have been made
            capturedRequests.Should().HaveCount(0);
        }

        // UTS: rest/unit/RSA8c/authurl-error-propagated-5
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA8c")]
        public async Task RSA8c_AuthUrlErrorPropagated()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        // authUrl returns error
                        req.RespondWith(500, new JObject { ["error"] = "Internal server error" });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthUrl = new Uri("https://auth.example.com/token") });

            // Test Steps
            Func<Task> act = () => client.Request(HttpMethod.Get, "/channels/test");
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            (((int?)error.StatusCode == 500) || (error.Message != null && error.Message.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)).Should().BeTrue("error.statusCode == 500 OR error.message CONTAINS \"auth\"");

            // Assertions
            // Only authUrl request was made, not the API request
            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.Host.Should().Be("auth.example.com");
        }
    }
}
