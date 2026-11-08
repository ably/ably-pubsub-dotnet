// Client ID Tests (RSA7, RSA7a, RSA7b, RSA7c, RSA12, RSA12a, RSA12b, RSA15, RSA15a, RSA15b, RSA15c)
// Derived from uts/rest/unit/auth/client_id.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    public class ClientIdTests : UtsUnitTestBase
    {
        public ClientIdTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA7a/clientid-from-options-0
        [Fact]
        [Trait("spec", "RSA7a")]
        public void RSA7a_ClientidFromOptions()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "my-client-id",
            });

            // Assertions
            client.Auth.ClientId.Should().Be("my-client-id");
        }

        // UTS: rest/unit/RSA7b/clientid-from-token-details-0
        [Fact]
        [Trait("spec", "RSA7b")]
        public void RSA7b_ClientidFromTokenDetails()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                TokenDetails = new TokenDetails("token-with-clientId")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    ClientId = "token-client-id",
                },
            });

            // Assertions
            client.Auth.ClientId.Should().Be("token-client-id");
        }

        // UTS: rest/unit/RSA7b/clientid-from-callback-token-1
        [UtsFact]
        [Trait("spec", "RSA7b")]
        public async Task RSA7b_ClientidFromCallbackToken()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("callback-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    ClientId = "callback-client-id",
                }),
            });

            // Test Steps
            // Trigger auth by making a request
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            client.Auth.ClientId.Should().Be("callback-client-id");
        }

        // UTS: rest/unit/RSA7c/clientid-null-unidentified-0
        [Fact]
        [Trait("spec", "RSA7c")]
        public void RSA7c_ClientidNullUnidentified()
        {
            // Setup
            // No clientId specified
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Assertions
            client.Auth.ClientId.Should().BeNull();
        }

        // UTS: rest/unit/RSA7c/clientid-null-unidentified-token-1
        [Fact]
        [Trait("spec", "RSA7c")]
        public void RSA7c_ClientidNullUnidentifiedToken()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            // No clientId in token
            var client = Rest(new ClientOptions
            {
                TokenDetails = new TokenDetails("token-without-clientId")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                },
            });

            // Assertions
            client.Auth.ClientId.Should().BeNull();
        }

        // UTS: rest/unit/RSA12a/clientid-passed-to-callback-0
        [UtsFact]
        [Trait("spec", "RSA12a")]
        public async Task RSA12a_ClientidPassedToCallback()
        {
            // Setup
            var receivedParams = new CaptureList<TokenParams>();

            Func<TokenParams, Task<object>> mockAuthCallback = tokenParams =>
            {
                receivedParams.Add(tokenParams);
                return Task.FromResult<object>(new TokenDetails("tok")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                AuthCallback = mockAuthCallback,
                ClientId = "library-client-id",
            });

            // Test Steps
            // Trigger auth
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            receivedParams.Count.Should().BeGreaterThanOrEqualTo(1);
            receivedParams[0].ClientId.Should().Be("library-client-id");
        }

        // UTS: rest/unit/RSA12b/clientid-sent-to-authurl-0
        [UtsFact]
        [Trait("spec", "RSA12b")]
        public async Task RSA12b_ClientidSentToAuthurl()
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
                        req.RespondWith(
                            200,
                            new JObject
                            {
                                ["token"] = "url-token",
                                ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                            },
                            new Dictionary<string, string> { ["Content-Type"] = "application/json" });
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

            var client = Rest(new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                ClientId = "url-client-id",
            });

            // Test Steps
            await client.Channels.Get("test").StatusAsync();

            // Assertions
            var authRequest = capturedRequests[0];
            authRequest.Url.Host.Should().Be("auth.example.com");

            // clientId should be in query params (GET) or body (POST)
            if (authRequest.Method == "GET")
            {
                authRequest.Url.QueryParams["clientId"].Should().Be("url-client-id");
            }
            else
            {
                var bodyParams = new RecordedUrl(new Uri("http://localhost/?" + authRequest.BodyText)).QueryParams;
                bodyParams["clientId"].Should().Be("url-client-id");
            }
        }

        // UTS: rest/unit/RSA7/clientid-updated-after-authorize-0
        [UtsFact]
        [Trait("spec", "RSA7")]
        public async Task RSA7_ClientidUpdatedAfterAuthorize()
        {
            // Setup
            var tokenCount = 0;

            Func<TokenParams, Task<object>> mockAuthCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref tokenCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    ClientId = "client-" + count,
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = mockAuthCallback });

            // Test Steps
            // First auth
            await client.Channels.Get("test").StatusAsync();

            client.Auth.ClientId.Should().Be("client-1");

            // Second auth with explicit authorize
            await client.Auth.AuthorizeAsync();

            client.Auth.ClientId.Should().Be("client-2");
        }

        // UTS: rest/unit/RSA12/wildcard-clientid-0
        [Fact]
        [Trait("spec", "RSA12")]
        public void RSA12_WildcardClientid()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                TokenDetails = new TokenDetails("wildcard-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),

                    // Wildcard
                    ClientId = "*",
                },
            });

            // Assertions
            // Wildcard clientId should be preserved
            client.Auth.ClientId.Should().Be("*");

            // The wildcard `*` clientId allows the token to be used with any client identity. This is a special case where `clientId` on individual operations can vary.
        }

        // NOTE: the spec gives the setup and steps for case 2 only; the other rows run the same setup and expect the same
        // status() call to succeed (the table's Expected column); case 2 expects the error.
        // UTS: rest/unit/RSA7/clientid-mismatch-error-1
        // DEVIATION: see deviations.md
        [DeviationTheory("RSA15a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [InlineData("client-a", "client-a", false)]
        [InlineData("client-a", "client-b", true)]
        [InlineData("client-a", null, false)]
        [InlineData("client-a", "*", false)]
        [InlineData(null, "client-b", false)]
        [Trait("spec", "RSA7")]
        public async Task RSA7_ClientidMismatchError(string optionsClientId, string tokenClientId, bool expectError)
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                ClientId = optionsClientId,
                TokenDetails = new TokenDetails("mismatched-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),

                    // Different from ClientOptions
                    ClientId = tokenClientId,
                },
            });

            // Test Steps
            // Or any operation requiring auth
            Func<Task> act = () => client.Channels.Get("test").StatusAsync();
            if (expectError)
            {
                var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
                (error.Message.Contains("clientId") || error.Message.Contains("mismatch")).Should().BeTrue("error.message CONTAINS \"clientId\" OR error.message CONTAINS \"mismatch\"");
            }
            else
            {
                await act.Should().NotThrowAsync();
            }

            // The exact timing of mismatch detection (constructor vs first use) may vary by implementation. The key requirement is that the mismatch is detected and reported as an error.
        }

        // UTS: rest/unit/RSA15a/token-clientid-must-match-0
        [UtsFact(Skip = "UTS spec error RSA15a: the test expects Rest(...) to throw code 40102 at construction; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA15a")]
        public Task RSA15a_TokenClientidMustMatch()
        {
            // SPEC ERROR RSA15a: RSA15a only says a ClientOptions clientId must match a non-wildcard TokenDetails clientId; features.md names no error code (40102) and no construction-time throw. See the RSA7 entry in UTS Spec Errors (the same timing problem).
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA15a — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA15b/wildcard-token-permits-any-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA15b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA15b")]
        public void RSA15b_WildcardTokenPermitsAny()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test",
                        ["status"] = new JObject { ["isActive"] = true },
                    });
                });
            InstallMock(mockHttp);

            // Wildcard token with explicit clientId — should succeed
            var client = Rest(new ClientOptions
            {
                ClientId = "any-client",
                TokenDetails = new TokenDetails("wildcard-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                    ClientId = "*",
                },
            });

            // Assertions
            // No error thrown — wildcard allows any clientId
            client.Auth.ClientId.Should().Be("any-client");
        }

        // UTS: rest/unit/RSA15c/incompatible-clientid-error-0
        [UtsFact(Skip = "UTS spec error RSA15c: the test has no steps of its own and refers to the RSA15a construction-time throw; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA15c")]
        public Task RSA15c_IncompatibleClientidError()
        {
            // SPEC ERROR RSA15c: RSA15c says a REST client should, following an auth request that uses an incompatible clientId, "result in an appropriate error response"; the UTS test has no setup or assertions and points at the RSA15a construction-time throw with code 40102. See the RSA7 entry in UTS Spec Errors.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA15c — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }
    }
}
