// Token Renewal Tests (RSA4a2, RSA4b, RSA4b1, RSC10, RSC10b)
// Derived from uts/rest/unit/auth/token_renewal.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Http;
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
    public class TokenRenewalTests : UtsUnitTestBase
    {
        public TokenRenewalTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA4b/renewal-on-40142-0
        [UtsFact]
        [Trait("spec", "RSA4b")]
        public async Task RSA4b_RenewalOn40142()
        {
            // Setup
            var callbackCount = 0;
            var tokens = new[] { "first-token", "second-token" };
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var token = tokens[Interlocked.Increment(ref callbackCount) - 1];
                return Task.FromResult<object>(new TokenDetails(token)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First request fails with token expired
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
                        // Second request (after renewal) succeeds
                        req.RespondWith(200, new JArray { new JObject { ["channel"] = "test" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            var result = await client.Channels.Get("test").HistoryAsync();

            // Assertions
            // authCallback was called twice (initial + renewal)
            Volatile.Read(ref callbackCount).Should().Be(2);

            // Two HTTP requests were made
            Volatile.Read(ref requestCount).Should().Be(2);

            // First request used first token
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            capturedRequests[0].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("first-token")));

            // Second request used renewed token
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            capturedRequests[1].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("second-token")));

            // Final result is successful
            // ASSERT result.items IS List: guaranteed at compile time (PaginatedResult<Message>.Items is List<Message>)
            result.Items.Should().NotBeNull();
        }

        // UTS: rest/unit/RSA4b/renewal-on-40140-1
        [UtsFact]
        [Trait("spec", "RSA4b")]
        public async Task RSA4b_RenewalOn40140()
        {
            // Setup
            var callbackCount = 0;
            var requestCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref callbackCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First attempt fails with 40140
                        req.RespondWith(401, new JObject
                        {
                            ["error"] = new JObject
                            {
                                ["code"] = 40140,
                                ["statusCode"] = 401,
                                ["message"] = "Token error",
                            },
                        });
                    }
                    else
                    {
                        // Retry succeeds
                        req.RespondWith(200, new JArray());
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            await client.Channels.Get("test").HistoryAsync();

            // Assertions
            Volatile.Read(ref callbackCount).Should().Be(2);
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSA4b1/preemptive-renewal-0
        [UtsFact(Skip = "UTS spec error RSA4b1: the test expects a local expiry check without any server time; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA4b1")]
        public Task RSA4b1_PreemptiveRenewal()
        {
            // SPEC ERROR RSA4b1: RSA4b1 makes pre-emptive renewal optional and only when the library has queried the Ably server time (RSA10k) and the expiry has passed on server time; this test advances/uses only the local clock.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA4b1 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA4a2/no-renewal-without-callback-0
        [UtsFact]
        [Trait("spec", "RSA4a2")]
        public async Task RSA4a2_NoRenewalWithoutCallback()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    Interlocked.Increment(ref requestCount);
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

            // Client with explicit token but no authCallback
            var client = Rest(new ClientOptions { Token = "static-token" });

            // Test Steps
            Func<Task> act = () => client.Channels.Get("test").HistoryAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40171);

            // Assertions
            // Only one request was made (no retry)
            Volatile.Read(ref requestCount).Should().Be(1);
        }

        // UTS: rest/unit/RSA4b/renewal-via-authurl-2
        // DEVIATION: see deviations.md
        [DeviationFact("RSA8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4b")]
        public async Task RSA4b_RenewalViaAuthurl()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    var requestNumber = Interlocked.Increment(ref requestCount);

                    if (req.Url.Host == "example.com")
                    {
                        // authUrl requests - return tokens
                        if (requestNumber == 1)
                        {
                            req.RespondWith(200, new JObject
                            {
                                ["token"] = "first-token",
                                ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                            });
                        }
                        else
                        {
                            // Second token request (renewal)
                            req.RespondWith(200, new JObject
                            {
                                ["token"] = "second-token",
                                ["expires"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3600000,
                            });
                        }
                    }
                    else
                    {
                        // API requests
                        if (requestNumber == 2)
                        {
                            // First API request fails
                            req.RespondWith(401, new JObject
                            {
                                ["error"] = new JObject { ["code"] = 40142, ["message"] = "Token expired" },
                            });
                        }
                        else
                        {
                            // Retry succeeds
                            req.RespondWith(200, new JArray());
                        }
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthUrl = new Uri("https://example.com/auth") });

            // Test Steps
            await client.Channels.Get("test").HistoryAsync();

            // Assertions
            // Two requests to authUrl
            var authRequests = capturedRequests.Snapshot().Where(r => r.Url.Host == "example.com").ToList();
            authRequests.Should().HaveCount(2);

            // Two requests to Ably API
            var apiRequests = capturedRequests.Snapshot().Where(r => r.Url.Host != "example.com").ToList();
            apiRequests.Should().HaveCount(2);

            // Second API request used renewed token
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            apiRequests[1].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("second-token")));
        }

        // UTS: rest/unit/RSA4b/renewal-limit-no-loop-3
        [UtsFact]
        [Trait("spec", "RSA4b")]
        public async Task RSA4b_RenewalLimitNoLoop()
        {
            // Setup
            var callbackCount = 0;
            var requestCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref callbackCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    Interlocked.Increment(ref requestCount);

                    // Always return token expired
                    req.RespondWith(401, new JObject
                    {
                        ["error"] = new JObject { ["code"] = 40142, ["message"] = "Token expired" },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            Func<Task> act = () => client.Channels.Get("test").HistoryAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Should eventually give up
            error.Code.Should().Be(40142);

            // Assertions
            // The library MUST retry at most once per original request (one renewal
            // attempt). After the renewed token is also rejected, the error is
            // propagated to the caller.
            // Initial token + one renewal
            Volatile.Read(ref callbackCount).Should().Be(2);

            // Original request + one retry
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSC10/request-retried-after-renewal-0
        [UtsFact]
        [Trait("spec", "RSC10")]
        public async Task RSC10_RequestRetriedAfterRenewal()
        {
            // Setup
            var callbackCount = 0;
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref callbackCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
                    if (req.Headers["Authorization"] == "Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("token-1")))
                    {
                        // First token is rejected
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
                        // Renewed token succeeds — return channel status
                        req.RespondWith(200, new JObject
                        {
                            ["channelId"] = "test",
                            ["status"] = new JObject
                            {
                                ["isActive"] = true,
                                ["occupancy"] = new JObject { ["metrics"] = new JObject { ["connections"] = 0 } },
                            },
                        });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            // Call channel.status() — the caller should not see the 401/renewal
            var result = await client.Channels.Get("test").StatusAsync();

            // Assertions
            // The call succeeded transparently
            result.Should().BeAssignableTo<ChannelDetails>();

            // Two HTTP requests were made to /channels/test (original + retry)
            var channelRequests = capturedRequests.Snapshot().Where(r => r.Path == "/channels/test").ToList();
            channelRequests.Should().HaveCount(2);

            // Auth callback was called twice (initial token + renewal)
            Volatile.Read(ref callbackCount).Should().Be(2);

            // First request used first token, second used renewed token
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            channelRequests[0].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("token-1")));
            // NOTE: RSA3b permits base64; the SDK encodes. The UTS spec asserts the plain token — see deviations.md (UTS Spec Errors, RSA3b)
            channelRequests[1].Headers["Authorization"].Should().Be("Bearer " + Convert.ToBase64String(Encoding.UTF8.GetBytes("token-2")));
        }

        // UTS: rest/unit/RSC10b/non-token-401-no-renewal-0
        [UtsFact]
        [Trait("spec", "RSC10b")]
        public async Task RSC10b_NonToken401NoRenewal()
        {
            // Setup
            var callbackCount = 0;
            var requestCount = 0;

            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                var count = Interlocked.Increment(ref callbackCount);
                return Task.FromResult<object>(new TokenDetails("token-" + count)
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    Interlocked.Increment(ref requestCount);

                    // Return a 401 with a non-token error code
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

            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            Func<Task> act = () => client.Channels.Get("test").StatusAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40100);

            // Assertions
            // Only one HTTP request — no retry
            Volatile.Read(ref requestCount).Should().Be(1);

            // Auth callback was called once (initial token only, no renewal)
            Volatile.Read(ref callbackCount).Should().Be(1);
        }

        // UTS-NA: rest/unit/RSA4b/renewal-msgpack-response-4 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
    }
}
