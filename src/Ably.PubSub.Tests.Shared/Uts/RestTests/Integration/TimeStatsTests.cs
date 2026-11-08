// Time and Stats Integration Tests (RSC16, RSC6)
// Derived from uts/rest/integration/time_stats.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class TimeStatsTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public TimeStatsTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSC16/time-returns-server-time-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC16")]
        public async Task RSC16_TimeReturnsServerTime()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Test Steps
            var beforeRequest = DateTimeOffset.UtcNow;
            var serverTime = await client.TimeAsync();
            var afterRequest = DateTimeOffset.UtcNow;

            // Assertions
            // Server time should be a DateTime
            // ASSERT server_time IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)

            // Server time should be reasonably close to client time
            // (allowing for network latency and minor clock differences)
            serverTime.Should().BeOnOrAfter(beforeRequest - TimeSpan.FromMilliseconds(5000));
            serverTime.Should().BeOnOrBefore(afterRequest + TimeSpan.FromMilliseconds(5000));
        }

        // UTS: rest/integration/RSC6/stats-returns-result-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC6")]
        public async Task RSC6_StatsReturnsResult()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Test Steps
            // Stats may be empty for a new sandbox app, but the call should succeed
            var result = await client.StatsAsync();

            // Assertions
            // Result should be a PaginatedResult (may be empty)
            // ASSERT result IS PaginatedResult: guaranteed at compile time (StatsAsync returns PaginatedResult<Stats>)
            // ASSERT result.items IS List: guaranteed at compile time (Items is a List<Stats>)
            result.Items.Should().NotBeNull();

            // If there are items, they should have expected structure
            if (result.Items.Count > 0)
            {
                // ASSERT result.items[0].intervalId IS String: guaranteed at compile time (IntervalId is a string)
                result.Items[0].IntervalId.Should().NotBeNull();

                // NOTE: Stats has no unit; the SDK's equivalent is IntervalGranularity (rest-mapping.md section 9, TS12c)
                result.Items[0].IntervalGranularity.Should().BeOneOf(
                    StatsIntervalGranularity.Minute,
                    StatsIntervalGranularity.Hour,
                    StatsIntervalGranularity.Day,
                    StatsIntervalGranularity.Month);
            }
        }

        // UTS: rest/integration/RSC6/stats-with-parameters-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC6")]
        public async Task RSC6_StatsWithParameters()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Test Steps
            // Request stats with specific parameters
            var result = await client.StatsAsync(new StatsRequestParams
            {
                Limit = 5,
                Direction = QueryDirection.Forwards,
                Unit = StatsIntervalGranularity.Hour,
            });

            // Assertions
            // Should succeed with parameters applied
            // ASSERT result IS PaginatedResult: guaranteed at compile time (StatsAsync returns PaginatedResult<Stats>)
            result.Items.Count.Should().BeLessThanOrEqualTo(5);
        }
    }
}
