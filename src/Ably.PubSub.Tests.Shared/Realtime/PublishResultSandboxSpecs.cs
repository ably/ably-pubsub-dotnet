using System.Threading.Tasks;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    [Collection("Channel SandBox")]
    [Trait("type", "integration")]
    public class PublishResultSandboxSpecs : SandboxSpecs
    {
        public PublishResultSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact]
        [Trait("requires", "protocol-v5")]
        [Trait("spec", "RSL1n")]
        public async Task RestPublish_ShouldReturnTheSerialsOfThePublishedMessages()
        {
            var rest = await GetRestClient(Protocol.Json);
            var channel = rest.Channels.Get("persisted:publish_result".AddRandomSuffix());

            var single = await channel.PublishAsync("greeting", "hello");
            single.Serials.Should().HaveCount(1);
            single.Serials[0].Should().NotBeNullOrEmpty();

            var batch = await channel.PublishAsync(new[] { new Message("a", "1"), new Message("b", "2"), new Message("c", "3") });
            batch.Serials.Should().HaveCount(3);
            batch.Serials.Should().OnlyContain(x => !string.IsNullOrEmpty(x));

            channel.Publish("sync", "data").Serials.Should().HaveCount(1);
        }

        [Fact]
        [Trait("requires", "protocol-v5")]
        [Trait("spec", "RTL6j")]
        public async Task RealtimePublish_ShouldReturnTheSerialsOfThePublishedMessages()
        {
            var client = await GetRealtimeClient(Protocol.Json);
            var channel = client.Channels.Get("persisted:publish_result".AddRandomSuffix());
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            var single = await channel.PublishAsync("greeting", "hello");
            single.IsSuccess.Should().BeTrue();
            single.Value.Serials.Should().HaveCount(1);
            single.Value.Serials[0].Should().NotBeNullOrEmpty();

            var batch = await channel.PublishAsync(new[] { new Message("a", "1"), new Message("b", "2"), new Message("c", "3") });
            batch.IsSuccess.Should().BeTrue();
            batch.Value.Serials.Should().HaveCount(3);
        }
    }
}
