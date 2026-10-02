using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Client
{
    /// <summary>
    /// Derived from uts/realtime/unit/client/realtime_stats.md in ably/specification.
    ///
    /// Spec points: RTC5, RTC5a, RTC5b
    ///
    /// The spec file carries a single Test ID — <c>realtime/unit/RTC5/stats-proxies-rest-0</c> — and no
    /// test body of its own. It delegates: "The tests in <c>uts/rest/unit/stats.md</c> (covering RSC6)
    /// should be used to test a <c>RealtimeClient</c> instance in place of a <c>RestClient</c>
    /// instance. All the same behaviour, parameters, and return types apply." So each of RSC6's sixteen
    /// scenarios is re-derived here against <c>PubSubRealtimeClient.StatsAsync()</c>, and every test
    /// names both its realtime Test ID and the REST test it was taken from.
    ///
    /// The method names carry RTC5a where the scenario is about the proxy's interface and return type,
    /// and RTC5b where it is about a parameter being accepted and forwarded — both contain "RTC5", so
    /// a <c>--filter "FullyQualifiedName~RTC5"</c> still finds the whole file.
    ///
    /// Every client is built on both seams — <c>RealtimeClient(mockWs, mockHttp)</c> — because
    /// <c>StatsAsync()</c> is a REST call made from a realtime client. The WebSocket mock is installed
    /// but never exercised: the harness defaults <c>AutoConnect</c> to false, so no connection attempt
    /// is made and <c>/stats</c> is the only traffic in each test.
    ///
    /// The three adaptations are inherited verbatim from the REST derivation
    /// (Uts/Rest/Unit/StatsTests.cs), because the realtime proxy forwards straight to the same REST
    /// client and so cannot differ:
    ///
    /// 1. The granularity query parameter. RSC6b4 calls it <c>unit</c>; this SDK sends <c>by</c>
    ///    (see <see cref="UnitParam"/>).
    /// 2. <c>Stats#unit</c>. The spec reads the granularity back off a returned Stats; the SDK's
    ///    <c>Stats.IntervalGranularity</c> is bound to the wire name <c>intervalGranularity</c>, which
    ///    the stats endpoint does not send, so the property never reflects the response.
    /// 3. "No parameters sends no query params". This SDK always sends its three defaults explicitly,
    ///    which RSC6b2, RSC6b3 and RSC6b4 in that same spec file expressly permit.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimeStatsTests : UtsTestBase
    {
        /// <summary>
        /// The query parameter this SDK sends the stats granularity in. RSC6b4 calls it <c>unit</c>;
        /// <c>StatsRequestParams.GetParameters()</c> has always spelled it <c>by</c>. Named once so the
        /// adaptation lives in one place.
        /// </summary>
        private const string UnitParam = "by";

        public RealtimeStatsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/returns-paginated-stats-0)
        [Fact]
        public async Task RTC5a_ReturnsPaginatedStats()
        {
            var statsData = new object[]
            {
                new
                {
                    intervalId = "2024-01-01:00:00",
                    unit = "hour",
                    all = new
                    {
                        messages = new { count = 100, data = 5000 },
                        all = new { count = 100, data = 5000 },
                    },
                },
                new
                {
                    intervalId = "2024-01-01:01:00",
                    unit = "hour",
                    all = new
                    {
                        messages = new { count = 150, data = 7500 },
                        all = new { count = 150, data = 7500 },
                    },
                },
            };

            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, statsData);

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var result = await client.StatsAsync();

            // "result IS PaginatedResult" is a static guarantee here: StatsAsync returns
            // PaginatedResult<Stats>, so a runtime type assertion cannot fail. The items carry the
            // coverage.
            result.Items.Should().HaveCount(2);
            result.Items[0].IntervalId.Should().Be("2024-01-01:00:00");
            result.Items[1].IntervalId.Should().Be("2024-01-01:01:00");

            // The spec asserts items[0].unit == "hour". The SDK exposes the granularity as
            // Stats.IntervalGranularity but binds it to the wire name "intervalGranularity", while the
            // stats endpoint sends "unit", so the response field is ignored and the property keeps its
            // enum default whatever the body says. Adapted to the SDK's observable, so the test fails
            // the day the binding is corrected.
            result.Items[0].IntervalGranularity.Should().Be(StatsIntervalGranularity.Minute);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Path.Should().Be("/stats");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/authenticated-with-headers-1)
        [Fact]
        public async Task RTC5a_AuthenticatedWithHeaders()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Headers.Should().ContainKey("Authorization");
            request.Headers.Should().ContainKey("X-Ably-Version");
            request.Headers.Should().ContainKey("Ably-Agent");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b1/start-param-millis-0)
        [Fact]
        public async Task RTC5b_StartParamMillis()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var startTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { Start = startTime });

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];
            request.Url.QueryParams["start"].Should().Be(startTime.ToUnixTimeInMilliseconds().ToString());
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b1/end-param-millis-1)
        [Fact]
        public async Task RTC5b_EndParamMillis()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var endTime = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { End = endTime });

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];
            request.Url.QueryParams["end"].Should().Be(endTime.ToUnixTimeInMilliseconds().ToString());
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b1/start-and-end-params-2)
        [Fact]
        public async Task RTC5b_StartAndEndParams()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var startTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var endTime = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { Start = startTime, End = endTime });

            capturedRequests.Should().HaveCount(1);
            var queryParams = capturedRequests[0].Url.QueryParams;
            queryParams["start"].Should().Be(startTime.ToUnixTimeInMilliseconds().ToString());
            queryParams["end"].Should().Be(endTime.ToUnixTimeInMilliseconds().ToString());
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b2/direction-param-forwards-0)
        [Fact]
        public async Task RTC5b_DirectionParamForwards()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            // The spec's direction: "forwards" is QueryDirection.Forwards here; the SDK lower-cases the
            // enum name onto the wire, which is the value the spec asserts.
            await client.StatsAsync(new StatsRequestParams { Direction = QueryDirection.Forwards });

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.QueryParams["direction"].Should().Be("forwards");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b2/direction-defaults-backwards-1)
        [Fact]
        public async Task RTC5b_DirectionDefaultsBackwards()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync();

            capturedRequests.Should().HaveCount(1);

            // The spec allows either branch — "direction" absent, or present as "backwards". This SDK
            // always sends the default explicitly, so the test pins that branch rather than accepting
            // both and asserting nothing.
            capturedRequests[0].Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b3/limit-param-value-0)
        [Fact]
        public async Task RTC5b_LimitParamValue()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync(new StatsRequestParams { Limit = 10 });

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("10");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b3/limit-defaults-to-100-1)
        [Fact]
        public async Task RTC5b_LimitDefaultsTo100()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync();

            capturedRequests.Should().HaveCount(1);

            // As with direction: the spec allows absent or "100", and this SDK always sends it.
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("100");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b4/unit-param-values-0)
        [Fact]
        public async Task RTC5b_UnitParamValues()
        {
            var cases = new[]
            {
                (Unit: StatsIntervalGranularity.Minute, Expected: "minute"),
                (Unit: StatsIntervalGranularity.Hour, Expected: "hour"),
                (Unit: StatsIntervalGranularity.Day, Expected: "day"),
                (Unit: StatsIntervalGranularity.Month, Expected: "month"),
            };

            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            foreach (var testCase in cases)
            {
                capturedRequests.Clear();

                await client.StatsAsync(new StatsRequestParams { Unit = testCase.Unit });

                capturedRequests.Should().HaveCount(1);

                // The spec asserts query_params["unit"]; this SDK spells the parameter "by". See
                // UnitParam — the value sent is the one the spec expects, only the key differs.
                capturedRequests[0].Url.QueryParams[UnitParam].Should().Be(testCase.Expected);
            }
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b4/unit-defaults-to-minute-1)
        [Fact]
        public async Task RTC5b_UnitDefaultsToMinute()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync();

            capturedRequests.Should().HaveCount(1);
            var queryParams = capturedRequests[0].Url.QueryParams;

            // "unit" is absent, which is the first branch of the spec's disjunction and so compliant.
            // The second assertion gives the test something to check: the SDK does send the default
            // granularity, under the name it uses for it.
            queryParams.Should().NotContainKey("unit");
            queryParams[UnitParam].Should().Be("minute");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6b/all-params-combined-0)
        [Fact]
        public async Task RTC5b_AllParamsCombined()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var startTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var endTime = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams
            {
                Start = startTime,
                End = endTime,
                Direction = QueryDirection.Forwards,
                Limit = 50,
                Unit = StatsIntervalGranularity.Hour,
            });

            capturedRequests.Should().HaveCount(1);
            var queryParams = capturedRequests[0].Url.QueryParams;
            queryParams["start"].Should().Be(startTime.ToUnixTimeInMilliseconds().ToString());
            queryParams["end"].Should().Be(endTime.ToUnixTimeInMilliseconds().ToString());
            queryParams["direction"].Should().Be("forwards");
            queryParams["limit"].Should().Be("50");

            // "unit" per the spec, "by" on this SDK's wire. See UnitParam.
            queryParams[UnitParam].Should().Be("hour");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/no-params-clean-request-2)
        [Fact]
        public async Task RTC5a_NoParamsCleanRequest()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, Array.Empty<object>());

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            await client.StatsAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/stats");

            // The spec asserts the query string is empty. This SDK always sends its three defaults, and
            // RSC6b2, RSC6b3 and RSC6b4 in that same spec file each expressly permit a client to send
            // the default value rather than omit it — so "query_params IS empty" contradicts its own
            // siblings. Adapted to pin exactly those three defaults, which still fails if a fourth
            // parameter appears or a value changes.
            var queryParams = request.Url.QueryParams;
            queryParams.Should().HaveCount(3);
            queryParams["direction"].Should().Be("backwards");
            queryParams["limit"].Should().Be("100");
            queryParams[UnitParam].Should().Be("minute");
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/pagination-link-headers-3)
        [Fact]
        public async Task RTC5a_PaginationLinkHeaders()
        {
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { intervalId = "2024-01-01:01:00", unit = "hour" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</stats?start=1704070800000&limit=1>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { intervalId = "2024-01-01:00:00", unit = "hour" } });
                    }
                });

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var page1 = await client.StatsAsync(new StatsRequestParams { Limit = 1 });
            var page2 = await page1.NextAsync();

            // The spec's hasNext() / isLast() are properties here.
            page1.Items.Should().HaveCount(1);
            page1.Items[0].IntervalId.Should().Be("2024-01-01:01:00");
            page1.HasNext.Should().BeTrue();
            page1.IsLast.Should().BeFalse();

            page2.Items.Should().HaveCount(1);
            page2.Items[0].IntervalId.Should().Be("2024-01-01:00:00");
            page2.HasNext.Should().BeFalse();
            page2.IsLast.Should().BeTrue();
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/empty-results-handled-4)
        [Fact]
        public async Task RTC5a_EmptyResultsHandled()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, Array.Empty<object>()));

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            var result = await client.StatsAsync();

            // "result IS PaginatedResult" is static here — see RTC5a_ReturnsPaginatedStats.
            result.Should().NotBeNull();
            result.Items.Should().BeEmpty();
            result.HasNext.Should().BeFalse();
            result.IsLast.Should().BeTrue();
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0 (rest/unit/RSC6a/error-propagated-5)
        [Fact]
        public async Task RTC5a_ErrorPropagated()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(401, new
                {
                    error = new
                    {
                        message = "Unauthorized",
                        code = 40100,
                        statusCode = 401,
                    },
                }));

            var client = RealtimeClient(new MockWebSocket(), mockHttp);

            Func<Task> act = () => client.StatsAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(401);
            error.Code.Should().Be(40100);
        }

        /// <summary>
        /// The spec's <c>MockHttpClient(onConnectionAttempt: ..., onRequest: ...)</c>, which almost every
        /// test in stats.md sets up identically: succeed the connection, record the request, answer it
        /// 200 with <paramref name="body"/>. The tests that need a stateful handler build their own.
        /// </summary>
        private static MockHttpClient CapturingMock(List<PendingHttpRequest> capturedRequests, object body)
        {
            return new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, body);
                });
        }
    }
}
