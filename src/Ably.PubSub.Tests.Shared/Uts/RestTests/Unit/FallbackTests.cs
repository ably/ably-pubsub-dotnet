// Host Fallback and Endpoint Configuration Tests (RSC15, RSC15a, RSC15f, RSC15j, RSC15l, RSC15m, REC1, REC1a, REC1b, REC1b1, REC1b2, REC1b3, REC1b4, REC1c, REC1c1, REC1c2, REC1d, REC1d1, REC1d2, REC2, REC2a, REC2a1, REC2a2, REC2b, REC2c, REC2c1, REC2c2, REC2c3, REC2c4, REC2c5, REC2c6, REC3, REC3a, REC3b)
// Derived from uts/rest/unit/fallback.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: REC3 drives a realtime client; client.connection.checkConnectivity() is client.HttpClient.CanConnectToAbly() (rest-mapping.md section 6)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    public class FallbackTests : UtsUnitTestBase
    {
        public FallbackTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC15m/no-fallback-empty-hosts-0
        [UtsFact]
        [Trait("spec", "RSC15m")]
        public async Task RSC15m_NoFallbackEmptyHosts()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackHosts = new string[0], // Explicitly empty
            });

            // Test Steps
            Func<Task> act = () => client.TimeAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Should fail without retry
            mockHttp.CapturedRequests.Count.Should().Be(1);
            ((int?)error.StatusCode).Should().Be(500);
        }

        // UTS: rest/unit/RSC15a/fallback-random-order-0
        [UtsFact]
        [Trait("spec", "RSC15a")]
        public async Task RSC15a_FallbackRandomOrder()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // All requests fail to test full fallback sequence
            mockHttp.QueueResponses(
                6, // primary + 5 fallbacks
                500,
                new JObject { ["error"] = new JObject { ["code"] = 50000 } });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            Func<Task> act = () => client.TimeAsync();
            await act.Should().ThrowAsync<AblyException>();

            // Expected to fail after all retries

            // Assertions
            var requests = mockHttp.CapturedRequests;

            // First request to primary
            requests[0].Url.Host.Should().Be("main.realtime.ably.net");

            // Subsequent requests to fallback hosts
            var fallbackHostsUsed = requests.Skip(1).Select(r => r.Url.Host).ToList();

            var expectedFallbacks = new[]
            {
                "main.a.fallback.ably-realtime.com",
                "main.b.fallback.ably-realtime.com",
                "main.c.fallback.ably-realtime.com",
                "main.d.fallback.ably-realtime.com",
                "main.e.fallback.ably-realtime.com",
            };

            // All used hosts should be valid fallbacks
            fallbackHostsUsed.Should().NotContain(host => !expectedFallbacks.Contains(host));

            // To test randomness: run test multiple times and verify order varies
            // (Implementation note: may need statistical test or seed control)
        }

        // UTS: rest/unit/RSC15l/qualifying-errors-trigger-fallback-0
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_QualifyingErrorsTriggerFallback()
        {
            // Test Cases
            // | ID | Spec | Condition | Should Retry |
            // | 1 | RSC15l1 | Host unreachable | Yes |
            // | 2 | RSC15l2 | Request timeout | Yes |
            // | 3 | RSC15l3 | HTTP 500 | Yes |
            // | 4 | RSC15l3 | HTTP 501 | Yes |
            // | 5 | RSC15l3 | HTTP 502 | Yes |
            // | 6 | RSC15l3 | HTTP 503 | Yes |
            // | 7 | RSC15l3 | HTTP 504 | Yes |
            // | 8 | | HTTP 400 | No |
            // | 9 | | HTTP 401 | No |
            // | 10 | | HTTP 404 | No |

            // Setup (HTTP status codes)
            foreach (var testCase in new[] { 500, 501, 502, 503, 504 })
            {
                var mockHttp = new MockHttpClient();

                // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
                InstallMock(mockHttp);
                mockHttp.QueueResponse(testCase, new JObject { ["error"] = new JObject { ["code"] = testCase * 100 } });
                // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                mockHttp.QueueResponse(200, new[] { 1234567890000L });

                var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

                await client.TimeAsync();

                mockHttp.CapturedRequests.Count.Should().Be(2, "status {0}", testCase);
                mockHttp.CapturedRequests[1].Url.Host.Should().NotBe(mockHttp.CapturedRequests[0].Url.Host, "status {0}", testCase);
            }

            // Setup (Non-retryable errors)
            foreach (var testCase in new[] { 400, 401, 404 })
            {
                var mockHttp = new MockHttpClient();

                // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
                InstallMock(mockHttp);
                mockHttp.QueueResponse(testCase, new JObject { ["error"] = new JObject { ["code"] = testCase * 100 } });

                var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

                Func<Task> act = () => client.TimeAsync();
                await act.Should().ThrowAsync<AblyException>();

                // Expected to fail

                // Should NOT have retried
                mockHttp.CapturedRequests.Count.Should().Be(1, "status {0}", testCase);
            }

            // Setup (Timeout)
            var mockHttpTimeout = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpTimeout);
            mockHttpTimeout.QueueTimeout(); // Simulates timeout
            mockHttpTimeout.QueueResponse(200, new[] { 1234567890000L });

            var clientTimeout = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                HttpRequestTimeout = TimeSpan.FromMilliseconds(1000),
            });

            await clientTimeout.TimeAsync();

            mockHttpTimeout.CapturedRequests.Count.Should().Be(2);
        }

        // UTS: rest/unit/RSC15l4/cloudfront-error-triggers-fallback-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC15l4", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC15l4")]
        public async Task RSC15l4_CloudfrontErrorTriggersFallback()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                403,
                new JObject { ["error"] = "Forbidden" },
                new Dictionary<string, string> { ["Server"] = "CloudFront" });
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("main.realtime.ably.net");
            mockHttp.CapturedRequests[1].Url.Host.Should().NotBe("main.realtime.ably.net");
        }

        // UTS: rest/unit/RSC15l/connection-refused-fallback-0
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_ConnectionRefusedFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First attempt (primary host) - connection refused
                        conn.RespondWithRefused();
                    }
                    else
                    {
                        // Fallback succeeds
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            var result = await client.TimeAsync();

            // Should have succeeded on fallback
            // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
            result.Should().NotBe(default(DateTimeOffset));
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/dns-error-fallback-1
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_DnsErrorFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First attempt - DNS failure
                        conn.RespondWithDnsError();
                    }
                    else
                    {
                        // Fallback succeeds
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            var result = await client.TimeAsync();

            // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
            result.Should().NotBe(default(DateTimeOffset));
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/connection-timeout-fallback-2
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_ConnectionTimeoutFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First attempt - connection timeout
                        conn.RespondWithTimeout();
                    }
                    else
                    {
                        // Fallback succeeds
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                HttpRequestTimeout = TimeSpan.FromMilliseconds(1000),
            });

            var result = await client.TimeAsync();

            // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
            result.Should().NotBe(default(DateTimeOffset));
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/request-timeout-fallback-3
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_RequestTimeoutFallback()
        {
            var requestCount = 0;
            var capturedHosts = new CaptureList<string>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    capturedHosts.Add(conn.Host);
                    conn.RespondWithSuccess();
                },
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First request times out
                        req.RespondWithTimeout();
                    }
                    else
                    {
                        // Fallback succeeds
                        // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                        req.RespondWith(200, new[] { 1234567890000L });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                HttpRequestTimeout = TimeSpan.FromMilliseconds(1000),
            });

            var result = await client.TimeAsync();

            // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
            result.Should().NotBe(default(DateTimeOffset));
            Volatile.Read(ref requestCount).Should().Be(2);

            // Should have tried different hosts
            capturedHosts[0].Should().NotBe(capturedHosts[1]);
        }

        // UTS: rest/unit/RSC15l/http-5xx-triggers-fallback-4
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_Http5xxTriggersFallback()
        {
            foreach (var statusCode in new[] { 500, 501, 502, 503, 504 })
            {
                var requestCount = 0;

                var mockHttp = new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        if (Interlocked.Increment(ref requestCount) == 1)
                        {
                            req.RespondWith(statusCode, new JObject { ["error"] = new JObject { ["code"] = statusCode * 100 } });
                        }
                        else
                        {
                            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                            req.RespondWith(200, new[] { 1234567890000L });
                        }
                    });
                InstallMock(mockHttp);

                var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

                var result = await client.TimeAsync();

                // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
                result.Should().NotBe(default(DateTimeOffset), "status {0}", statusCode);
                Volatile.Read(ref requestCount).Should().Be(2, "status {0}", statusCode);
            }
        }

        // UTS: rest/unit/RSC15l/http-4xx-no-fallback-5
        [UtsFact]
        [Trait("spec", "RSC15l")]
        public async Task RSC15l_Http4xxNoFallback()
        {
            foreach (var statusCode in new[] { 400, 401, 404 })
            {
                var requestCount = 0;

                var mockHttp = new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        Interlocked.Increment(ref requestCount);
                        req.RespondWith(statusCode, new JObject { ["error"] = new JObject { ["code"] = statusCode * 100 } });
                    });
                InstallMock(mockHttp);

                var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

                Func<Task> act = () => client.TimeAsync();
                var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
                ((int?)error.StatusCode).Should().Be(statusCode);

                // Should NOT have retried
                Volatile.Read(ref requestCount).Should().Be(1, "status {0}", statusCode);
            }
        }

        // UTS: rest/unit/RSC15j/host-header-matches-request-0
        [UtsFact]
        [Trait("spec", "RSC15j")]
        public async Task RSC15j_HostHeaderMatchesRequest()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            var request1 = mockHttp.CapturedRequests[0];
            var request2 = mockHttp.CapturedRequests[1];

            // Host header should match the actual host being requested
            request1.Headers["Host"].Should().Be(request1.Url.Host);
            request2.Headers["Host"].Should().Be(request2.Url.Host);
            request1.Headers["Host"].Should().NotBe(request2.Headers["Host"]);
        }

        // UTS: rest/unit/RSC15f/successful-fallback-cached-0
        [UtsFact(Skip = "UTS spec error RSC15f: the tests stub the primary host and one named fallback host (main.a), but the SDK tries fallback hosts in random order (RSC15a); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC15f")]
        public Task RSC15f_SuccessfulFallbackCached()
        {
            // SPEC ERROR RSC15f: the mock queues responses for main.realtime.ably.net and main.a.fallback.ably-realtime.com only. RSC15a requires the fallback hosts to be tried in random order, so the first fallback request goes to any of main.[a-e].fallback and the unanswered one fails the test.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC15f — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC15f/cached-fallback-expires-1
        [UtsFact(Skip = "UTS spec error RSC15f: the tests stub the primary host and one named fallback host (main.a), but the SDK tries fallback hosts in random order (RSC15a); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC15f")]
        public Task RSC15f_CachedFallbackExpires()
        {
            // SPEC ERROR RSC15f: the mock queues responses for main.realtime.ably.net and main.a.fallback.ably-realtime.com only. RSC15a requires the fallback hosts to be tried in random order, so the first fallback request goes to any of main.[a-e].fallback and the unanswered one fails the test.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC15f — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC15f/expired-not-resurrected-2
        [UtsFact]
        [Trait("spec", "RSC15f")]
        public async Task RSC15f_ExpiredNotResurrected()
        {
            // Setup
            EnableFakeTimers();
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // Request handler: primary fails on first attempt, all others succeed.
            // Second request (to cached fallback) is NOT responded to immediately —
            // we hold the PendingRequest and respond later, after the timeout expires.
            PendingHttpRequest heldRequest = null;
            var requestIndex = 0;

            mockHttp.OnRequest = req =>
            {
                var index = Interlocked.Increment(ref requestIndex);
                if (index == 1)
                {
                    // First request to primary — fail to trigger fallback
                    req.RespondWith(500, new JObject
                    {
                        ["error"] = new JObject { ["message"] = "fail", ["code"] = 50000, ["statusCode"] = 500 },
                    });
                }
                else if (index == 2)
                {
                    // First fallback — succeed, caches this host
                    req.RespondWith(200, new[] { 1000L });
                }
                else if (index == 3)
                {
                    // Second request goes to cached fallback — hold it (don't respond yet)
                    Volatile.Write(ref heldRequest, req);
                }
                else
                {
                    // All subsequent requests — succeed
                    req.RespondWith(200, new[] { 1000L });
                }
            };

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackRetryTimeout = TimeSpan.FromMilliseconds(100), // 100ms for testing
            });

            // Test Steps
            // Request 1+2: primary fails → fallback succeeds → fallback cached
            await client.TimeAsync();

            // Request 3: goes to cached fallback, but we hold the response
            var requestFuture = client.TimeAsync(); // starts but does not complete

            // Advance fake time past fallbackRetryTimeout so the cache expires
            await AdvanceTime(150);

            // Request 4: cache expired → should try primary again
            await client.TimeAsync();

            // Now let the held request (3) complete successfully
            Volatile.Read(ref heldRequest).RespondWith(200, new[] { 1000L });
            await requestFuture;

            // Request 5: the late success from request 3 must NOT have re-pinned
            // the fallback — this request should go to primary again
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(5);

            // Requests 1+2: primary fail → fallback success
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("main.realtime.ably.net");
            mockHttp.CapturedRequests[1].Url.Host.Should().NotBe("main.realtime.ably.net");

            var fallbackHost = mockHttp.CapturedRequests[1].Url.Host;

            // Request 3: went to cached fallback (held, not yet responded)
            mockHttp.CapturedRequests[2].Url.Host.Should().Be(fallbackHost);

            // Request 4: after timeout expiry, primary is tried again
            mockHttp.CapturedRequests[3].Url.Host.Should().Be("main.realtime.ably.net");

            // Request 5: late success from request 3 did NOT re-pin fallback
            mockHttp.CapturedRequests[4].Url.Host.Should().Be("main.realtime.ably.net");
        }

        // UTS: rest/unit/REC1a/default-primary-domain-0
        [UtsFact]
        [Trait("spec", "REC1a")]
        public async Task REC1a_DefaultPrimaryDomain()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("main.realtime.ably.net");
        }

        // UTS: rest/unit/REC1b2/explicit-hostname-with-period-0
        [UtsFact]
        [Trait("spec", "REC1b2")]
        public async Task REC1b2_ExplicitHostnameWithPeriod()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "custom.ably.example.com",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("custom.ably.example.com");
        }

        // UTS: rest/unit/REC1b2/endpoint-localhost-1
        [UtsFact]
        [Trait("spec", "REC1b2")]
        public async Task REC1b2_EndpointLocalhost()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "localhost",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("localhost");
        }

        // UTS: rest/unit/REC1b2/endpoint-ipv6-address-2
        [UtsFact]
        [Trait("spec", "REC1b2")]
        public async Task REC1b2_EndpointIpv6Address()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "::1",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            // IPv6 addresses may be bracketed in URLs
            var host = mockHttp.CapturedRequests[0].Url.Host;
            (host == "::1" || host == "[::1]").Should().BeTrue("host was {0}", host);
        }

        // UTS: rest/unit/REC1b3/nonprod-routing-policy-0
        [UtsFact]
        [Trait("spec", "REC1b3")]
        public async Task REC1b3_NonprodRoutingPolicy()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "nonprod:staging",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("staging.realtime.ably-nonprod.net");
        }

        // UTS: rest/unit/REC1b4/production-routing-policy-0
        [UtsFact]
        [Trait("spec", "REC1b4")]
        public async Task REC1b4_ProductionRoutingPolicy()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "test",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("test.realtime.ably.net");
        }

        // UTS-NA: rest/unit/REC1b1/endpoint-conflicts-environment-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1b1/endpoint-conflicts-resthost-1 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1b1/endpoint-conflicts-realtimehost-2 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1b1/endpoint-conflicts-fallback-default-3 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1c2/environment-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1c1/environment-conflicts-resthost-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1c1/environment-conflicts-realtimehost-1 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1d1/resthost-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1d2/realtimehost-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC1d/resthost-precedence-over-realtimehost-0 — not applicable: removed option (Endpoint replaces it)

        // UTS: rest/unit/REC2c1/default-fallback-domains-0
        [UtsFact]
        [Trait("spec", "REC2c1")]
        public async Task REC2c1_DefaultFallbackDomains()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // Primary fails
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });

            // Fallback succeeds
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("main.realtime.ably.net");

            var expectedFallbacks = new[]
            {
                "main.a.fallback.ably-realtime.com",
                "main.b.fallback.ably-realtime.com",
                "main.c.fallback.ably-realtime.com",
                "main.d.fallback.ably-realtime.com",
                "main.e.fallback.ably-realtime.com",
            };
            mockHttp.CapturedRequests[1].Url.Host.Should().BeOneOf(expectedFallbacks);
        }

        // UTS: rest/unit/REC2a2/custom-fallback-hosts-0
        [UtsFact]
        [Trait("spec", "REC2a2")]
        public async Task REC2a2_CustomFallbackHosts()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackHosts = new[] { "fb1.example.com", "fb2.example.com", "fb3.example.com" },
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("main.realtime.ably.net");
            mockHttp.CapturedRequests[1].Url.Host.Should().BeOneOf("fb1.example.com", "fb2.example.com", "fb3.example.com");
        }

        // UTS-NA: rest/unit/REC2a1/fallback-hosts-conflicts-use-default-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC2b/fallback-hosts-use-default-0 — not applicable: removed option (Endpoint replaces it)

        // UTS: rest/unit/REC2c2/explicit-hostname-no-fallbacks-0
        [UtsFact]
        [Trait("spec", "REC2c2")]
        public async Task REC2c2_ExplicitHostnameNoFallbacks()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "custom.ably.example.com", // Contains period = explicit hostname
            });

            // Test Steps
            Func<Task> act = () => client.TimeAsync();
            await act.Should().ThrowAsync<AblyException>();

            // Expected to fail with no fallback

            // Assertions
            // No fallback attempted - only one request
            mockHttp.CapturedRequests.Count.Should().Be(1);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("custom.ably.example.com");
        }

        // UTS: rest/unit/REC2c3/nonprod-fallback-domains-0
        [UtsFact]
        [Trait("spec", "REC2c3")]
        public async Task REC2c3_NonprodFallbackDomains()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "nonprod:staging",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("staging.realtime.ably-nonprod.net");

            var expectedFallbacks = new[]
            {
                "staging.a.fallback.ably-realtime-nonprod.com",
                "staging.b.fallback.ably-realtime-nonprod.com",
                "staging.c.fallback.ably-realtime-nonprod.com",
                "staging.d.fallback.ably-realtime-nonprod.com",
                "staging.e.fallback.ably-realtime-nonprod.com",
            };
            mockHttp.CapturedRequests[1].Url.Host.Should().BeOneOf(expectedFallbacks);
        }

        // UTS: rest/unit/REC2c4/production-endpoint-fallback-domains-0
        [UtsFact]
        [Trait("spec", "REC2c4")]
        public async Task REC2c4_ProductionEndpointFallbackDomains()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "test",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("test.realtime.ably.net");

            var expectedFallbacks = new[]
            {
                "test.a.fallback.ably-realtime.com",
                "test.b.fallback.ably-realtime.com",
                "test.c.fallback.ably-realtime.com",
                "test.d.fallback.ably-realtime.com",
                "test.e.fallback.ably-realtime.com",
            };
            mockHttp.CapturedRequests[1].Url.Host.Should().BeOneOf(expectedFallbacks);
        }

        // UTS-NA: rest/unit/REC2c5/production-environment-fallback-domains-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC2c6/custom-resthost-no-fallbacks-0 — not applicable: removed option (Endpoint replaces it)

        // UTS-NA: rest/unit/REC2c6/custom-realtimehost-no-fallbacks-1 — not applicable: removed option (Endpoint replaces it)

        // UTS: rest/unit/REC3a/default-connectivity-check-url-0
        [UtsFact]
        [Trait("spec", "REC3a")]
        public async Task REC3a_DefaultConnectivityCheckUrl()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // Queue response for connectivity check
            mockHttp.QueueResponseForUrl(
                "https://internet-up.ably-realtime.com/is-the-internet-up.txt",
                200,
                "yes");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Trigger connectivity check (implementation-specific)
            // Some libraries expose this, others do it internally
            var result = await client.HttpClient.CanConnectToAbly();

            // OR: observe that connectivity check request was made during connection

            // Assertions
            var connectivityRequests = mockHttp.CapturedRequests.Where(r => r.Url.Path.Contains("is-the-internet-up")).ToList();
            connectivityRequests.Count.Should().BeGreaterThanOrEqualTo(1);
            connectivityRequests[0].Url.ToString().Should().Be("https://internet-up.ably-realtime.com/is-the-internet-up.txt");

            await CloseClient(client);
        }

        // UTS: rest/unit/REC3b/custom-connectivity-check-url-0
        [UtsFact(Skip = "Missing SDK public API: ClientOptions#connectivityCheckUrl (REC3b); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "REC3b")]
        public Task REC3b_CustomConnectivityCheckUrl()
        {
            // NOTE: missing SDK API: the connectivityCheckUrl option does not exist (the check always uses Defaults.InternetCheckUrl)
            // Setup
            // mock_http = MockHttpClient()
            // mock_http.queue_response_for_url(
            //   "https://custom.example.com/connectivity",
            //   200,
            //   "ok"
            // )
            //
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   connectivityCheckUrl: "https://custom.example.com/connectivity"
            // ))

            // Test Steps
            // result = AWAIT client.connection.checkConnectivity()

            // Assertions
            // connectivity_requests = mock_http.captured_requests.filter(
            //   r => r.url.host == "custom.example.com"
            // )
            // ASSERT connectivity_requests.length >= 1
            // ASSERT connectivity_requests[0].url.toString() == "https://custom.example.com/connectivity"
            //
            // # Should NOT request the default URL
            // default_requests = mock_http.captured_requests.filter(
            //   r => r.url.host == "internet-up.ably-realtime.com"
            // )
            // ASSERT default_requests.length == 0
            //
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: rest/unit/REC3/connectivity-check-validation-0
        [UtsFact]
        [Trait("spec", "REC3")]
        public async Task REC3_ConnectivityCheckValidation()
        {
            // Test Cases
            // | ID | Response | Expected Result |
            // | 1 | HTTP 200 with body "yes" | Connected |
            // | 2 | HTTP 200 with body "no" | Not connected |
            // | 3 | HTTP 200 with empty body | Not connected |
            // | 4 | HTTP 404 | Not connected |
            // | 5 | HTTP network error | Not connected |

            // Setup (Case 1 - Success)
            var mockHttpCase1 = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpCase1);
            mockHttpCase1.QueueResponseForUrl(
                "https://internet-up.ably-realtime.com/is-the-internet-up.txt",
                200,
                "yes");

            // NOTE: autoConnect: false (TO3e); the spec's auto-connecting client fails its connection attempt and its RTN17j
            // fallback connectivity check consumes the single queued check response first - see deviations.md (UTS Spec Errors, REC3)
            var clientCase1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var resultCase1 = await clientCase1.HttpClient.CanConnectToAbly();

            resultCase1.Should().BeTrue();

            await CloseClient(clientCase1);

            // Setup (Case 2 - Wrong body)
            var mockHttpCase2 = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpCase2);
            mockHttpCase2.QueueResponseForUrl(
                "https://internet-up.ably-realtime.com/is-the-internet-up.txt",
                200,
                "no");

            // NOTE: autoConnect: false (TO3e); the spec's auto-connecting client fails its connection attempt and its RTN17j
            // fallback connectivity check consumes the single queued check response first - see deviations.md (UTS Spec Errors, REC3)
            var clientCase2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var resultCase2 = await clientCase2.HttpClient.CanConnectToAbly();

            resultCase2.Should().BeFalse();

            await CloseClient(clientCase2);

            // Setup (Case 3 - Empty body)
            // NOTE: setup derived from the Test Cases table (no setup section in the spec)
            var mockHttpCase3 = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpCase3);
            mockHttpCase3.QueueResponseForUrl(
                "https://internet-up.ably-realtime.com/is-the-internet-up.txt",
                200,
                string.Empty);

            // NOTE: autoConnect: false (TO3e); the spec's auto-connecting client fails its connection attempt and its RTN17j
            // fallback connectivity check consumes the single queued check response first - see deviations.md (UTS Spec Errors, REC3)
            var clientCase3 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var resultCase3 = await clientCase3.HttpClient.CanConnectToAbly();

            resultCase3.Should().BeFalse();

            await CloseClient(clientCase3);

            // Setup (Case 4 - HTTP error)
            var mockHttpCase4 = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpCase4);
            mockHttpCase4.QueueResponseForUrl(
                "https://internet-up.ably-realtime.com/is-the-internet-up.txt",
                404,
                "Not Found");

            // NOTE: autoConnect: false (TO3e); the spec's auto-connecting client fails its connection attempt and its RTN17j
            // fallback connectivity check consumes the single queued check response first - see deviations.md (UTS Spec Errors, REC3)
            var clientCase4 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var resultCase4 = await clientCase4.HttpClient.CanConnectToAbly();

            resultCase4.Should().BeFalse();

            await CloseClient(clientCase4);

            // Setup (Case 5 - Network error)
            // NOTE: setup derived from the Test Cases table (no setup section in the spec)
            var mockHttpCase5 = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithRefused());

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttpCase5);

            // NOTE: autoConnect: false (TO3e); the spec's auto-connecting client fails its connection attempt and its RTN17j
            // fallback connectivity check consumes the single queued check response first - see deviations.md (UTS Spec Errors, REC3)
            var clientCase5 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var resultCase5 = await clientCase5.HttpClient.CanConnectToAbly();

            resultCase5.Should().BeFalse();

            await CloseClient(clientCase5);
        }
    }
}
