using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Rest
{
    [Collection("AblyRest SandBox Collection")]
    [Trait("type", "integration")]
    public class PresenceSandboxSpecs : SandboxSpecs
    {
        public PresenceSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Theory]
        [ProtocolData]
        public async Task GetsChannelPresence(Protocol protocol)
        {
            var client = await GetRestClient(protocol);

            var channel = client.Channels.Get(TestEnvironmentSettings.PresenceChannelName);
            channel.Should().NotBeNull();

            var presence = await channel.Presence.GetAsync();
            presence.Items.Should().HaveCount(6);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSP5")]
        public async Task WithCorrectCipherParams_DecryptsMessagesCorrectly(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var settings = await AblySandboxFixture.GetSettings();
            var channel = client.Channels.Get(TestEnvironmentSettings.PresenceChannelName, new ChannelOptions(settings.CipherParams));

            var presence = await channel.Presence.GetAsync();
            foreach (var message in presence.Items)
            {
                message.Encoding.Should().BeNullOrEmpty();
            }
        }
    }
}
