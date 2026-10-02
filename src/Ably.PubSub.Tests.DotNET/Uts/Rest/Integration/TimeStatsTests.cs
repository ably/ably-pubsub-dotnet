using System;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/time_stats.md in ably/specification.
    ///
    /// Spec points: RSC16, RSC6
    ///
    /// Three notes on the translation:
    ///
    /// 1. The spec guards its stats assertions on <c>result.items.length > 0</c>, which never holds
    ///    against a freshly provisioned sandbox app — it has no stats at all. The guarded test
    ///    therefore establishes the premise first by injecting an interval through the sandbox's own
    ///    <c>POST /stats</c> (<see cref="AblySandboxFixture.SetupStats"/>, which posts the embedded
    ///    <c>StatsFixture.json</c>) and polling until it is readable. The guard itself is left
    ///    exactly as the spec writes it.
    /// 2. <c>result.items[0].unit</c> is <c>Stats.IntervalGranularity</c> here, a
    ///    <see cref="StatsIntervalGranularity"/> whose four members are precisely the spec's
    ///    "minute, hour, day, month", so the spec's membership assertion cannot fail. It is kept
    ///    because it is the spec's assertion, but it carries no coverage — and in this SDK the
    ///    property never reflects the response at all, because it is bound to the wire name
    ///    <c>intervalGranularity</c> which the stats endpoint does not send. That gap is already
    ///    recorded against the unit-tier stats spec.
    /// 3. The spec's "IS DateTime" / "IS PaginatedResult" / "IS List" assertions are static
    ///    guarantees of the .NET signatures rather than runtime checks, so each is replaced at its
    ///    site by the strongest assertion that can actually fail.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class TimeStatsTests : UtsIntegrationTestBase
    {
        /// <summary>
        /// The spec's 5000ms allowance for network latency and minor clock differences between the
        /// machine running the test and the Ably server.
        /// </summary>
        private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMilliseconds(5000);

        /// <summary>
        /// Held separately because the base class keeps its own copy private, and
        /// <c>SetupStats()</c> is an instance method on the fixture.
        /// </summary>
        private readonly AblySandboxFixture _fixture;

        public TimeStatsTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
            _fixture = fixture;
        }

        // UTS: rest/integration/RSC16/time-returns-server-time-0
        [Fact]
        public async Task RSC16_TimeReturnsServerTime()
        {
            var client = await SandboxRestClient();

            var beforeRequest = DateTimeOffset.UtcNow;
            var serverTime = await client.TimeAsync();
            var afterRequest = DateTimeOffset.UtcNow;

            // "server_time IS DateTime" is static here: TimeAsync returns DateTimeOffset, which is
            // the idiomatic .NET rendering of the spec's DateTime, so a runtime type assertion
            // cannot fail. The window assertions below are what carry the coverage.
            serverTime.Should().BeOnOrAfter(
                beforeRequest - ClockSkewAllowance,
                "the server time must be no earlier than the instant the request was made, "
                + "allowing for network latency and minor clock differences");

            serverTime.Should().BeOnOrBefore(
                afterRequest + ClockSkewAllowance,
                "the server time must be no later than the instant the response arrived, "
                + "allowing for network latency and minor clock differences");
        }

        // UTS: rest/integration/RSC6/stats-returns-result-0
        [Fact]
        public async Task RSC6_StatsReturnsResult()
        {
            var client = await SandboxRestClient();

            // Premise, not a spec step. A fresh sandbox app has no stats, so the spec's guarded
            // assertions below would never run against one. Inject an interval through the
            // sandbox's own POST /stats and wait until it is readable — a wall-clock poll rather
            // than a fixed delay, because nothing is consistent immediately after a write.
            await _fixture.SetupStats();
            await UtsSandbox.WallClockPollUntil(
                async () => (await client.StatsAsync()).Items.Count > 0,
                "the interval injected through the sandbox's POST /stats to be readable by stats()",
                TimeSpan.FromSeconds(30));

            // Stats may be empty for a new sandbox app, but the call should succeed.
            var result = await client.StatsAsync();

            Output.WriteLine($"stats() returned {result.Items.Count} interval(s)");

            // "result IS PaginatedResult" and "result.items IS List" are static: StatsAsync returns
            // PaginatedResult<Stats> and Items is a List<Stats>. Asserting both are present keeps
            // the "should succeed" half of the spec's assertion meaningful.
            result.Should().NotBeNull();
            result.Items.Should().NotBeNull();

            if (result.Items.Count > 0)
            {
                // "intervalId IS String" — a typed string property, so presence is the assertable part.
                result.Items[0].IntervalId.Should().NotBeNullOrEmpty();

                var granularities = new[]
                {
                    StatsIntervalGranularity.Minute,
                    StatsIntervalGranularity.Hour,
                    StatsIntervalGranularity.Day,
                    StatsIntervalGranularity.Month,
                };

                granularities.Should().Contain(result.Items[0].IntervalGranularity);
            }
        }

        // UTS: rest/integration/RSC6/stats-with-parameters-1
        [Fact]
        public async Task RSC6_StatsWithParameters()
        {
            var client = await SandboxRestClient();

            var result = await client.StatsAsync(new StatsRequestParams
            {
                Limit = 5,
                Direction = QueryDirection.Forwards,
                Unit = StatsIntervalGranularity.Hour,
            });

            // NOTE: the spec asserts only that the page is no longer than the limit, which an empty
            // page satisfies, so what this really exercises is that the parameters are accepted and
            // applied rather than the limit itself. No interval is injected here, because unlike
            // stats-returns-result-0 this spec does not guard its assertion on there being stats to
            // read, and the fixture supplies fewer intervals than the limit anyway.
            result.Should().NotBeNull();
            result.Items.Should().NotBeNull();
            result.Items.Count.Should().BeLessOrEqualTo(5);
        }
    }
}
