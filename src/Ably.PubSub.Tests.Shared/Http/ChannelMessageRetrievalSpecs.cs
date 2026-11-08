using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Http
{
    public class ChannelMessageRetrievalSpecs : MockHttpRestSpecs
    {
        private const string PlainSerial = "abc123";
        private const string SerialNeedingEncoding = "01826232498871-001@abcdefghij:001";

        private const string MessageJson =
            "{\"id\":\"m1\",\"serial\":\"" + PlainSerial + "\",\"timestamp\":1700000000000,\"name\":\"n\",\"data\":\"hello\",\"action\":0}";

        public ChannelMessageRetrievalSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        public static IEnumerable<object[]> Serials()
        {
            yield return new object[] { PlainSerial, "abc123" };
            yield return new object[] { SerialNeedingEncoding, "01826232498871-001%40abcdefghij%3A001" };
            yield return new object[] { "a/b c", "a%2Fb%20c" };
        }

        [Theory]
        [MemberData(nameof(Serials))]
        [Trait("spec", "RSL11b")]
        public async Task GetMessageAsync_ShouldGetTheMessageEndpointWithTheSerialUrlEncoded(string serial, string encodedSerial)
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = MessageJson }.ToTask());

            await client.Channels.Get("test").GetMessageAsync(serial);

            LastRequest.Method.Should().Be(HttpMethod.Get);
            LastRequest.Url.Should().Be($"/channels/test/messages/{encodedSerial}");
        }

        [Fact]
        [Trait("spec", "RSL11a1")]
        public async Task GetMessageAsync_WithAMessage_ShouldUseItsSerial()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = MessageJson }.ToTask());

            await client.Channels.Get("test").GetMessageAsync(new Message { Serial = PlainSerial });

            LastRequest.Url.Should().Be("/channels/test/messages/abc123");
        }

        [Fact]
        [Trait("spec", "RSL11c")]
        public async Task GetMessageAsync_ShouldReturnTheMessageWithTheTm2DefaultsApplied()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = MessageJson }.ToTask());

            var message = await client.Channels.Get("test").GetMessageAsync(PlainSerial);

            message.Serial.Should().Be(PlainSerial);
            message.Action.Should().Be(MessageAction.MessageCreate);
            message.Version.Should().NotBeNull();
            message.Version.Serial.Should().Be(PlainSerial);
            message.Version.Timestamp.Should().Be(message.Timestamp);
            message.Annotations.Should().NotBeNull();
            message.Annotations.Summary.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSL11c")]
        public async Task GetMessageAsync_ForAnEncryptedChannel_ShouldReturnTheDecodedMessage()
        {
            var options = new ChannelOptions(Crypto.GetDefaultParams());
            var encrypted = new Message("n", new { value = "secret" }) { Serial = PlainSerial };
            MessageHandler.EncodePayloads(options.ToDecodingContext(), new[] { encrypted }).IsSuccess.Should().BeTrue();
            encrypted.Encoding.Should().Contain("cipher+aes-256-cbc");

            var client = GetRestClient(_ => new AblyResponse { TextResponse = JsonHelper.Serialize(encrypted) }.ToTask());

            var message = await client.Channels.Get("test", options).GetMessageAsync(PlainSerial);

            message.Encoding.Should().BeNullOrEmpty();
            message.Data.Should().BeEquivalentTo(Newtonsoft.Json.Linq.JObject.Parse("{\"value\":\"secret\"}"));
        }

        [Fact]
        [Trait("spec", "RSL11a")]
        public async Task GetMessageAsync_WithoutASerial_ShouldThrowWithCode40003AndSendNoRequest()
        {
            var channel = GetRestClient().Channels.Get("test");

            foreach (var call in new System.Func<Task>[]
            {
                () => channel.GetMessageAsync((string)null),
                () => channel.GetMessageAsync(string.Empty),
                () => channel.GetMessageAsync(new Message()),
                () => channel.GetMessageAsync((Message)null),
            })
            {
                var exception = await Assert.ThrowsAsync<AblyException>(call);
                exception.ErrorInfo.Code.Should().Be(40003);
            }

            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSL11a")]
        public async Task GetMessageVersionsAsync_WithoutASerial_ShouldThrowWithCode40003AndSendNoRequest()
        {
            var channel = GetRestClient().Channels.Get("test");

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.GetMessageVersionsAsync(string.Empty));

            exception.ErrorInfo.Code.Should().Be(40003);
            Requests.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(Serials))]
        [Trait("spec", "RSL14b")]
        public async Task GetMessageVersionsAsync_ShouldGetTheVersionsEndpoint(string serial, string encodedSerial)
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = "[]" }.ToTask());

            await client.Channels.Get("test").GetMessageVersionsAsync(serial);

            LastRequest.Method.Should().Be(HttpMethod.Get);
            LastRequest.Url.Should().Be($"/channels/test/messages/{encodedSerial}/versions");
        }

        [Fact]
        [Trait("spec", "RSL14a1")]
        public async Task GetMessageVersionsAsync_WithAMessage_ShouldUseItsSerial()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = "[]" }.ToTask());

            await client.Channels.Get("test").GetMessageVersionsAsync(new Message { Serial = PlainSerial });

            LastRequest.Url.Should().Be("/channels/test/messages/abc123/versions");
        }

        [Fact]
        [Trait("spec", "RSL14c")]
        public async Task GetMessageVersionsAsync_ShouldReturnAPaginatedResultOfDecodedMessages()
        {
            var versions = $"[{MessageJson},{MessageJson.Replace("\"hello\"", "\"edited\"")}]";
            var client = GetRestClient(_ => new AblyResponse
            {
                Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                TextResponse = versions,
            }.ToTask());

            var result = await client.Channels.Get("test").GetMessageVersionsAsync(PlainSerial);

            result.Items.Select(x => x.Data).Should().Equal("hello", "edited");
            result.Items.Should().OnlyContain(x => x.Version != null && x.Annotations != null);
            result.HasNext.Should().BeTrue();
            result.NextQueryParams.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RSL14a")]
        public async Task GetMessageVersionsAsync_ShouldSendTheParamsAsTheQuerystring()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = "[]" }.ToTask());

            await client.Channels.Get("test").GetMessageVersionsAsync(
                PlainSerial,
                new PaginatedRequestParams { Limit = 5, Direction = QueryDirection.Forwards });

            LastRequest.QueryParameters["limit"].Should().Be("5");
            LastRequest.QueryParameters["direction"].Should().Be("forwards");
        }
    }
}
