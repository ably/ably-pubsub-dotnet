// Auth Scheme Selection Tests (RSA1, RSA2, RSA3, RSA4, RSA4a2, RSA11, RSC1b, RSC18)
// Derived from uts/rest/unit/auth/auth_scheme.md
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
    public class AuthSchemeTests : UtsUnitTestBase
    {
        public AuthSchemeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA4/basic-auth-key-only-0
        [UtsFact]
        [Trait("spec", "RSA4")]
        public async Task RSA4_BasicAuthKeyOnly()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var request = capturedRequests[0];

            // Basic auth header uses base64-encoded key
            var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("appId.keyId:keySecret"));
            request.Headers["Authorization"].Should().Be(expectedAuth);
        }

        // UTS: rest/unit/RSA3/token-auth-explicit-token-0
        [UtsFact(Skip = "UTS spec error RSA3b: the test's subject is the exact plain Authorization: Bearer value, but RSA3b makes Base64-encoding of the token optional; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA3")]
        public Task RSA3_TokenAuthExplicitToken()
        {
            // SPEC ERROR RSA3b: RSA3b says the token string is "optionally Base64-encoded" in the Authorization: Bearer header; this test checks only the header value, so asserting the plain token over-specifies (the SDK sends it Base64-encoded).
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA3b — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA3/token-auth-token-details-1
        [UtsFact(Skip = "UTS spec error RSA3b: the test's subject is the exact plain Authorization: Bearer value, but RSA3b makes Base64-encoding of the token optional; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA3")]
        public Task RSA3_TokenAuthTokenDetails()
        {
            // SPEC ERROR RSA3b: RSA3b says the token string is "optionally Base64-encoded" in the Authorization: Bearer header; this test checks only the header value, so asserting the plain token over-specifies (the SDK sends it Base64-encoded).
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA3b — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA4/use-token-auth-forced-1
        [UtsFact]
        [Trait("spec", "RSA4")]
        public async Task RSA4_UseTokenAuthForced()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (Regex.IsMatch(req.Path, "/keys/.*/requestToken"))
                    {
                        // Token request
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "obtained-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        // API request
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseTokenAuth = true,
            });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // Should obtain token rather than use Basic auth
            var apiRequest = capturedRequests[1];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            apiRequest.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("obtained-token")));
        }

        // UTS: rest/unit/RSA4/auth-callback-triggers-token-2
        [UtsFact]
        [Trait("spec", "RSA4")]
        public async Task RSA4_AuthCallbackTriggersToken()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
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
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var request = capturedRequests[0];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            request.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-token")));
        }

        // UTS: rest/unit/RSA4/authurl-triggers-token-3
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4")]
        public async Task RSA4_AuthUrlTriggersToken()
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
                        // authUrl response
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "authurl-token",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        });
                    }
                    else
                    {
                        // API request
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthUrl = new Uri("https://auth.example.com/token") });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var apiRequest = capturedRequests[1];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            apiRequest.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("authurl-token")));
        }

        // UTS: rest/unit/RSC1b/no-auth-method-error-0
        [Fact]
        [Trait("spec", "RSC1b")]
        public void RSC1b_NoAuthMethodError()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            // No key, token, or auth callback
            // NOTE: RSC1b does not say when the 40106 is raised; the SDK raises it at construction. The UTS spec expects it on the first request — see deviations.md (UTS Spec Errors, RSC1b)
            Action create = () => Rest(new ClientOptions());

            // Test Steps
            var error = create.Should().Throw<AblyException>().Which.ErrorInfo;

            // No authentication method
            error.Code.Should().Be(40106);

            // Assertions
            // No HTTP request should have been made
            capturedRequests.Should().HaveCount(0);
        }

        // UTS: rest/unit/RSA4a2/expired-token-no-renewal-0
        [UtsFact(Skip = "UTS spec error RSA4a2: the test expects a 40171 error and no HTTP request for a locally expired static token; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA4a2")]
        public Task RSA4a2_ExpiredTokenNoRenewal()
        {
            // SPEC ERROR RSA4a2: RSA4a2 applies when the server responds with a token error, and RSA4b1 makes local expiry detection optional (it needs known server time); features.md does not require failing a request locally on an expired static token.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA4a2 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA1/token-auth-takes-precedence-0
        [UtsFact]
        [Trait("spec", "RSA1")]
        public async Task RSA1_TokenAuthTakesPrecedence()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
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

            // Both key and authCallback provided
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AuthCallback = authCallback,
            });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            // authCallback takes precedence, so Bearer auth is used
            var request = capturedRequests[0];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            request.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-token")));
        }

        // UTS: rest/unit/RSA2/basic-auth-header-format-0
        [UtsFact]
        [Trait("spec", "RSA2")]
        public async Task RSA2_BasicAuthHeaderFormat()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["channelId"] = "test" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "app123.key456:secretXYZ" });

            // Test Steps
            await client.Request(HttpMethod.Get, "/channels/test");

            // Assertions
            var request = capturedRequests[0];

            // Verify exact Base64 encoding
            // "app123.key456:secretXYZ" base64 encoded
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("app123.key456:secretXYZ"));
            request.Headers["Authorization"].Should().Be(expected);

            // The Base64 should NOT have URL-safe encoding (+ and / are valid)
            request.Headers["Authorization"].Should().Contain("Basic ");
        }

        // UTS: rest/unit/RSC18/token-auth-over-non-tls-0
        [UtsFact]
        [Trait("spec", "RSC18")]
        public async Task RSC18_TokenAuthOverNonTls()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Token = "explicit-token",

                // Non-TLS allowed for token auth
                Tls = false,
            });

            // Test Steps
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            var request = capturedRequests[0];
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            request.Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("explicit-token")));

            // Request should use http:// (non-TLS)
            request.Url.Scheme.Should().Be("http");
        }
    }
}
