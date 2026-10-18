// RealtimeClient Time Tests (RTC6, RTC6a)
// Derived from uts/realtime/unit/client/realtime_time.md
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
    public class RealtimeTimeTests : UtsUnitTestBase
    {
        public RealtimeTimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0
        [UtsFact(Skip = "Mock infrastructure limitation: the shared REST cases helper TimeCases.cs for uts/rest/unit/time.md does not exist yet; see deviations.md")]
        [Trait("spec", "RTC6")]
        public Task RTC6_TimeProxiesRest()
        {
            // NOTE: this spec delegates to uts/rest/unit/time.md: the tests there are run against a realtime client through
            // the shared helper beside the REST suite (realtime-mapping.md section 12), which is not translated yet.
            // Realtime operation: client.TimeAsync()
            // Spec: RealtimeClient#time is a direct proxy to RestClient#time. The tests in uts/rest/unit/time.md (covering RSC16) should be used to test a RealtimeClient instance in place of a RestClient instance.
            return Task.CompletedTask;
        }
    }
}
