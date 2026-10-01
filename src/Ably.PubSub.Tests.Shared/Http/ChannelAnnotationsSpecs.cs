using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Http
{
    public class ChannelAnnotationsSpecs : MockHttpRestSpecs
    {
        private const string Serial = "01826232498871-001@abcdefghij:001";
        private const string EncodedSerial = "01826232498871-001%40abcdefghij%3A001";

        public ChannelAnnotationsSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private JArray SentBody() => JArray.Parse(LastRequest.RequestBody.GetText());

        [Fact]
        [Trait("spec", "RSAN1c")]
        [Trait("spec", "RSAN1c1")]
        [Trait("spec", "RSAN1c2")]
        [Trait("spec", "RSAN1c5")]
        [Trait("spec", "RSAN1c6")]
        public async Task PublishAsync_ShouldPostAnArrayOfOneCreateAnnotationToTheAnnotationsEndpoint()
        {
            var client = GetRestClient(null, o => o.IdempotentRestPublishing = false);

            await client.Channels.Get("test").Annotations.PublishAsync(Serial, new Annotation
            {
                Type = "reaction:distinct.v1",
                Name = "like",
                Count = 2,
            });

            LastRequest.Method.Should().Be(HttpMethod.Post);
            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
            var body = SentBody();
            body.Should().HaveCount(1);
            body[0]["action"].Value<int>().Should().Be(0);
            body[0]["messageSerial"].Value<string>().Should().Be(Serial);
            body[0]["type"].Value<string>().Should().Be("reaction:distinct.v1");
            body[0]["name"].Value<string>().Should().Be("like");
            body[0]["count"].Value<int>().Should().Be(2);
        }

        [Fact]
        [Trait("spec", "RSAN1a1")]
        public async Task PublishAsync_WithAMessage_ShouldUseItsSerial()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").Annotations.PublishAsync(new Message { Serial = Serial }, new Annotation { Type = "total.v1" });

            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
        }

        [Fact]
        [Trait("spec", "RSAN1a2")]
        public async Task PublishAsync_ShouldOnlySendTheFieldsAPublisherMaySupply()
        {
            var client = GetRestClient(null, o => o.IdempotentRestPublishing = false);
            var annotation = new Annotation
            {
                Type = "flag.v1",
                ClientId = "me",
                Name = "n",
                Count = 1,
                Data = "d",
                Serial = "ignored-serial",
                ConnectionId = "ignored-connection",
                Timestamp = System.DateTimeOffset.UtcNow,
                MessageSerial = "ignored-message-serial",
                Action = AnnotationAction.Delete,
            };

            await client.Channels.Get("test").Annotations.PublishAsync(Serial, annotation);

            var sent = (JObject)SentBody()[0];
            sent.Properties().Select(p => p.Name).Should().BeEquivalentTo(
                "type", "clientId", "name", "count", "data", "action", "messageSerial");
            sent["action"].Value<int>().Should().Be(0);
            sent["messageSerial"].Value<string>().Should().Be(Serial);
            annotation.Action.Should().Be(AnnotationAction.Delete, "the caller's annotation must not be altered");
        }

        [Fact]
        [Trait("spec", "RSAN2a")]
        public async Task DeleteAsync_ShouldPostADeleteAnnotation()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").Annotations.DeleteAsync(Serial, new Annotation { Type = "reaction:distinct.v1", Name = "like" });

            LastRequest.Method.Should().Be(HttpMethod.Post);
            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
            SentBody()[0]["action"].Value<int>().Should().Be(1);
        }

        [Fact]
        [Trait("spec", "RSAN1a3")]
        public async Task PublishAsync_WithoutAType_ShouldThrowWithCode40003AndSendNoRequest()
        {
            var annotations = GetRestClient().Channels.Get("test").Annotations;

            var exception = await Assert.ThrowsAsync<AblyException>(() => annotations.PublishAsync(Serial, new Annotation { Name = "like" }));

            exception.ErrorInfo.Code.Should().Be(40003);
            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSAN1a1")]
        public async Task PublishAsync_WithoutASerial_ShouldThrowWithCode40003AndSendNoRequest()
        {
            var annotations = GetRestClient().Channels.Get("test").Annotations;
            var annotation = new Annotation { Type = "total.v1" };

            foreach (var call in new System.Func<Task>[]
            {
                () => annotations.PublishAsync((string)null, annotation),
                () => annotations.PublishAsync(string.Empty, annotation),
                () => annotations.PublishAsync(new Message(), annotation),
                () => annotations.DeleteAsync(new Message(), annotation),
            })
            {
                var exception = await Assert.ThrowsAsync<AblyException>(call);
                exception.ErrorInfo.Code.Should().Be(40003);
            }

            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSAN1c3")]
        public async Task PublishAsync_ShouldEncodeTheDataAsForAMessage()
        {
            var client = GetRestClient(null, o => o.IdempotentRestPublishing = false);

            await client.Channels.Get("test").Annotations.PublishAsync(Serial, new Annotation { Type = "t", Data = new { a = 1 } });

            var sent = SentBody()[0];
            sent["encoding"].Value<string>().Should().Be("json");
            sent["data"].Value<string>().Should().Be("{\"a\":1}");
        }

        [Fact]
        [Trait("spec", "RSAN1c3")]
        public async Task PublishAsync_OnAnEncryptedChannel_ShouldEncryptTheData()
        {
            var client = GetRestClient(null, o => o.IdempotentRestPublishing = false);
            var channel = client.Channels.Get("test", new ChannelOptions(Crypto.GetDefaultParams()));

            await channel.Annotations.PublishAsync(Serial, new Annotation { Type = "t", Data = "secret" });

            var sent = SentBody()[0];
            sent["encoding"].Value<string>().Should().Be("utf-8/cipher+aes-256-cbc/base64");
            sent["data"].Value<string>().Should().NotBe("secret");
        }

        [Theory]
        [InlineData(true, null, true)]
        [InlineData(true, "supplied", false)]
        [InlineData(false, null, false)]
        [Trait("spec", "RSAN1c4")]
        public async Task PublishAsync_ShouldGenerateAnIdOnlyWhenIdempotentRestPublishingIsEnabledAndNoneIsSet(bool idempotent, string suppliedId, bool expectGenerated)
        {
            var client = GetRestClient(null, o => o.IdempotentRestPublishing = idempotent);

            await client.Channels.Get("test").Annotations.PublishAsync(Serial, new Annotation { Type = "t", Id = suppliedId });

            var id = SentBody()[0]["id"]?.Value<string>();
            if (expectGenerated)
            {
                id.Should().MatchRegex("^[A-Za-z0-9+/=]{12,}:0$");
            }
            else
            {
                id.Should().Be(suppliedId);
            }
        }

        [Fact]
        [Trait("spec", "RSAN3b")]
        [Trait("spec", "RSAN3c")]
        public async Task GetAsync_ShouldGetTheAnnotationsEndpointAndReturnDecodedAnnotations()
        {
            const string json = "[{\"id\":\"a1\",\"action\":0,\"type\":\"reaction:distinct.v1\",\"name\":\"like\",\"messageSerial\":\"" + Serial + "\",\"serial\":\"s1\",\"data\":\"{\\\"a\\\":1}\",\"encoding\":\"json\",\"timestamp\":1700000000000}]";
            var client = GetRestClient(_ => new AblyResponse
            {
                Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                TextResponse = json,
            }.ToTask());

            var result = await client.Channels.Get("test").Annotations.GetAsync(Serial);

            LastRequest.Method.Should().Be(HttpMethod.Get);
            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
            LastRequest.QueryParameters.Should().BeEquivalentTo(new Dictionary<string, string> { ["limit"] = "100" });
            result.Items.Should().HaveCount(1);
            var annotation = result.Items[0];
            annotation.Action.Should().Be(AnnotationAction.Create);
            annotation.Type.Should().Be("reaction:distinct.v1");
            annotation.MessageSerial.Should().Be(Serial);
            annotation.Encoding.Should().BeNullOrEmpty();
            annotation.Data.Should().BeEquivalentTo(JObject.Parse("{\"a\":1}"));
        }

        [Fact]
        [Trait("spec", "RSAN3a")]
        public async Task GetAsync_ShouldSendTheLimitAndAcceptAMessage()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = "[]" }.ToTask());

            await client.Channels.Get("test").Annotations.GetAsync(new Message { Serial = Serial }, new AnnotationsRequestParams { Limit = 7 });

            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
            LastRequest.QueryParameters["limit"].Should().Be("7");
            LastRequest.QueryParameters.Should().NotContainKey("direction");
        }

        [Fact]
        [Trait("spec", "RSAN3c")]
        public async Task GetAsync_NextPage_ShouldReplayTheQueryReturnedByAbly()
        {
            var client = GetRestClient(_ => new AblyResponse
            {
                Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                TextResponse = "[]",
            }.ToTask());
            var first = await client.Channels.Get("test").Annotations.GetAsync(Serial);

            await first.NextAsync();

            Requests.Should().HaveCount(2);
            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}/annotations");
            LastRequest.QueryParameters["first_start"].Should().Be("1380794880000");
            LastRequest.QueryParameters["start"].Should().Be("1380794881111");
        }

        [Fact]
        [Trait("spec", "RSAN3a")]
        public async Task GetAsync_WithoutASerial_ShouldThrowWithCode40003()
        {
            var annotations = GetRestClient().Channels.Get("test").Annotations;

            var exception = await Assert.ThrowsAsync<AblyException>(() => annotations.GetAsync(string.Empty));

            exception.ErrorInfo.Code.Should().Be(40003);
            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSL10")]
        public void Annotations_ShouldBeExposedOnTheChannel()
        {
            GetRestClient().Channels.Get("test").Annotations.Should().BeOfType<RestAnnotations>();
        }
    }
}
