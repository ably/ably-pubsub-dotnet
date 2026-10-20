// Auth.tokenDetails Tests (RSA16, RSA16a, RSA16b, RSA16c, RSA16d)
// Derived from uts/rest/unit/auth/token_details.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: Auth#tokenDetails (RSA16) is not public in this SDK; every read goes through the internal AblyAuth.CurrentToken (adapted, see deviations.md); TokenDetails.Expires and Issued are non-nullable, so "IS null" is checked as DateTimeOffset.MinValue; TestClock/WITH_CLOCK is EnableFakeTimers() + AdvanceTime (infra stand-in)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    public class TokenDetailsTests : UtsUnitTestBase
    {
        public TokenDetailsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA16a/token-from-callback-0
        [UtsFact]
        [Trait("spec", "RSA16a")]
        public async Task RSA16a_TokenFromCallback()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("callback-token-abc")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    Issued = DateTimeOffset.UtcNow,
                    ClientId = "my-client",
                }),
            });

            // Test Steps
            // Force token acquisition by making a request
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().NotBeNull();
            CurrentTokenDetails(client).Token.Should().Be("callback-token-abc");
            CurrentTokenDetails(client).ClientId.Should().Be("my-client");

            // NOTE: Expires and Issued are non-nullable DateTimeOffset (unset is MinValue), so "IS NOT null" is checked as "is set"
            CurrentTokenDetails(client).Expires.Should().NotBe(DateTimeOffset.MinValue);
            CurrentTokenDetails(client).Issued.Should().NotBe(DateTimeOffset.MinValue);
        }

        // UTS: rest/unit/RSA16a/token-from-request-token-1
        [UtsFact]
        [Trait("spec", "RSA16a")]
        public async Task RSA16a_TokenFromRequestToken()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Regex.IsMatch(req.Path, "/keys/.*/requestToken"))
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["token"] = "requested-token-xyz",
                            ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                            ["issued"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ["keyName"] = "appId.keyId",
                            ["clientId"] = "token-client",
                        });
                    }
                    else
                    {
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
            // Explicitly authorize to get a token
            await client.Auth.AuthorizeAsync();

            // Assertions
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().NotBeNull();
            CurrentTokenDetails(client).Token.Should().Be("requested-token-xyz");
            CurrentTokenDetails(client).ClientId.Should().Be("token-client");
        }

        // UTS: rest/unit/RSA16b/token-string-in-options-0
        [Fact]
        [Trait("spec", "RSA16b")]
        public void RSA16b_TokenStringInOptions()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            // Provide only a token string, not full TokenDetails
            var client = Rest(new ClientOptions { Token = "standalone-token-string" });

            // Test Steps
            // Access tokenDetails immediately after construction
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var tokenDetails = CurrentTokenDetails(client);

            // Assertions
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("standalone-token-string");

            // Other fields should be null since we only had the token string
            // NOTE: Expires and Issued are non-nullable DateTimeOffset (unset is MinValue), so "IS null" is checked as "is unset"
            tokenDetails.Expires.Should().Be(DateTimeOffset.MinValue);
            tokenDetails.Issued.Should().Be(DateTimeOffset.MinValue);
            tokenDetails.ClientId.Should().BeNull();
            tokenDetails.Capability.Should().BeNull();
        }

        // UTS: rest/unit/RSA16b/token-string-from-callback-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA16b")]
        public async Task RSA16b_TokenStringFromCallback()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            // authCallback returns just a token string, not TokenDetails
            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>("just-a-token-string"),
            });

            // Test Steps
            // Force token acquisition
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().NotBeNull();
            CurrentTokenDetails(client).Token.Should().Be("just-a-token-string");

            // Other fields should be null
            // NOTE: Expires and Issued are non-nullable DateTimeOffset (unset is MinValue), so "IS null" is checked as "is unset"
            CurrentTokenDetails(client).Expires.Should().Be(DateTimeOffset.MinValue);
            CurrentTokenDetails(client).Issued.Should().Be(DateTimeOffset.MinValue);
        }

        // UTS: rest/unit/RSA16c/set-on-instantiation-0
        [Fact]
        [Trait("spec", "RSA16c")]
        public void RSA16c_SetOnInstantiation()
        {
            // Setup
            var initialToken = new TokenDetails("initial-token")
            {
                Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                Issued = DateTimeOffset.UtcNow,
                ClientId = "initial-client",
            };

            var client = Rest(new ClientOptions { TokenDetails = initialToken });

            // Test Steps
            // Access tokenDetails immediately after construction
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var tokenDetails = CurrentTokenDetails(client);

            // Assertions
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("initial-token");
            tokenDetails.ClientId.Should().Be("initial-client");
        }

        // UTS: rest/unit/RSA16c/updated-after-authorize-1
        [UtsFact]
        [Trait("spec", "RSA16c")]
        public async Task RSA16c_UpdatedAfterAuthorize()
        {
            // Setup
            var tokenCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref tokenCount);
                    return Task.FromResult<object>(new TokenDetails("token-v" + count)
                    {
                        Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                        ClientId = "client-v" + count,
                    });
                },
            });

            // Test Steps
            // First authorize
            await client.Auth.AuthorizeAsync();

            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var firstToken = CurrentTokenDetails(client);

            // Second authorize
            await client.Auth.AuthorizeAsync();
            var secondToken = CurrentTokenDetails(client);

            // Assertions
            firstToken.Token.Should().Be("token-v1");
            firstToken.ClientId.Should().Be("client-v1");

            secondToken.Token.Should().Be("token-v2");
            secondToken.ClientId.Should().Be("client-v2");

            // Verify it's actually updated, not the same object
            firstToken.Token.Should().NotBe(secondToken.Token);
        }

        // UTS: rest/unit/RSA16c/updated-after-expiry-renewal-2
        [UtsFact(Skip = "UTS spec error RSA4b1: the test expects a local expiry check without any server time; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA16c")]
        public Task RSA16c_UpdatedAfterExpiryRenewal()
        {
            // SPEC ERROR RSA4b1: RSA4b1 makes pre-emptive renewal optional and only when the library has queried the Ably server time (RSA10k) and the expiry has passed on server time; this test advances/uses only the local clock.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA4b1 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA16c/updated-after-40142-renewal-3
        [UtsFact]
        [Trait("spec", "RSA16c")]
        public async Task RSA16c_UpdatedAfter40142Renewal()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First request fails with token expired error
                        req.RespondWith(401, new JObject
                        {
                            ["error"] = new JObject
                            {
                                ["code"] = 40142,
                                ["statusCode"] = 401,
                                ["message"] = "Token expired",
                            },
                        });
                    }
                    else
                    {
                        // Subsequent requests succeed
                        req.RespondWith(200, new JObject
                        {
                            ["channelId"] = "test",
                            ["status"] = new JObject { ["isActive"] = true },
                        });
                    }
                });
            InstallMock(mockHttp);

            var tokenCount = 0;

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref tokenCount);
                    return Task.FromResult<object>(new TokenDetails("token-v" + count)
                    {
                        Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                        ClientId = "client-v" + count,
                    });
                },
            });

            // Test Steps
            // First get a token
            await client.Auth.AuthorizeAsync();

            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var firstToken = CurrentTokenDetails(client);

            // Make a request that will fail with 40142, triggering renewal
            await client.Channels.Get("test").StatusAsync();
            var secondToken = CurrentTokenDetails(client);

            // Assertions
            firstToken.Token.Should().Be("token-v1");
            secondToken.Token.Should().Be("token-v2");
        }

        // UTS: rest/unit/RSA16d/null-with-basic-auth-0
        [UtsFact]
        [Trait("spec", "RSA16d")]
        public async Task RSA16d_NullWithBasicAuth()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            // Client with only API key - uses basic auth
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Make a request using basic auth (no token)
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            // Should be null because we're using basic auth, not token auth
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().BeNull();
        }

        // UTS: rest/unit/RSA16d/null-before-token-obtained-1
        [Fact]
        [Trait("spec", "RSA16d")]
        public void RSA16d_NullBeforeTokenObtained()
        {
            // Setup
            // Client configured for token auth but no request made yet
            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("my-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                }),
            });

            // Test Steps
            // Don't make any requests - just check tokenDetails
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var tokenDetails = CurrentTokenDetails(client);

            // Assertions
            // Should be null because no token has been obtained yet
            tokenDetails.Should().BeNull();
        }

        // UTS: rest/unit/RSA16d/null-after-invalidation-2
        // DEVIATION: see deviations.md
        [DeviationFact("RSA16d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA16d")]
        public async Task RSA16d_NullAfterInvalidation()
        {
            // Setup
            var callbackCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    // Always fail with token error
                    req.RespondWith(401, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40142,
                            ["statusCode"] = 401,
                            ["message"] = "Token expired",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    if (Interlocked.Increment(ref callbackCount) == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("first-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                        });
                    }

                    // Second callback fails - cannot renew
                    throw new AblyException("Cannot obtain new token");
                },
            });

            // Test Steps
            // First authorize succeeds
            await client.Auth.AuthorizeAsync();

            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().NotBeNull();
            CurrentTokenDetails(client).Token.Should().Be("first-token");

            // Make a request that fails with 40142
            // Renewal will be attempted but will fail
            Func<Task> act = () => client.Channels.Get("test").StatusAsync();
            await act.Should().ThrowAsync<AblyException>();

            // Expected to fail - error is expected

            // Assertions
            // After failed renewal, tokenDetails should be null
            // (the old token is invalid and we couldn't get a new one)
            CurrentTokenDetails(client).Should().BeNull();
        }

        // UTS: rest/unit/RSA16d/null-after-switch-to-basic-3
        [UtsFact(Skip = "UTS spec error RSA10a: the test expects authorize() with useTokenAuth: false to switch to Basic auth; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA16d")]
        public Task RSA16d_NullAfterSwitchToBasic()
        {
            // SPEC ERROR RSA10a: RSA10a says authorize() creates a token immediately and ensures Token Auth is used for all future requests, so it cannot switch the client to Basic auth.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA10a — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA16a/preserved-across-requests-0
        [UtsFact]
        [Trait("spec", "RSA16a")]
        public async Task RSA16a_PreservedAcrossRequests()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("stable-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    ClientId = "stable-client",
                }),
            });

            // Test Steps
            // Make multiple requests
            await client.Channels.Get("test").StatusAsync();

            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            var firstCheck = CurrentTokenDetails(client);

            await client.Channels.Get("test").StatusAsync();
            var secondCheck = CurrentTokenDetails(client);

            await client.Channels.Get("test").StatusAsync();
            var thirdCheck = CurrentTokenDetails(client);

            // Assertions
            // Token should remain the same across requests (not re-fetched)
            firstCheck.Token.Should().Be("stable-token");
            secondCheck.Token.Should().Be("stable-token");
            thirdCheck.Token.Should().Be("stable-token");
        }

        // UTS: rest/unit/RSA16a/reflects-capability-1
        [UtsFact]
        [Trait("spec", "RSA16a")]
        public async Task RSA16a_ReflectsCapability()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject
                {
                    ["channelId"] = "test",
                    ["status"] = new JObject { ["isActive"] = true },
                }));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("capable-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    Capability = new Capability("{\"channel1\":[\"publish\",\"subscribe\"],\"channel2\":[\"subscribe\"]}"),
                }),
            });

            // Test Steps
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            // DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken
            CurrentTokenDetails(client).Should().NotBeNull();
            CurrentTokenDetails(client).Capability.ToJson().Should().Be("{\"channel1\":[\"publish\",\"subscribe\"],\"channel2\":[\"subscribe\"]}");
        }

        // Reads the spec's Auth#tokenDetails through the internal AblyAuth.CurrentToken; each caller marks its
        // first read with the DEVIATION comment (see deviations.md, Adapted Tests, RSA16).
        private static TokenDetails CurrentTokenDetails(PubSubHttpClient client) => ((AblyAuth)client.Auth).CurrentToken;
    }
}
