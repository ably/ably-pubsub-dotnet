// RealtimeChannel GetMessageVersions Tests (RTL31)
// Derived from uts/realtime/unit/channels/channel_message_versions.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec: the uts/rest/unit/channel/message_versions.md tests run through an inherited client-agnostic base (MessageVersionsTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelMessageVersionsTests : MessageVersionsTestsBase<PubSubRealtimeClient, IRealtimeChannel>
    {
        public ChannelMessageVersionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL31/identical-to-rest-0
        // NOTE: the spec runs the uts/rest/unit/channel/message_versions.md tests against a realtime client in place of a REST client. They are inherited
        // unchanged from MessageVersionsTestsBase (RestTests/Unit/MessageVersionsTests.cs) and run here through the overrides below; the realtime
        // client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        protected override PubSubRealtimeClient NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options);
        }

        protected override IRealtimeChannel GetChannel(PubSubRealtimeClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<PaginatedResult<Message>> GetMessageVersionsAsync(IRealtimeChannel channel, string serial, PaginatedRequestParams query = null) => channel.GetMessageVersionsAsync(serial, query);
    }
}
