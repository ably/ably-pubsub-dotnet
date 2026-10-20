// Auth.authorize() Tests (RSA10, RSA10a, RSA10b, RSA10e, RSA10g, RSA10h, RSA10i, RSA10j, RSA10k, RSA10l)
// Derived from uts/rest/unit/auth/authorize.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: Auth#tokenDetails (RSA16) is not public in this SDK; RSA10g reads the internal AblyAuth.CurrentToken instead (adapted, see deviations.md)

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
    public class AuthorizeTests : UtsUnitTestBase
    {
        public AuthorizeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA10a/authorize-default-params-0
        [UtsFact]
        [Trait("spec", "RSA10a")]
        public async Task RSA10a_AuthorizeDefaultParams()
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
                            ["keyName"] = "appId.keyId",
                        });
                    }
                    else
                    {
                        // Subsequent request to verify token is used
                        req.RespondWith(200, new JObject
                        {
                            ["channelId"] = "test",
                            ["status"] = new JObject { ["isActive"] = true },
                        });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var tokenDetails = await client.Auth.AuthorizeAsync();

            // Assertions
            // ASSERT token_details IS TokenDetails: guaranteed at compile time (AuthorizeAsync returns TokenDetails)
            tokenDetails.Token.Should().Be("obtained-token");

            // Verify token is now used for requests
            await client.Channels.Get("test").StatusAsync();
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            capturedRequests.Snapshot().Last().Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("obtained-token")));
        }

        // UTS: rest/unit/RSA10b/authorize-explicit-params-0
        [UtsFact]
        [Trait("spec", "RSA10b")]
        public async Task RSA10b_AuthorizeExplicitParams()
        {
            // Setup
            var callbackParams = new CaptureList<TokenParams>();

            Func<TokenParams, Task<object>> mockAuthCallback = tokenParams =>
            {
                callbackParams.Add(tokenParams);
                return Task.FromResult<object>(new TokenDetails("callback-token")
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

            var client = Rest(new ClientOptions
            {
                AuthCallback = mockAuthCallback,

                // Default TokenParams
                ClientId = "default-client",
            });

            // Test Steps
            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "override-client",
                Ttl = TimeSpan.FromMilliseconds(7200000),
            });

            // Assertions
            var receivedParams = callbackParams[0];

            // Overridden
            receivedParams.ClientId.Should().Be("override-client");
            receivedParams.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
        }

        // UTS: rest/unit/RSA10e/authorize-saves-params-0
        [UtsFact]
        [Trait("spec", "RSA10e")]
        public async Task RSA10e_AuthorizeSavesParams()
        {
            // Setup
            var callbackInvocations = new CaptureList<TokenParams>();

            Func<TokenParams, Task<object>> mockAuthCallback = tokenParams =>
            {
                callbackInvocations.Add(tokenParams);
                return Task.FromResult<object>(new TokenDetails("token-" + callbackInvocations.Count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = mockAuthCallback });

            // Test Steps
            // First authorize with custom params
            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "saved-client",
                Ttl = TimeSpan.FromMilliseconds(3600000),
            });

            // Second authorize without explicit params — RSA10a: authorize() always requests
            // a new token, so the callback runs again and must receive the saved params.
            // (Deterministic: no short-expiry token or real-time wait needed.)
            await client.Auth.AuthorizeAsync();

            // Assertions
            callbackInvocations.Should().HaveCount(2);

            // Second callback should have received the saved params
            callbackInvocations[1].ClientId.Should().Be("saved-client");
            callbackInvocations[1].Ttl.Should().Be(TimeSpan.FromMilliseconds(3600000));
        }

        // UTS: rest/unit/RSA10g/authorize-updates-token-details-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RSA10g")]
        public async Task RSA10g_AuthorizeUpdatesTokenDetails()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["token"] = "new-token",
                        ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                        ["keyName"] = "appId.keyId",
                        ["clientId"] = "token-client",
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Before authorize
            // DEVIATION: Auth#tokenDetails is not public; the internal AblyAuth.CurrentToken is read instead (see deviations.md)
            ((AblyAuth)client.Auth).CurrentToken.Should().BeNull();

            var result = await client.Auth.AuthorizeAsync();

            // Assertions
            ((AblyAuth)client.Auth).CurrentToken.Should().NotBeNull();
            ((AblyAuth)client.Auth).CurrentToken.Token.Should().Be("new-token");
            ((AblyAuth)client.Auth).CurrentToken.ClientId.Should().Be("token-client");

            // Same object
            ((AblyAuth)client.Auth).CurrentToken.Should().BeSameAs(result);
        }

        // UTS: rest/unit/RSA10h/authorize-replaces-auth-options-0
        [UtsFact]
        [Trait("spec", "RSA10h")]
        public async Task RSA10h_AuthorizeReplacesAuthOptions()
        {
            // Setup
            var originalCallbackCalled = false;
            var newCallbackCalled = false;

            Func<TokenParams, Task<object>> originalCallback = tokenParams =>
            {
                originalCallbackCalled = true;
                return Task.FromResult<object>(new TokenDetails("original")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            Func<TokenParams, Task<object>> newCallback = tokenParams =>
            {
                newCallbackCalled = true;
                return Task.FromResult<object>(new TokenDetails("new")
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

            var client = Rest(new ClientOptions { AuthCallback = originalCallback });

            // Test Steps
            await client.Auth.AuthorizeAsync(null, new AuthOptions { AuthCallback = newCallback });

            // Assertions
            originalCallbackCalled.Should().BeFalse();
            newCallbackCalled.Should().BeTrue();
        }

        // UTS: rest/unit/RSA10i/authorize-preserves-key-0
        [UtsFact]
        [Trait("spec", "RSA10i")]
        public async Task RSA10i_AuthorizePreservesKey()
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
                        // Initial token request using key
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "token-via-key",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                            ["keyName"] = "appId.keyId",
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject { ["channelId"] = "test" });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Call authorize with new authUrl but no key
            await client.Auth.AuthorizeAsync(null, new AuthOptions { AuthUrl = new Uri("https://new-auth.example.com/token") });

            // The key should still be available for signing
            // Implementation can still use key for requestToken

            // Assertions
            // NOTE: no assertion: the spec's Assertions section holds only comments (it depends on whether auth.key is exposed)
            // Key from constructor should be preserved (not cleared)
            // Exact assertion depends on whether auth.key is exposed
            // Verify by checking that key-based operations still work
        }

        // UTS: rest/unit/RSA10j/authorize-replaces-existing-token-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RSA10j")]
        public async Task RSA10j_AuthorizeReplacesExistingToken()
        {
            // Setup
            var tokenCount = 0;

            Func<TokenParams, Task<object>> mockAuthCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref tokenCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
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

            var client = Rest(new ClientOptions { AuthCallback = mockAuthCallback });

            // Test Steps
            var result1 = await client.Auth.AuthorizeAsync();
            var result2 = await client.Auth.AuthorizeAsync();

            // Assertions
            result1.Token.Should().Be("token-1");
            result2.Token.Should().Be("token-2");

            // DEVIATION: Auth#tokenDetails is not public; the internal AblyAuth.CurrentToken is read instead (see deviations.md)
            ((AblyAuth)client.Auth).CurrentToken.Token.Should().Be("token-2");
        }

        // UTS: rest/unit/RSA10k/authorize-query-time-0
        [UtsFact(Skip = "UTS spec error RSA10k: the /time stub answers a time object instead of an [ms] array the Ably /time endpoint returns; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA10k")]
        public Task RSA10k_AuthorizeQueryTime()
        {
            // SPEC ERROR RSA10k: the mock answers /time with a JSON object, but the endpoint returns an array of milliseconds (as the spec's own time.md stubs show), so queryTime cannot work against this stub.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA10k — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA10l/authorize-error-propagated-0
        [UtsFact]
        [Trait("spec", "RSA10l")]
        public async Task RSA10l_AuthorizeErrorPropagated()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(401, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40100,
                            ["statusCode"] = 401,
                            ["message"] = "Unauthorized",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "invalid.key:secret" });

            // Test Steps
            Func<Task> act = () => client.Auth.AuthorizeAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40100);
            ((int?)error.StatusCode).Should().Be(401);
        }
    }
}
