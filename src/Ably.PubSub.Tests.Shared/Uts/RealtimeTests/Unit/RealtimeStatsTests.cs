// RealtimeClient Stats Tests (RTC5, RTC5a, RTC5b)
// Derived from uts/realtime/unit/client/realtime_stats.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using Ably.PubSub.Tests.Uts.Infra;
using System;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimeStatsTests : UtsUnitTestBase
    {
        public RealtimeStatsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0
        [UtsFact(Skip = "Mock infrastructure limitation: the shared REST cases helper StatsCases.cs for uts/rest/unit/stats.md does not exist yet; see deviations.md")]
        [Trait("spec", "RTC5")]
        public Task RTC5_StatsProxiesRest()
        {
            // NOTE: this spec delegates to uts/rest/unit/stats.md: the tests there are run against a realtime client through
            // the shared helper beside the REST suite (realtime-mapping.md section 12), which is not translated yet.
            // Realtime operation: client.StatsAsync(...)
            // Spec: RealtimeClient#stats is a direct proxy to RestClient#stats. The tests in uts/rest/unit/stats.md (covering RSC6) should be used to test a RealtimeClient instance in place of a RestClient instance.
            return Task.CompletedTask;
        }
    }
}
