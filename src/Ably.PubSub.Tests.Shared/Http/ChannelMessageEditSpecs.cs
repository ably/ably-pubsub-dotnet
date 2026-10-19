using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Tests.Infrastructure;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Http
{
    public class ChannelMessageEditSpecs : MockHttpRestSpecs
    {
        private const string Serial = "01826232498871-001@abcdefghij:001";
        private const string EncodedSerial = "01826232498871-001%40abcdefghij%3A001";

        public ChannelMessageEditSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        internal override AblyResponse DefaultResponse { get; } = new AblyResponse { TextResponse = "{\"versionSerial\":\"vs1\"}" };

        private static Message FullMessage() => new Message("n", "plain", "client", new MessageExtras(Newtonsoft.Json.Linq.JToken.Parse("{\"push\":{}}")))
        {
            Id = "id1",
            Serial = "s1",
            Encoding = "utf-8",
            ConnectionId = "conn",
            ConnectionKey = "ck",
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000),
            Action = MessageAction.MessageCreate,
            Version = new MessageVersion { Serial = "v1", ClientId = "vc", Description = "vd", Metadata = new Dictionary<string, string> { { "k", "v" } } },
            Annotations = new MessageAnnotations { Summary = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { { "reaction", 1 } } },
        };

        // Every property of the message, with the observable contents of the reference-typed ones, so that an
        // in-place mutation of Extras, Version or Annotations is detected as well as a reassignment.
        private static object[] Snapshot(Message m) =>
            new object[]
            {
                m.Id, m.ClientId, m.Name, m.Data, m.Encoding, m.Extras, m.Serial, m.Action, m.Version, m.ConnectionId, m.ConnectionKey, m.Timestamp, m.Annotations,
                m.Extras?.ToJson().ToString(),
                m.Version?.Serial, m.Version?.Timestamp, m.Version?.ClientId, m.Version?.Description,
                m.Version?.Metadata == null ? null : string.Join(",", m.Version.Metadata.Select(x => x.Key + "=" + x.Value)),
                m.Annotations?.Summary == null ? null : string.Join(",", m.Annotations.Summary.Select(x => x.Key + "=" + x.Value)),
            };

        private JObject SentBody() => JObject.Parse(LastRequest.RequestBody.GetText());

        private static Task<UpdateDeleteResult> Edit(Ably.PubSub.Http.IHttpChannel channel, MessageAction action, Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null)
        {
            switch (action)
            {
                case MessageAction.MessageUpdate: return channel.UpdateMessageAsync(message, operation, parameters);
                case MessageAction.MessageDelete: return channel.DeleteMessageAsync(message, operation, parameters);
                default: return channel.AppendMessageAsync(message, operation, parameters);
            }
        }

        [Theory]
        [InlineData(MessageAction.MessageUpdate, 1)]
        [InlineData(MessageAction.MessageDelete, 2)]
        [InlineData(MessageAction.MessageAppend, 5)]
        [Trait("spec", "RSL15b")]
        [Trait("spec", "RSL15b1")]
        public async Task ShouldPatchASingleMessageWithTheActionToTheSerialPath(MessageAction action, int wireAction)
        {
            var client = GetRestClient();

            await Edit(client.Channels.Get("test"), action, new Message("n", "d") { Serial = Serial });

            LastRequest.Method.Method.Should().Be("PATCH");
            LastRequest.Url.Should().Be($"/channels/test/messages/{EncodedSerial}");
            var body = SentBody();
            body["action"].Value<int>().Should().Be(wireAction);
            body["serial"].Value<string>().Should().Be(Serial);
            body["name"].Value<string>().Should().Be("n");
            body["data"].Value<string>().Should().Be("d");
        }

        [Fact]
        [Trait("spec", "RSL15b")]
        public async Task ShouldEncodeTheChannelNameInThePath()
        {
            var client = GetRestClient();

            await client.Channels.Get("test channel/1").UpdateMessageAsync(new Message { Serial = "s1", Data = "d" });

            LastRequest.Url.Should().Be("/channels/test%20channel%2F1/messages/s1");
        }

        [Fact]
        [Trait("spec", "RSL15b7")]
        public async Task ShouldSendTheOperationAsTheVersion()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" },
                new MessageOperation
                {
                    ClientId = "user1",
                    Description = "fixed typo",
                    Metadata = new Dictionary<string, string> { { "reason", "typo" } },
                });

            var version = SentBody()["version"];
            version["clientId"].Value<string>().Should().Be("user1");
            version["description"].Value<string>().Should().Be("fixed typo");
            version["metadata"]["reason"].Value<string>().Should().Be("typo");
            SentBody().Should().NotContainKey("operation");
        }

        [Fact]
        [Trait("spec", "RSL15b7")]
        public async Task ShouldOmitTheVersionWhenThereIsNoOperation()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").UpdateMessageAsync(new Message { Serial = "s1", Data = "updated" });

            SentBody().Should().NotContainKey("version");
        }

        [Fact]
        [Trait("spec", "RSL15c")]
        public async Task ShouldNotMutateTheCallersMessage()
        {
            var client = GetRestClient();
            var channel = client.Channels.Get("test", new ChannelOptions(Crypto.GetDefaultParams()));
            var message = FullMessage();
            var before = Snapshot(message);

            await channel.UpdateMessageAsync(message, new MessageOperation { ClientId = "c", Description = "d" });

            Snapshot(message).Should().Equal(before);
        }

        [Fact]
        [Trait("spec", "RSL15e")]
        public async Task ShouldReturnTheVersionSerialOfTheResponse()
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = "{\"versionSerial\":\"vs1\",\"extra\":1}" }.ToTask());

            var result = await client.Channels.Get("test").DeleteMessageAsync(new Message { Serial = "s1" });

            result.VersionSerial.Should().Be("vs1");
        }

        [Theory]
        [InlineData("{\"versionSerial\":null}")]
        [InlineData("{}")]
        [Trait("spec", "RSL15e")]
        [Trait("spec", "UDR2a")]
        public async Task ShouldReturnAResultWithANullVersionSerialWhenTheMessageWasSuperseded(string body)
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = body }.ToTask());

            var result = await client.Channels.Get("test").UpdateMessageAsync(new Message { Serial = "s1" });

            result.Should().NotBeNull();
            result.VersionSerial.Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("not json")]
        [Trait("spec", "RSL15e")]
        public async Task WhenTheResponseHasNoUsableBody_ShouldThrowAnAblyException(string body)
        {
            var client = GetRestClient(_ => new AblyResponse { TextResponse = body }.ToTask());

            var ex = await Assert.ThrowsAsync<AblyException>(() => client.Channels.Get("test").UpdateMessageAsync(new Message { Serial = "s1" }));

            ex.ErrorInfo.Code.Should().Be(50000);
            ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }

        [Fact]
        [Trait("spec", "RSL15f")]
        public async Task ShouldSendTheParamsInTheQueryString()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").AppendMessageAsync(
                new Message { Serial = "s1", Data = "x" },
                null,
                new Dictionary<string, string> { { "foo", "bar" }, { "n", "1" } });

            LastRequest.QueryParameters.Should().Contain("foo", "bar").And.Contain("n", "1");
            SentBody().Should().NotContainKey("foo");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [Trait("spec", "RSL15a")]
        public async Task WithoutASerial_ShouldThrow40003AndSendNothing(string serial)
        {
            var client = GetRestClient();
            var channel = client.Channels.Get("test");

            foreach (var action in new[] { MessageAction.MessageUpdate, MessageAction.MessageDelete, MessageAction.MessageAppend })
            {
                var ex = await Assert.ThrowsAsync<AblyException>(() => Edit(channel, action, new Message { Serial = serial, Data = "d" }));
                ex.ErrorInfo.Code.Should().Be(40003);
                ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSL15d")]
        [Trait("spec", "RSL4")]
        public async Task OnAnEncryptedChannel_ShouldEncodeTheBodyPerRsl4()
        {
            var client = GetRestClient();
            var channel = client.Channels.Get("test", new ChannelOptions(Crypto.GetDefaultParams()));

            await channel.UpdateMessageAsync(new Message { Serial = "s1", Data = "secret" });

            var body = SentBody();
            body["encoding"].Value<string>().Should().Be("utf-8/cipher+aes-256-cbc/base64");
            body["data"].Value<string>().Should().NotBe("secret");
        }

        [Fact]
        [Trait("spec", "RSL4")]
        public async Task ShouldEncodeJsonDataInTheBody()
        {
            var client = GetRestClient();

            await client.Channels.Get("test").UpdateMessageAsync(new Message { Serial = "s1", Data = new { a = 1 } });

            var body = SentBody();
            body["encoding"].Value<string>().Should().Be("json");
            body["data"].Value<string>().Should().Be("{\"a\":1}");
        }

        [Fact]
        [Trait("spec", "RSL15b")]
        public async Task OnTheWire_ShouldSendAPatchRequestWithTheMessageBody()
        {
            string wireMethod = null;
            string wireUri = null;
            string wireBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                wireMethod = request.Method.Method;
                wireUri = request.RequestUri.AbsolutePath;
                wireBody = request.Content?.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"versionSerial\":\"vs1\"}", System.Text.Encoding.UTF8, "application/json"),
                };
            });
            var options = new ClientOptions(ValidKey) { UseBinaryProtocol = false, HttpClient = new HttpClient(handler) };
            var client = new PubSubHttpClient(options);

            var result = await client.Channels.Get("test").UpdateMessageAsync(new Message { Serial = "s1", Data = "d" });

            result.VersionSerial.Should().Be("vs1");
            wireMethod.Should().Be("PATCH");
            wireUri.Should().Be("/channels/test/messages/s1");
            JObject.Parse(wireBody)["action"].Value<int>().Should().Be(1);
        }
    }
}
