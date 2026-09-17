using System;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("SandBox Connection")]
    [Trait("type", "integration")]
    public class ExtraSandboxSpecs : SandboxSpecs
    {
        [Theory]
        [ProtocolData]
        public async Task Sending_custom_json_in_extras_Should_echo_it_back(Protocol protocol)
        {
            string testName = "extras-channel".AddRandomSuffix();
            var realtime = await GetRealtimeClient(protocol);
            var channel = realtime.Channels.Get(testName);

            var received = new Message();
            channel.Subscribe(message =>
            {
                received = message;
                Output.WriteLine(((RealtimeChannel)channel).LastSuccessfulMessageIds.ToString());
            });
            channel.Error += (sender, args) =>
                throw new Exception(args.Reason.Message);
            /* subscribe */
            await channel.AttachAsync();

            var result = await channel.PublishAsync(new Message { Name = "test", Data = "test", Extras = new MessageExtras(JToken.Parse(@"{""headers"":{ ""good"": ""walk"", ""nice"": ""exercise"" }}")) });

            result.IsSuccess.Should().BeTrue();

            await new ConditionalAwaiter(() => received.Extras != null);

            var extras = received.Extras;
            var json = extras.ToJson();
            ((string)json["headers"]["good"]).Should().Be("walk");
        }

        public ExtraSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }
    }
}
