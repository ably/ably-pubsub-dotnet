// RealtimeChannel GetMessage Tests (RTL28)
// Derived from uts/realtime/unit/channels/channel_get_message.md
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

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelGetMessageTests : UtsUnitTestBase
    {
        public ChannelGetMessageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL28/identical-to-rest-0
        [UtsFact(Skip = "Mock infrastructure limitation: the shared REST cases helper for uts/rest/unit/channel/get_message.md (GetMessageCases) does not exist yet; see deviations.md")]
        [Trait("spec", "RTL28")]
        public async Task RTL28_IdenticalToRest()
        {
            // NOTE: delegating spec: the tests in uts/rest/unit/channel/get_message.md (RSL11) are to be run against a
            // RealtimeChannel (channel.GetMessageAsync) through the shared helper GetMessageCases beside the REST suite;
            // that REST spec is not translated in this run, so the helper does not exist.
            // RealtimeChannel#getMessage function: same as RestChannel#getMessage.
            await Task.CompletedTask;
        }
    }
}
