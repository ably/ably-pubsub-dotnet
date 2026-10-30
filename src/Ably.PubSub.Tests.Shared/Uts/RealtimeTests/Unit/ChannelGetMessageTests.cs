// RealtimeChannel GetMessage Tests (RTL28)
// Derived from uts/realtime/unit/channels/channel_get_message.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec: the uts/rest/unit/channel/get_message.md tests run through an inherited client-agnostic base (GetMessageTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelGetMessageTests : GetMessageTestsBase<PubSubRealtimeClient, IRealtimeChannel>
    {
        public ChannelGetMessageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL28/identical-to-rest-0
        // NOTE: the spec runs the uts/rest/unit/channel/get_message.md tests against a realtime client in place of a REST client. They are inherited
        // unchanged from GetMessageTestsBase (RestTests/Unit/GetMessageTests.cs) and run here through the overrides below; the realtime
        // client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        protected override PubSubRealtimeClient NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options);
        }

        protected override IRealtimeChannel GetChannel(PubSubRealtimeClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<Message> GetMessageAsync(IRealtimeChannel channel, string serial) => channel.GetMessageAsync(serial);
    }
}
