using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Http
{
    public class PublishResultSpecs : MockHttpRestSpecs
    {
        public PublishResultSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private Ably.PubSub.Http.IHttpChannel ChannelReturning(string body)
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = body, StatusCode = HttpStatusCode.Created }.ToTask());
            return client.Channels.Get("test");
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        [Trait("spec", "PBR2")]
        public async Task PublishAsync_WithASingleMessage_ShouldReturnTheSerialFromTheResponseBody()
        {
            var result = await ChannelReturning(@"{""serials"":[""s1""]}").PublishAsync("name", "data");

            result.Serials.Should().Equal("s1");
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_WithAMessageObject_ShouldReturnTheSerialFromTheResponseBody()
        {
            var result = await ChannelReturning(@"{""serials"":[""s1""]}").PublishAsync(new Message("name", "data"));

            result.Serials.Should().Equal("s1");
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_WithABatch_ShouldReturnOneSerialPerMessage()
        {
            var result = await ChannelReturning(@"{""serials"":[""s1"",""s2"",""s3""]}")
                .PublishAsync(new[] { new Message("a", "1"), new Message("b", "2"), new Message("c", "3") });

            result.Serials.Should().Equal("s1", "s2", "s3");
        }

        [Fact]
        [Trait("spec", "PBR2a")]
        public async Task PublishAsync_WhenAMessageWasDiscarded_ShouldReturnANullSerialAtItsPosition()
        {
            var result = await ChannelReturning(@"{""serials"":[""s1"",null,""s3""]}")
                .PublishAsync(new[] { new Message("a", "1"), new Message("b", "2"), new Message("c", "3") });

            result.Serials.Should().Equal("s1", null, "s3");
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_ShouldIgnoreOtherFieldsOfTheResponseBody()
        {
            var result = await ChannelReturning(@"{""channel"":""test"",""messageId"":""x"",""serials"":[""s1""]}").PublishAsync("name", "data");

            result.Serials.Should().Equal("s1");
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("")]
        [InlineData(@"{""serials"":null}")]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_WithAnEmptyOrSerialLessBody_ShouldReturnANonNullResultWithNoSerials(string body)
        {
            var result = await ChannelReturning(body).PublishAsync("name", "data");

            result.Should().NotBeNull();
            result.Serials.Should().NotBeNull().And.BeEmpty();
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData(@"{""serials"":")]
        [InlineData(@"[""s1""]")]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_WithAMalformedBody_ShouldThrowAnAblyException(string body)
        {
            var channel = ChannelReturning(body);

            await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync("name", "data"));
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public void Publish_Sync_ShouldReturnTheSerialsFromTheResponseBody()
        {
            var channel = ChannelReturning(@"{""serials"":[""s1"",null]}");

            channel.Publish("name", "data").Serials.Should().Equal("s1", null);
            channel.Publish(new Message("name", "data")).Serials.Should().Equal("s1", null);
            channel.Publish(new[] { new Message("a", "1"), new Message("b", "2") }).Serials.Should().Equal("s1", null);
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public async Task PublishAsync_ShouldStillPostTheMessagesToTheMessagesEndpoint()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = @"{""serials"":[""s1""]}" }.ToTask());

            await client.Channels.Get("test").PublishAsync("name", "data");

            LastRequest.Method.Should().Be(System.Net.Http.HttpMethod.Post);
            LastRequest.Url.Should().Be("/channels/test/messages");
        }
    }
}
