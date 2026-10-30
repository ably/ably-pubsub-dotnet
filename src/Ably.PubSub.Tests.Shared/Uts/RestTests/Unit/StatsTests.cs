// Stats API Tests (RSC6, RSC6a, RSC6b1, RSC6b2, RSC6b3, RSC6b4)
// Derived from uts/rest/unit/stats.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    /// <summary>
    /// The uts/rest/unit/stats.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="StatsTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: RealtimeStatsTests (realtime/unit/RTC5/stats-proxies-rest-0).
    /// </summary>
    public abstract class StatsTestsBase : UtsUnitTestBase
    {
        protected StatsTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the client under test, through the stats API both clients share.</summary>
        protected abstract IStatsCommands NewClient(ClientOptions options);

        // UTS: rest/unit/RSC6a/returns-paginated-stats-0
        // DEVIATION: see deviations.md
        [DeviationFact("TS12c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC6a")]
        public async Task RSC6a_ReturnsPaginatedStats()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var statsData = new JArray
            {
                new JObject
                {
                    ["intervalId"] = "2024-01-01:00:00",
                    ["unit"] = "hour",
                    ["all"] = new JObject
                    {
                        ["messages"] = new JObject { ["count"] = 100, ["data"] = 5000 },
                        ["all"] = new JObject { ["count"] = 100, ["data"] = 5000 },
                    },
                },
                new JObject
                {
                    ["intervalId"] = "2024-01-01:01:00",
                    ["unit"] = "hour",
                    ["all"] = new JObject
                    {
                        ["messages"] = new JObject { ["count"] = 150, ["data"] = 7500 },
                        ["all"] = new JObject { ["count"] = 150, ["data"] = 7500 },
                    },
                },
            };

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, statsData);
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var result = await client.StatsAsync();

            // Assertions
            // Result should be a PaginatedResult
            // ASSERT result IS PaginatedResult: guaranteed at compile time (StatsAsync returns PaginatedResult<Stats>)
            result.Items.Count.Should().Be(2);

            // Stats objects should have correct fields
            result.Items[0].IntervalId.Should().Be("2024-01-01:00:00");

            // NOTE: Stats has no unit; the SDK's equivalent is IntervalGranularity (rest-mapping.md section 9, TS12c)
            result.Items[0].IntervalGranularity.Should().Be(StatsIntervalGranularity.Hour);
            result.Items[1].IntervalId.Should().Be("2024-01-01:01:00");

            // Verify correct endpoint and method
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/stats");
        }

        // UTS: rest/unit/RSC6a/authenticated-with-headers-1
        [UtsFact]
        [Trait("spec", "RSC6a")]
        public async Task RSC6a_AuthenticatedWithHeaders()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync();

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];

            // Request must be authenticated
            request.Headers.Should().ContainKey("Authorization");

            // Standard Ably headers must be present
            request.Headers.Should().ContainKey("X-Ably-Version");
            request.Headers.Should().ContainKey("Ably-Agent");
        }

        // UTS: rest/unit/RSC6b1/start-param-millis-0
        [UtsFact]
        [Trait("spec", "RSC6b1")]
        public async Task RSC6b1_StartParamMillis()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var startTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { Start = startTime });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["start"].Should().Be(startTime.ToUnixTimeMilliseconds().ToString());
        }

        // UTS: rest/unit/RSC6b1/end-param-millis-1
        [UtsFact]
        [Trait("spec", "RSC6b1")]
        public async Task RSC6b1_EndParamMillis()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var endTime = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { End = endTime });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["end"].Should().Be(endTime.ToUnixTimeMilliseconds().ToString());
        }

        // UTS: rest/unit/RSC6b1/start-and-end-params-2
        [UtsFact]
        [Trait("spec", "RSC6b1")]
        public async Task RSC6b1_StartAndEndParams()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var startTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var endTime = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);
            await client.StatsAsync(new StatsRequestParams { Start = startTime, End = endTime });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["start"].Should().Be(startTime.ToUnixTimeMilliseconds().ToString());
            request.QueryParams["end"].Should().Be(endTime.ToUnixTimeMilliseconds().ToString());
        }

        // UTS: rest/unit/RSC6b2/direction-param-forwards-0
        [UtsFact]
        [Trait("spec", "RSC6b2")]
        public async Task RSC6b2_DirectionParamForwards()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync(new StatsRequestParams { Direction = QueryDirection.Forwards });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["direction"].Should().Be("forwards");
        }

        // UTS: rest/unit/RSC6b2/direction-defaults-backwards-1
        [UtsFact]
        [Trait("spec", "RSC6b2")]
        public async Task RSC6b2_DirectionDefaultsBackwards()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync();

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];

            // Direction should either be absent (server default) or "backwards"
            (!request.QueryParams.ContainsKey("direction") || request.QueryParams["direction"] == "backwards").Should().BeTrue();
        }

        // UTS: rest/unit/RSC6b3/limit-param-value-0
        [UtsFact]
        [Trait("spec", "RSC6b3")]
        public async Task RSC6b3_LimitParamValue()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync(new StatsRequestParams { Limit = 10 });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["limit"].Should().Be("10");
        }

        // UTS: rest/unit/RSC6b3/limit-defaults-to-100-1
        [UtsFact]
        [Trait("spec", "RSC6b3")]
        public async Task RSC6b3_LimitDefaultsTo100()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync();

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];

            // Limit should either be absent (server default) or "100"
            (!request.QueryParams.ContainsKey("limit") || request.QueryParams["limit"] == "100").Should().BeTrue();
        }

        // UTS: rest/unit/RSC6b4/unit-param-values-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC6b4", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC6b4")]
        public async Task RSC6b4_UnitParamValues()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Cases
            var testCases = new[]
            {
                new { Id = 1, Unit = StatsIntervalGranularity.Minute, Wire = "minute" },
                new { Id = 2, Unit = StatsIntervalGranularity.Hour, Wire = "hour" },
                new { Id = 3, Unit = StatsIntervalGranularity.Day, Wire = "day" },
                new { Id = 4, Unit = StatsIntervalGranularity.Month, Wire = "month" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();

                await client.StatsAsync(new StatsRequestParams { Unit = testCase.Unit });

                capturedRequests.Count.Should().Be(1, "test case {0}", testCase.Id);
                var request = capturedRequests[0];
                request.QueryParams["unit"].Should().Be(testCase.Wire);
            }
        }

        // UTS: rest/unit/RSC6b4/unit-defaults-to-minute-1
        [UtsFact]
        [Trait("spec", "RSC6b4")]
        public async Task RSC6b4_UnitDefaultsToMinute()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await client.StatsAsync();

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];

            // Unit should either be absent (server default) or "minute"
            (!request.QueryParams.ContainsKey("unit") || request.QueryParams["unit"] == "minute").Should().BeTrue();
        }

        // UTS: rest/unit/RSC6b/all-params-combined-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC6b4", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC6b")]
        public async Task RSC6b_AllParamsCombined()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
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

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.QueryParams["start"].Should().Be(startTime.ToUnixTimeMilliseconds().ToString());
            request.QueryParams["end"].Should().Be(endTime.ToUnixTimeMilliseconds().ToString());
            request.QueryParams["direction"].Should().Be("forwards");
            request.QueryParams["limit"].Should().Be("50");
            request.QueryParams["unit"].Should().Be("hour");
        }

        // UTS: rest/unit/RSC6a/no-params-clean-request-2
        [UtsFact(Skip = "UTS spec error RSC6a: the test asserts the request carries no query parameters, but the features IDL marks direction, limit and unit api-default (unnecessary for the client to send, not forbidden); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC6a")]
        public Task RSC6a_NoParamsCleanRequest()
        {
            // SPEC ERROR RSC6a: it asserts request.query_params IS empty for stats() with no arguments; sending the api-default values is permitted by the features IDL.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC6a — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC6a/pagination-link-headers-3
        [UtsFact]
        [Trait("spec", "RSC6a")]
        public async Task RSC6a_PaginationLinkHeaders()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            new JArray { new JObject { ["intervalId"] = "2024-01-01:01:00", ["unit"] = "hour" } },
                            new Dictionary<string, string> { ["Link"] = "</stats?start=1704070800000&limit=1>; rel=\"next\"" });
                    }
                    else
                    {
                        req.RespondWith(
                            200,
                            new JArray { new JObject { ["intervalId"] = "2024-01-01:00:00", ["unit"] = "hour" } });
                    }
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var page1 = await client.StatsAsync(new StatsRequestParams { Limit = 1 });
            var page2 = await page1.NextAsync();

            // Assertions
            // First page
            page1.Items.Count.Should().Be(1);
            page1.Items[0].IntervalId.Should().Be("2024-01-01:01:00");
            page1.HasNext.Should().BeTrue();
            page1.IsLast.Should().BeFalse();

            // Second page
            page2.Items.Count.Should().Be(1);
            page2.Items[0].IntervalId.Should().Be("2024-01-01:00:00");
            page2.HasNext.Should().BeFalse();
            page2.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/RSC6a/empty-results-handled-4
        [UtsFact]
        [Trait("spec", "RSC6a")]
        public async Task RSC6a_EmptyResultsHandled()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var result = await client.StatsAsync();

            // Assertions
            // ASSERT result IS PaginatedResult: guaranteed at compile time (StatsAsync returns PaginatedResult<Stats>)
            result.Items.Count.Should().Be(0);
            result.HasNext.Should().BeFalse();
            result.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/RSC6a/error-propagated-5
        [UtsFact]
        [Trait("spec", "RSC6a")]
        public async Task RSC6a_ErrorPropagated()
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
                            ["message"] = "Unauthorized",
                            ["code"] = 40100,
                            ["statusCode"] = 401,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            Func<Task> act = () => client.StatsAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(401);
            error.Code.Should().Be(40100);
        }
    }

    /// <summary>The uts/rest/unit/stats.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class StatsTests : StatsTestsBase
    {
        public StatsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override IStatsCommands NewClient(ClientOptions options) => Rest(options);
    }
}
