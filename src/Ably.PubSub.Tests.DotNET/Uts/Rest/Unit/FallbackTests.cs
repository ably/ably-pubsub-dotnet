using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/fallback.md in ably/specification.
    ///
    /// Spec points: RSC15a, RSC15f, RSC15j, RSC15l, RSC15l4, RSC15m, REC1a, REC1c1, REC1c2, REC1d,
    /// REC1d1, REC1d2, REC2a2, REC2c1, REC2c5, REC2c6, REC3, REC3a
    ///
    /// Three file-wide translation decisions, each made once here rather than restated at every site:
    ///
    /// 1. This SDK has no <c>endpoint</c> option and no <c>fallbackHostsUseDefault</c> option - it is
    /// still on the legacy <c>restHost</c> / <c>realtimeHost</c> / <c>environment</c> model. Every spec
    /// test whose setup needs one of those options is omitted rather than written, because in C# naming
    /// an absent option is a compile error, not a failing assertion. The spec's own notes under REC1a
    /// and REC2c1 sanction asserting the legacy domains, so the default primary domain here is
    /// <c>rest.ably.io</c> (<c>Defaults.RestHost</c>) and the default fallbacks are
    /// <c>[a-e].ably-realtime.com</c> rather than the REC1b <c>main.*</c> names.
    ///
    /// 2. <c>UtsClients.Options</c> clears <c>FallbackHosts</c> for the whole unit tier, so every test
    /// that is *about* the SDK's own fallback derivation sets <c>options.FallbackHosts = null</c> to put
    /// <c>ClientOptions.GetFallbackHosts()</c> back in charge.
    ///
    /// 3. Several setups stub <c>/time</c> as <c>{"time": N}</c>; the endpoint and uts/rest/unit/time.md
    /// both return <c>[N]</c>, and <c>TimeAsync()</c> indexes it, so the array form is used throughout.
    /// </summary>
    public class FallbackTests : UtsTestBase
    {
        private const string PrimaryHost = "rest.ably.io";
        private const long ServerTimeMs = 1234567890000;

        private static readonly string[] DefaultFallbackHosts =
        {
            "a.ably-realtime.com",
            "b.ably-realtime.com",
            "c.ably-realtime.com",
            "d.ably-realtime.com",
            "e.ably-realtime.com",
        };

        private static readonly string[] SandboxFallbackHosts =
        {
            "sandbox-a-fallback.ably-realtime.com",
            "sandbox-b-fallback.ably-realtime.com",
            "sandbox-c-fallback.ably-realtime.com",
            "sandbox-d-fallback.ably-realtime.com",
            "sandbox-e-fallback.ably-realtime.com",
        };

        public FallbackTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC15m/no-fallback-empty-hosts-0
        [Fact]
        public async Task RSC15m_NoFallbackEmptyHosts()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });

            var client = RestClient(mockHttp, configure: options =>
            {
                // The spec's explicit `fallbackHosts: []`, which is also the unit-tier default.
                options.FallbackHosts = Array.Empty<string>();
            });

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            mockHttp.CapturedRequests.Should().HaveCount(
                1,
                "with no fallback domains configured the 5xx must be surfaced rather than retried");
            ((int)error.StatusCode.Value).Should().Be(500);
        }

        // UTS: rest/unit/RSC15a/fallback-random-order-0
        [Fact]
        public async Task RSC15a_FallbackRandomOrder()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponses(6, 500, new { error = new { code = 50000 } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            Func<Task> act = () => client.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();

            var requests = mockHttp.CapturedRequests;

            // First request to primary.
            requests[0].Url.Host.Should().Be(PrimaryHost);

            // The spec queues six responses (primary plus five fallbacks). This SDK stops after
            // ClientOptions.HttpMaxRetryCount retries, which defaults to 3, so four attempts are made
            // and two of the queued responses go unused. The spec asserts on *which* hosts were used,
            // not on how many, so the count is deliberately not asserted.
            var fallbackHostsUsed = requests.Skip(1).Select(r => r.Url.Host).ToList();

            fallbackHostsUsed.Should().NotBeEmpty(
                "otherwise the 'all used hosts are valid fallbacks' assertion below checks nothing");

            foreach (var host in fallbackHostsUsed)
            {
                host.Should().BeOneOf(DefaultFallbackHosts);
            }

            // Randomness itself is not asserted: the spec notes it needs repeated runs or seed control,
            // and AblyHttpRequester.GetNextFallbackHost() uses an un-injectable System.Random.
        }

        // UTS: rest/unit/RSC15l/qualifying-errors-trigger-fallback-0
        [Theory]
        [InlineData(500)]
        [InlineData(501)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(504)]
        public async Task RSC15l_QualifyingErrorsTriggerFallback_HttpStatusCodes(int statusCode)
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(statusCode, new { error = new { code = statusCode * 100 } });
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);
            requests[1].Url.Host.Should().NotBe(requests[0].Url.Host);
        }

        // UTS: rest/unit/RSC15l/qualifying-errors-trigger-fallback-0
        [Theory]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(404)]
        public async Task RSC15l_QualifyingErrorsTriggerFallback_NonRetryableErrors(int statusCode)
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(statusCode, new { error = new { code = statusCode * 100 } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            Func<Task> act = () => client.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();

            mockHttp.CapturedRequests.Should().HaveCount(1, "a 4xx must not be retried on a fallback");
        }

        // UTS: rest/unit/RSC15l/qualifying-errors-trigger-fallback-0
        [Fact]
        public async Task RSC15l_QualifyingErrorsTriggerFallback_Timeout()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueTimeout();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
                options.HttpRequestTimeout = TimeSpan.FromMilliseconds(1000);
            });

            await client.TimeAsync();

            mockHttp.CapturedRequests.Should().HaveCount(2);
        }

        // UTS: rest/unit/RSC15l4/cloudfront-error-triggers-fallback-0
        //
        // Deviation: AblyHttpRequester.IsRetryableResponse() consults only
        // ErrorInfo.IsRetryableStatusCode(), which is 500-504. Nothing in the SDK reads the response's
        // Server header, so a CloudFront 403 is surfaced to the caller instead of being retried on a
        // fallback host, and this test's `await client.TimeAsync()` throws.
        [DeviationFact]
        public async Task RSC15l4_CloudFrontErrorTriggersFallback()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(
                403,
                new { error = "Forbidden" },
                new Dictionary<string, string> { { "Server", "CloudFront" } });
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);
            requests[0].Url.Host.Should().Be(PrimaryHost);
            requests[1].Url.Host.Should().NotBe(PrimaryHost);
        }

        // UTS: rest/unit/RSC15l/connection-refused-fallback-0
        [Fact]
        public async Task RSC15l_ConnectionRefusedFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        // First attempt (primary host) - connection refused.
                        conn.RespondWithRefused();
                    }
                    else
                    {
                        // Fallback succeeds.
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/dns-error-fallback-1
        [Fact]
        public async Task RSC15l_DnsErrorFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        // First attempt - DNS failure.
                        conn.RespondWithDnsError();
                    }
                    else
                    {
                        // Fallback succeeds.
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/connection-timeout-fallback-2
        [Fact]
        public async Task RSC15l_ConnectionTimeoutFallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        // First attempt - connection timeout.
                        conn.RespondWithTimeout();
                    }
                    else
                    {
                        // Fallback succeeds.
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
                options.HttpRequestTimeout = TimeSpan.FromMilliseconds(1000);
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/request-timeout-fallback-3
        [Fact]
        public async Task RSC15l_RequestTimeoutFallback()
        {
            var requestCount = 0;
            var capturedHosts = new List<string>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    capturedHosts.Add(conn.Host);
                    conn.RespondWithSuccess();
                },
                onRequest: req =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        // First request times out.
                        req.RespondWithTimeout();
                    }
                    else
                    {
                        // Fallback succeeds.
                        req.RespondWith(200, new object[] { ServerTimeMs });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
                options.HttpRequestTimeout = TimeSpan.FromMilliseconds(1000);
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            requestCount.Should().Be(2);

            // Should have tried different hosts.
            capturedHosts[0].Should().NotBe(capturedHosts[1]);
        }

        // UTS: rest/unit/RSC15l/http-5xx-triggers-fallback-4
        [Theory]
        [InlineData(500)]
        [InlineData(501)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(504)]
        public async Task RSC15l_Http5xxTriggersFallback(int statusCode)
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        req.RespondWith(statusCode, new { error = new { code = statusCode * 100 } });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { ServerTimeMs });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSC15l/http-4xx-no-fallback-5
        [Theory]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(404)]
        public async Task RSC15l_Http4xxNoFallback(int statusCode)
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount++;
                    req.RespondWith(statusCode, new { error = new { code = statusCode * 100 } });
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(statusCode);

            // Should NOT have retried.
            requestCount.Should().Be(1);
        }

        // UTS: rest/unit/RSC15j/host-header-matches-request-0
        [Fact]
        public async Task RSC15j_HostHeaderMatchesRequest()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);

            // Spec: request_N.headers["Host"] == request_N.url.host, and the two requests' Host headers
            // differ. Adapted, because the header is not observable at this seam: the SDK never sets one
            // (AblyHttpRequester.GetRequestMessage leaves HttpRequestMessage.Headers.Host null), and
            // .NET writes it in the socket handler *below* the HttpMessageHandler the mock is installed
            // as. The request URI's host is therefore exactly what the framework will put in the Host
            // header, so asserting on it is the equivalent observable.
            requests[0].Headers.Should().NotContainKey(
                "Host",
                "the framework derives it from the request URI below this seam");

            requests[0].Url.Host.Should().Be(PrimaryHost);
            requests[1].Url.Host.Should().BeOneOf(DefaultFallbackHosts);
            requests[1].Url.Host.Should().NotBe(requests[0].Url.Host);
        }

        // UTS: rest/unit/RSC15f/successful-fallback-cached-0
        [Fact]
        public async Task RSC15f_SuccessfulFallbackCached()
        {
            // NOTE: the spec queues responses per host and names the first default fallback. This SDK
            // picks the fallback at random (AblyHttpRequester.GetNextFallbackHost), so the handler
            // answers by role - the primary fails, anything else succeeds - and the assertions read
            // back whichever fallback the SDK chose.
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                if (req.Url.Host == PrimaryHost)
                {
                    req.RespondWith(500, new { error = new object() });
                }
                else
                {
                    req.RespondWith(200, new object[] { ServerTimeMs });
                }
            });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
                options.FallbackRetryTimeout = TimeSpan.FromMilliseconds(60000);
            });

            // First request - triggers fallback.
            await client.TimeAsync();

            // Second request - should use the cached fallback.
            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(3);

            // Request 1: primary (failed).
            requests[0].Url.Host.Should().Be(PrimaryHost);

            // Request 2: fallback (succeeded).
            requests[1].Url.Host.Should().BeOneOf(DefaultFallbackHosts);

            // Request 3: cached fallback (no retry to primary).
            requests[2].Url.Host.Should().Be(requests[1].Url.Host);
        }

        // UTS: rest/unit/RSC15f/cached-fallback-expires-1
        [Fact]
        public async Task RSC15f_CachedFallbackExpires()
        {
            // The spec's enable_fake_timers() + ADVANCE_TIME(150). This is time the SDK *measures*:
            // AblyHttpRequester stamps FallbackHostUsedFrom from ClientOptions.NowFunc and compares it
            // against NowFunc() in GetHost(), so TestClock reaches it without any real wait.
            var clock = new TestClock(TestClock.FixedStart);
            var primaryFailuresRemaining = 1;

            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                if (req.Url.Host == PrimaryHost && primaryFailuresRemaining > 0)
                {
                    primaryFailuresRemaining--;
                    req.RespondWith(500, new { error = new object() });
                }
                else
                {
                    req.RespondWith(200, new object[] { ServerTimeMs });
                }
            });

            var client = RestClient(mockHttp, clock, options =>
            {
                options.FallbackHosts = null;
                options.FallbackRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            // First request triggers fallback.
            await client.TimeAsync();

            // Advance fake time past fallbackRetryTimeout.
            clock.Advance(150);

            // Next request should try primary again.
            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(3);
            requests[2].Url.Host.Should().Be(
                PrimaryHost,
                "the preference should have expired once the clock passed FallbackRetryTimeout");
        }

        // UTS: rest/unit/RSC15f/expired-not-resurrected-2
        [Fact]
        public async Task RSC15f_ExpiredNotResurrected()
        {
            var clock = new TestClock(TestClock.FixedStart);
            PendingHttpRequest heldRequest = null;
            var requestIndex = 0;

            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                requestIndex++;
                if (requestIndex == 1)
                {
                    // First request to primary - fail to trigger fallback.
                    req.RespondWith(500, new
                    {
                        error = new
                        {
                            message = "fail",
                            code = 50000,
                            statusCode = 500,
                        },
                    });
                }
                else if (requestIndex == 2)
                {
                    // First fallback - succeed, caches this host.
                    req.RespondWith(200, new object[] { ServerTimeMs });
                }
                else if (requestIndex == 3)
                {
                    // Second request goes to the cached fallback - hold it, do not respond yet.
                    heldRequest = req;
                }
                else
                {
                    // All subsequent requests - succeed.
                    req.RespondWith(200, new object[] { ServerTimeMs });
                }
            });

            var client = RestClient(mockHttp, clock, options =>
            {
                options.FallbackHosts = null;
                options.FallbackRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            // Requests 1+2: primary fails, fallback succeeds, fallback cached.
            await client.TimeAsync();

            // Request 3: goes to the cached fallback, but the response is held. The SDK starts its
            // request eagerly, yet not synchronously, so the premise is polled rather than assumed.
            var requestFuture = client.TimeAsync();
            await UtsClients.PollUntil(
                () => heldRequest != null,
                "the third request to reach the mock");

            // Advance fake time past fallbackRetryTimeout so the cache expires.
            clock.Advance(150);

            // Request 4: cache expired, so the primary should be tried again.
            await client.TimeAsync();

            // Now let the held request (3) complete successfully.
            heldRequest.RespondWith(200, new object[] { ServerTimeMs });
            await requestFuture;

            // Request 5: the late success from request 3 must NOT have re-pinned the fallback.
            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(5);

            // Requests 1+2: primary fail, fallback success.
            requests[0].Url.Host.Should().Be(PrimaryHost);
            requests[1].Url.Host.Should().NotBe(PrimaryHost);

            var fallbackHost = requests[1].Url.Host;

            // Request 3: went to the cached fallback (held, not yet responded).
            requests[2].Url.Host.Should().Be(fallbackHost);

            // Request 4: after timeout expiry, the primary is tried again.
            requests[3].Url.Host.Should().Be(PrimaryHost);

            // Request 5: the late success from request 3 did not re-pin the fallback.
            requests[4].Url.Host.Should().Be(PrimaryHost);
        }

        // UTS: rest/unit/REC1a/default-primary-domain-0
        [Fact]
        public async Task REC1a_DefaultPrimaryDomain()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp);

            await client.TimeAsync();

            // Spec: main.realtime.ably.net under the REC1b endpoint routing policy. Adapted per the
            // spec's own note: this SDK is still on the legacy restHost pattern, so the default primary
            // domain is Defaults.RestHost.
            mockHttp.CapturedRequests[0].Url.Host.Should().Be(PrimaryHost);
        }

        // UTS: rest/unit/REC1c2/environment-sets-primary-domain-0
        [Fact]
        public async Task REC1c2_EnvironmentSetsPrimaryDomain()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Environment = "sandbox";
            });

            await client.TimeAsync();

            // Spec: sandbox.realtime.ably.net. Adapted to the legacy pattern, where
            // ClientOptions.FullRestHost() prefixes the environment onto Defaults.RestHost.
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("sandbox-rest.ably.io");
        }

        // UTS: rest/unit/REC1c1/environment-conflicts-resthost-0
        //
        // Deviation: REC1c1's conflict validation is not implemented. ClientOptions.FullRestHost()
        // silently prefers _restHost over Environment, so the client constructs and the request goes to
        // custom.host.com with no error raised.
        [DeviationFact]
        public void REC1c1_EnvironmentConflictsRestHost()
        {
            var mockHttp = new MockHttpClient();

            Action act = () => UtsClients.RestClient(mockHttp, configure: options =>
            {
                options.Environment = "sandbox";
                options.RestHost = "custom.host.com";
            });

            var error = act.Should().Throw<AblyException>().Which.ErrorInfo;
            IsConflictError(error).Should().BeTrue(
                "the spec allows error code 40000, or a message naming the invalid or conflicting option");
        }

        // UTS: rest/unit/REC1c1/environment-conflicts-realtimehost-1
        //
        // Deviation: as above. _realtimeHost does not participate in REST host selection at all, so
        // Environment and RealtimeHost cannot conflict in this SDK's model.
        [DeviationFact]
        public void REC1c1_EnvironmentConflictsRealtimeHost()
        {
            var mockHttp = new MockHttpClient();

            Action act = () => UtsClients.RestClient(mockHttp, configure: options =>
            {
                options.Environment = "sandbox";
                options.RealtimeHost = "custom.realtime.com";
            });

            var error = act.Should().Throw<AblyException>().Which.ErrorInfo;
            IsConflictError(error).Should().BeTrue(
                "the spec allows error code 40000, or a message naming the invalid or conflicting option");
        }

        // UTS: rest/unit/REC1d1/resthost-sets-primary-domain-0
        [Fact]
        public async Task REC1d1_RestHostSetsPrimaryDomain()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.RestHost = "custom.rest.example.com";
            });

            await client.TimeAsync();

            mockHttp.CapturedRequests[0].Url.Host.Should().Be("custom.rest.example.com");
        }

        // UTS: rest/unit/REC1d2/realtimehost-sets-primary-domain-0
        //
        // Deviation: ClientOptions.FullRestHost() never consults _realtimeHost. The fallback runs the
        // other way only - FullRealtimeHost() falls back to _restHost - so a REST client given only
        // realtimeHost still addresses rest.ably.io rather than the configured host.
        [DeviationFact]
        public async Task REC1d2_RealtimeHostSetsPrimaryDomain()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.RealtimeHost = "custom.realtime.example.com";
            });

            await client.TimeAsync();

            mockHttp.CapturedRequests[0].Url.Host.Should().Be("custom.realtime.example.com");
        }

        // UTS: rest/unit/REC1d/resthost-precedence-over-realtimehost-0
        [Fact]
        public async Task REC1d_RestHostPrecedenceOverRealtimeHost()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.RestHost = "rest.example.com";
                options.RealtimeHost = "realtime.example.com";
            });

            await client.TimeAsync();

            // REST client uses restHost, not realtimeHost.
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("rest.example.com");
        }

        // UTS: rest/unit/REC2c1/default-fallback-domains-0
        [Fact]
        public async Task REC2c1_DefaultFallbackDomains()
        {
            var mockHttp = new MockHttpClient();

            // Primary fails.
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });

            // Fallback succeeds.
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = null;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);
            requests[0].Url.Host.Should().Be(PrimaryHost);

            // Spec: main.[a-e].fallback.ably-realtime.com under REC1b. Adapted per the spec's own note:
            // legacy SDKs assert against [a-e].ably-realtime.com (Defaults.FallbackHosts).
            requests[1].Url.Host.Should().BeOneOf(DefaultFallbackHosts);
        }

        // UTS: rest/unit/REC2a2/custom-fallback-hosts-0
        [Fact]
        public async Task REC2a2_CustomFallbackHosts()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var customFallbacks = new[] { "fb1.example.com", "fb2.example.com", "fb3.example.com" };

            var client = RestClient(mockHttp, configure: options =>
            {
                options.FallbackHosts = customFallbacks;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);
            requests[0].Url.Host.Should().Be(PrimaryHost);
            requests[1].Url.Host.Should().BeOneOf(customFallbacks);
        }

        // UTS: rest/unit/REC2c5/production-environment-fallback-domains-0
        [Fact]
        public async Task REC2c5_ProductionEnvironmentFallbackDomains()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Environment = "sandbox";
                options.FallbackHosts = null;
            });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);

            // Spec: sandbox.realtime.ably.net plus sandbox.[a-e].fallback.ably-realtime.com. Adapted to
            // the legacy shape produced by Defaults.GetEnvironmentFallbackHosts().
            requests[0].Url.Host.Should().Be("sandbox-rest.ably.io");
            requests[1].Url.Host.Should().BeOneOf(SandboxFallbackHosts);
        }

        // UTS: rest/unit/REC2c6/custom-resthost-no-fallbacks-0
        [Fact]
        public async Task REC2c6_CustomRestHostNoFallbacks()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.RestHost = "custom.rest.example.com";

                // Left unset so ClientOptions.GetFallbackHosts() derives the answer from restHost, which
                // is the behaviour under test.
                options.FallbackHosts = null;
            });

            Func<Task> act = () => client.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();

            // No fallback attempted - only one request.
            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(1);
            requests[0].Url.Host.Should().Be("custom.rest.example.com");
        }

        // UTS: rest/unit/REC2c6/custom-realtimehost-no-fallbacks-1
        [Fact]
        public async Task REC2c6_CustomRealtimeHostNoFallbacks()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500, new { error = new { code = 50000 } });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.RealtimeHost = "custom.realtime.example.com";
                options.FallbackHosts = null;
            });

            Func<Task> act = () => client.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();

            // No fallback attempted - this is the half of the spec point this SDK satisfies: a set
            // realtimeHost suppresses the default fallback domains (ClientOptions.GetFallbackHosts).
            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(1);

            // Spec: the one request goes to custom.realtime.example.com. Adapted - a REST client ignores
            // realtimeHost entirely, so it addresses the default primary domain. The spec-correct
            // assertion for that half lives in REC1d2_RealtimeHostSetsPrimaryDomain.
            requests[0].Url.Host.Should().Be(PrimaryHost);
        }

        // UTS: rest/unit/REC3a/default-connectivity-check-url-0
        [Fact]
        public async Task REC3a_DefaultConnectivityCheckUrl()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponseForUrl("is-the-internet-up", 200, "yes");

            // Spec: Realtime(...) then client.connection.checkConnectivity(). This SDK exposes the check
            // as PubSubHttpClient.CanConnectToAbly(), which issues exactly the request the realtime
            // fallback path makes through it (Realtime/AttemptsHelpers.cs). SkipInternetCheck is on for
            // the whole unit tier and has to be turned off for the spec points about the check itself.
            var client = RestClient(mockHttp, configure: options =>
            {
                options.SkipInternetCheck = false;
            });

            var result = await client.CanConnectToAbly();

            result.Should().BeTrue();

            var connectivityRequests = mockHttp.CapturedRequests
                .Where(r => r.Url.Path.Contains("is-the-internet-up"))
                .ToList();

            connectivityRequests.Should().NotBeEmpty();
            connectivityRequests[0].Url.ToString().Should()
                .Be("https://internet-up.ably-realtime.com/is-the-internet-up.txt");
        }

        // UTS: rest/unit/REC3/connectivity-check-validation-0
        [Theory]
        [InlineData(200, "yes", true)]
        [InlineData(200, "no", false)]
        [InlineData(200, null, false)]
        [InlineData(404, "Not Found", false)]
        public async Task REC3_ConnectivityCheckValidation(int status, string body, bool expected)
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponseForUrl("is-the-internet-up", status, body);

            var client = RestClient(mockHttp, configure: options =>
            {
                options.SkipInternetCheck = false;
            });

            var result = await client.CanConnectToAbly();

            result.Should().Be(expected);
        }

        // UTS: rest/unit/REC3/connectivity-check-validation-0
        [Fact]
        public async Task REC3_ConnectivityCheckValidation_NetworkError()
        {
            var mockHttp = new MockHttpClient(onConnectionAttempt: conn => conn.RespondWithRefused());

            var client = RestClient(mockHttp, configure: options =>
            {
                options.SkipInternetCheck = false;
            });

            var result = await client.CanConnectToAbly();

            result.Should().BeFalse();
        }

        /// <summary>
        /// The spec's <c>error.code == 40000 OR error.message CONTAINS "invalid" OR "conflict"</c>,
        /// written once because both REC1c1 conflict-validation tests share it.
        /// </summary>
        private static bool IsConflictError(ErrorInfo error)
        {
            var message = error.Message ?? string.Empty;
            return error.Code == 40000
                   || message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
