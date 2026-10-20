// RealtimeClient Time Tests (RTC6, RTC6a)
// Derived from uts/realtime/unit/client/realtime_time.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec: the uts/rest/unit/time.md tests run through an inherited client-agnostic base (TimeTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimeTimeTests : TimeTestsBase<PubSubRealtimeClient>
    {
        public RealtimeTimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0
        // NOTE: the spec runs the uts/rest/unit/time.md tests against a realtime client in place of a REST client. They are inherited
        // unchanged from TimeTestsBase (RestTests/Unit/TimeTests.cs) and run here through the overrides below; the realtime
        // client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        protected override PubSubRealtimeClient NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options);
        }

        protected override Task<DateTimeOffset> TimeAsync(PubSubRealtimeClient client) => client.TimeAsync();
    }
}
