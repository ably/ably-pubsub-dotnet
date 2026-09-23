using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    [Trait("type", "integration")]
    public class StatsSandBoxSpecs : SandboxSpecs
    {
        private static readonly DateTimeOffset StartInterval = DateHelper.CreateDate(DateTimeOffset.UtcNow.Year - 1, 2, 3, 15, 5);

        private async Task<List<Stats>> GetStats(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var result = await client.StatsAsync(new StatsRequestParams { Start = StartInterval.AddMinutes(-2), End = StartInterval.AddMinutes(1) });

            return result.Items;
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "G3")]
        public async Task ShouldHaveCorrectStatsAsPerStatsSpec(Protocol protocol)
        {
            await Fixture.SetupStats();

            async Task GetAndValidateStats()
            {
                var allStats = await GetStats(protocol);
                var stats = allStats.First();
                stats.All.Messages.Count.Should().Be(40 + 70);
                stats.All.Messages.Data.Should().Be(4000 + 7000);
                stats.Inbound.Realtime.All.Count.Should().Be(70);
                stats.Inbound.Realtime.All.Data.Should().Be(7000);
                stats.Inbound.Realtime.Messages.Count.Should().Be(70);
                stats.Inbound.Realtime.Messages.Data.Should().Be(7000);
                stats.Outbound.Realtime.All.Count.Should().Be(40);
                stats.Outbound.Realtime.All.Data.Should().Be(4000);
                stats.Persisted.Presence.Count.Should().Be(20);
                stats.Persisted.Presence.Data.Should().Be(2000);
                stats.Connections.Tls.Peak.Should().Be(20);
                stats.Connections.Tls.Opened.Should().Be(10);
                stats.Channels.Peak.Should().Be(50);
                stats.Channels.Opened.Should().Be(30);
                stats.ApiRequests.Succeeded.Should().Be(50);
                stats.ApiRequests.Failed.Should().Be(10);
                stats.TokenRequests.Succeeded.Should().Be(60);
                stats.TokenRequests.Failed.Should().Be(20);
            }

            await AssertMultipleTimes(GetAndValidateStats, 5, TimeSpan.FromSeconds(5));
        }

        public StatsSandBoxSpecs(ITestOutputHelper output)
            : base(new AblySandboxFixture(), output)
        {
        }
    }
}
