// RealtimeClient Stats Tests (RTC5, RTC5a, RTC5b)
// Derived from uts/realtime/unit/client/realtime_stats.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec: the uts/rest/unit/stats.md tests run through an inherited client-agnostic base (StatsTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimeStatsTests : StatsTestsBase
    {
        public RealtimeStatsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC5/stats-proxies-rest-0
        // NOTE: the spec runs the uts/rest/unit/stats.md tests against a realtime client in place of a REST client. They are inherited
        // unchanged from StatsTestsBase (RestTests/Unit/StatsTests.cs) and run here through the overrides below; the realtime
        // client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        protected override IStatsCommands NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options);
        }
    }
}
