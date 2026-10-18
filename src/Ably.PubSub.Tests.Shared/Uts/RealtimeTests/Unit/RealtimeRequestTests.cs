// RealtimeClient Request Tests (RTC9)
// Derived from uts/realtime/unit/client/realtime_request.md
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
    public class RealtimeRequestTests : UtsUnitTestBase
    {
        public RealtimeRequestTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC9/request-proxies-rest-0
        [UtsFact(Skip = "Mock infrastructure limitation: the shared REST cases helper RequestCases.cs for uts/rest/unit/request.md does not exist yet; see deviations.md")]
        [Trait("spec", "RTC9")]
        public Task RTC9_RequestProxiesRest()
        {
            // NOTE: this spec delegates to uts/rest/unit/request.md: the tests there are run against a realtime client through
            // the shared helper beside the REST suite (realtime-mapping.md section 12), which is not translated yet.
            // Realtime operation: client.HttpClient.Request(...)
            // Spec: RealtimeClient#request is a direct proxy to RestClient#request. The tests in uts/rest/unit/request.md (covering RSC19) should be used to test a RealtimeClient instance in place of a RestClient instance.
            return Task.CompletedTask;
        }
    }
}
