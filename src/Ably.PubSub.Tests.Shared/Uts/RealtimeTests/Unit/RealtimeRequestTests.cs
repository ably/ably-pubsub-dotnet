// RealtimeClient Request Tests (RTC9)
// Derived from uts/realtime/unit/client/realtime_request.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec: the uts/rest/unit/request.md tests run through an inherited client-agnostic base (RequestTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimeRequestTests : RequestTestsBase
    {
        public RealtimeRequestTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC9/request-proxies-rest-0
        // NOTE: the spec runs the uts/rest/unit/request.md tests against a realtime client in place of a REST client. They are inherited
        // unchanged from RequestTestsBase (RestTests/Unit/RequestTests.cs) and run here through the overrides below; the realtime
        // client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        // NOTE: RTC9 asks for the RestClient#request wrapper "delivered in an idiomatic way"; this SDK delivers it as the
        // realtime client's own REST client, client.HttpClient, whose Request runs the inherited cases (realtime-mapping.md section 12).
        protected override PubSubHttpClient NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options).HttpClient;
        }
    }
}
