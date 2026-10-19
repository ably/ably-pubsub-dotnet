using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.AcceptanceTests;
using Ably.PubSub.Encryption;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Infrastructure;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    public class AnnotationsSpecs : AblyRealtimeSpecs
    {
        private const string Serial = "01826232498871-001@abcdefghij:001";

        public AnnotationsSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private static ProtocolMessage AnnotationFrame(params Annotation[] annotations) =>
            new ProtocolMessage(ProtocolMessage.MessageAction.Annotation, TestChannelName)
            {
                Id = "pm:1",
                ConnectionId = "other-connection",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000),
                Annotations = annotations,
            };

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel)> GetAttachedChannel(Action<ClientOptions> optionsAction = null)
        {
            var (client, channel) = await GetClientAndChannel(optionsAction);
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);
            return (client, channel);
        }

        private List<ProtocolMessage> SentAnnotationFrames() =>
            LastCreatedTransport.SentMessages
                .Select(x => x.Original)
                .Where(x => x.Action == ProtocolMessage.MessageAction.Annotation)
                .ToList();

        [Fact]
        [Trait("spec", "RTAN1a")]
        [Trait("spec", "RTAN1c")]
        [Trait("spec", "RSAN1c1")]
        [Trait("spec", "RSAN1c2")]
        public async Task PublishAsync_ShouldSendAnAnnotationProtocolMessageWithACreateAnnotation()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.Annotations.PublishAsync(Serial, new Annotation { Type = "reaction:distinct.v1", Name = "like" });
            await client.ProcessCommands();

            var frames = SentAnnotationFrames();
            frames.Should().HaveCount(1);
            frames[0].Channel.Should().Be(TestChannelName);
            frames[0].Annotations.Should().HaveCount(1);
            var annotation = frames[0].Annotations[0];
            annotation.Action.Should().Be(AnnotationAction.Create);
            annotation.MessageSerial.Should().Be(Serial);
            annotation.Type.Should().Be("reaction:distinct.v1");
            annotation.Name.Should().Be("like");

            var wire = JObject.Parse(LastCreatedTransport.SentMessages.Last().Text);
            wire["action"].Value<int>().Should().Be(21);
            wire["msgSerial"].Value<long>().Should().Be(0);
            wire["annotations"].Should().HaveCount(1);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 0, Count = 1 });
            (await publish).IsSuccess.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RTAN1a")]
        [Trait("spec", "RSAN1a3")]
        public async Task PublishAsync_WithoutAType_ShouldThrowWithCode40003AndSendNothing()
        {
            var (client, channel) = await GetAttachedChannel();

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.Annotations.PublishAsync(Serial, new Annotation { Name = "like" }));
            await client.ProcessCommands();

            exception.ErrorInfo.Code.Should().Be(40003);
            SentAnnotationFrames().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTAN1a")]
        [Trait("spec", "RSAN1a1")]
        public async Task PublishAsync_WithoutASerial_ShouldThrowWithCode40003AndSendNothing()
        {
            var (client, channel) = await GetAttachedChannel();

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.Annotations.PublishAsync(new Message(), new Annotation { Type = "total.v1" }));
            await client.ProcessCommands();

            exception.ErrorInfo.Code.Should().Be(40003);
            SentAnnotationFrames().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTAN1a")]
        [Trait("spec", "RSAN1c3")]
        public async Task PublishAsync_ShouldEncodeTheData()
        {
            var (client, channel) = await GetAttachedChannel();

            _ = channel.Annotations.PublishAsync(Serial, new Annotation { Type = "t", Data = new { a = 1 } });
            await client.ProcessCommands();

            var sent = JObject.Parse(LastCreatedTransport.SentMessages.Last().Text)["annotations"][0];
            sent["encoding"].Value<string>().Should().Be("json");
            sent["data"].Value<string>().Should().Be("{\"a\":1}");
        }

        [Theory]
        [InlineData(ChannelState.Suspended)]
        [InlineData(ChannelState.Failed)]
        [Trait("spec", "RTAN1b")]
        [Trait("spec", "RTL6c4")]
        public async Task PublishAsync_InAChannelStateWhichRefusesPublishing_ShouldThrow(ChannelState state)
        {
            var (client, channel) = await GetAttachedChannel();
            ((RealtimeChannel)channel).SetChannelState(state);

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.Annotations.PublishAsync(Serial, new Annotation { Type = "t" }));
            await client.ProcessCommands();

            exception.ErrorInfo.Code.Should().Be(40000);
            SentAnnotationFrames().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTAN1d")]
        [Trait("spec", "RTN7a")]
        public async Task PublishAsync_ShouldCompleteWithSuccessOnAckAndWithTheErrorOnNack()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.Annotations.PublishAsync(Serial, new Annotation { Type = "t", Name = "a" });
            var second = channel.Annotations.PublishAsync(Serial, new Annotation { Type = "t", Name = "b" });
            await client.ProcessCommands();

            SentAnnotationFrames().Select(x => x.MsgSerial).Should().Equal(0, 1);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 0, Count = 1 });
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Nack)
            {
                MsgSerial = 1,
                Count = 1,
                Error = new ErrorInfo("refused", 40160, System.Net.HttpStatusCode.Forbidden),
            });

            (await first).IsSuccess.Should().BeTrue();
            var nack = await second;
            nack.IsFailure.Should().BeTrue();
            nack.Error.Code.Should().Be(40160);
        }

        [Fact]
        [Trait("spec", "RTAN2a")]
        public async Task DeleteAsync_ShouldSendAnAnnotationProtocolMessageWithADeleteAnnotation()
        {
            var (client, channel) = await GetAttachedChannel();

            _ = channel.Annotations.DeleteAsync(Serial, new Annotation { Type = "reaction:distinct.v1", Name = "like" });
            await client.ProcessCommands();

            var frames = SentAnnotationFrames();
            frames.Should().HaveCount(1);
            frames[0].Annotations[0].Action.Should().Be(AnnotationAction.Delete);
            frames[0].Annotations[0].MessageSerial.Should().Be(Serial);
        }

        [Fact]
        [Trait("spec", "RTAN4a")]
        [Trait("spec", "RTAN4b")]
        [Trait("spec", "RTAN4c")]
        public async Task Subscribe_ShouldDeliverDecodedAnnotationsAndOnlyTheOnesOfTheRequestedType()
        {
            var (client, channel) = await GetAttachedChannel();
            var all = new List<Annotation>();
            var flags = new List<Annotation>();
            channel.Annotations.Subscribe(all.Add);
            channel.Annotations.Subscribe("flag.v1", flags.Add);

            client.FakeProtocolMessageReceived(AnnotationFrame(
                new Annotation { Type = "reaction:distinct.v1", Name = "like", Serial = "s1", MessageSerial = Serial, Action = AnnotationAction.Create, Data = "{\"a\":1}", Encoding = "json" },
                new Annotation { Type = "flag.v1", Serial = "s2", MessageSerial = Serial, Action = AnnotationAction.Delete }));
            await client.ProcessCommands();

            all.Select(x => x.Type).Should().Equal("reaction:distinct.v1", "flag.v1");
            all[0].Data.Should().BeEquivalentTo(JObject.Parse("{\"a\":1}"));
            all[0].Encoding.Should().BeNullOrEmpty();
            all.Select(x => x.Id).Should().Equal("pm:1:0", "pm:1:1");
            all[0].ConnectionId.Should().Be("other-connection");
            all[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            flags.Should().HaveCount(1);
            flags[0].Serial.Should().Be("s2");
            flags[0].Action.Should().Be(AnnotationAction.Delete);
        }

        [Fact]
        [Trait("spec", "RTAN4c")]
        public async Task Subscribe_ShouldMatchTheAnnotationTypeExactly()
        {
            var (client, channel) = await GetAttachedChannel();
            var upperCase = new List<Annotation>();
            var exact = new List<Annotation>();
            channel.Annotations.Subscribe("Reaction", upperCase.Add);
            Action<Annotation> exactHandler = exact.Add;
            channel.Annotations.Subscribe("reaction", exactHandler);

            // Unsubscribing with a different case leaves the exact subscription in place.
            channel.Annotations.Unsubscribe("Reaction", exactHandler).Should().BeFalse();

            client.FakeProtocolMessageReceived(AnnotationFrame(
                new Annotation { Type = "reaction", Serial = "s1", MessageSerial = Serial, Action = AnnotationAction.Create }));
            await client.ProcessCommands();

            upperCase.Should().BeEmpty();
            exact.Should().HaveCount(1);
        }

        [Fact]
        [Trait("spec", "RTAN4d")]
        public async Task Subscribe_ShouldImplicitlyAttachTheChannel()
        {
            var (client, channel) = await GetClientAndChannel();
            channel.State.Should().Be(ChannelState.Initialized);

            channel.Annotations.Subscribe(_ => { });
            await client.ProcessCommands();

            channel.State.Should().Be(ChannelState.Attaching);
            LastCreatedTransport.SentMessages.Select(x => x.Original.Action)
                .Should().Contain(ProtocolMessage.MessageAction.Attach);
        }

        [Fact]
        [Trait("spec", "RTAN4e")]
        public async Task Subscribe_WhenTheAttachedChannelWasNotGrantedTheMode_ShouldLogAWarning()
        {
            var (client, channel) = await GetClientAndChannel();
            var sink = new TestLoggerSink();

            using (DefaultLogger.SetTempDestination(sink))
            {
                channel.Annotations.Subscribe(_ => { });
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached, TestChannelName)
                {
                    Flags = (int)(ProtocolMessage.Flag.Subscribe | ProtocolMessage.Flag.Publish),
                });
                await client.ProcessCommands();
            }

            channel.State.Should().Be(ChannelState.Attached);
            sink.Messages.Should().Contain(x => x.StartsWith("Warning") && x.Contains("AnnotationSubscribe"));
        }

        [Fact]
        [Trait("spec", "RTAN4e")]
        public async Task Subscribe_WhenTheAttachedChannelWasGrantedTheMode_ShouldNotWarn()
        {
            var (client, channel) = await GetClientAndChannel();
            var sink = new TestLoggerSink();

            using (DefaultLogger.SetTempDestination(sink))
            {
                channel.Annotations.Subscribe(_ => { });
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached, TestChannelName)
                {
                    Flags = (int)(ProtocolMessage.Flag.Subscribe | ProtocolMessage.Flag.AnnotationSubscribe),
                });
                await client.ProcessCommands();
            }

            channel.Modes.Should().Contain(ChannelMode.AnnotationSubscribe);
            sink.Messages.Should().NotContain(x => x.Contains("AnnotationSubscribe"));
        }

        [Fact]
        [Trait("spec", "RTAN5a")]
        public async Task Unsubscribe_ShouldRemoveTheListenersForAllAnnotationsAndForATypeAndNoMore()
        {
            var (client, channel) = await GetAttachedChannel();
            var all = new List<Annotation>();
            var flags = new List<Annotation>();
            var other = new List<Annotation>();
            Action<Annotation> allHandler = all.Add;
            Action<Annotation> flagHandler = flags.Add;
            channel.Annotations.Subscribe(allHandler);
            channel.Annotations.Subscribe("flag.v1", flagHandler);
            channel.Annotations.Subscribe("other.v1", other.Add);

            // by type
            channel.Annotations.Unsubscribe("flag.v1", flagHandler).Should().BeTrue();
            client.FakeProtocolMessageReceived(AnnotationFrame(new Annotation { Type = "flag.v1" }));
            await client.ProcessCommands();
            all.Should().HaveCount(1);
            flags.Should().BeEmpty();

            // the listener of all annotations
            channel.Annotations.Unsubscribe(allHandler).Should().BeTrue();
            client.FakeProtocolMessageReceived(AnnotationFrame(new Annotation { Type = "flag.v1" }, new Annotation { Type = "other.v1" }));
            await client.ProcessCommands();
            all.Should().HaveCount(1);
            other.Should().HaveCount(1);

            // everything
            channel.Annotations.Unsubscribe();
            client.FakeProtocolMessageReceived(AnnotationFrame(new Annotation { Type = "other.v1" }));
            await client.ProcessCommands();
            other.Should().HaveCount(1);
        }

        [Fact]
        [Trait("spec", "RTL15b")]
        public async Task AnnotationProtocolMessage_ShouldUpdateTheChannelSerial()
        {
            var (client, channel) = await GetAttachedChannel();
            var frame = AnnotationFrame(new Annotation { Type = "t" });
            frame.ChannelSerial = "serial-after-annotation";

            client.FakeProtocolMessageReceived(frame);
            await client.ProcessCommands();

            channel.Properties.ChannelSerial.Should().Be("serial-after-annotation");
        }

        [Fact]
        [Trait("spec", "RTL28")]
        [Trait("spec", "RTL31")]
        [Trait("spec", "RTAN3a")]
        public async Task Retrieval_ShouldDelegateToRestSoTheRequestsAreTheOnesTheRestChannelMakes()
        {
            var client = await GetConnectedClient(handleRequestFunc: _ => new AblyResponse { TextResponse = "[]" }.ToTask());
            var realtimeChannel = client.Channels.Get(TestChannelName);
            var restChannel = client.HttpClient.Channels.Get(TestChannelName);
            var query = new PaginatedRequestParams { Limit = 3 };

            async Task<AblyRequest> RequestOf(Func<Task> call)
            {
                Requests.Clear();
                try
                {
                    await call();
                }
                catch (Exception)
                {
                    // the canned response is not a message; only the request matters here
                }

                return Requests.Single();
            }

            void ShouldBeTheSame(AblyRequest actual, AblyRequest expected)
            {
                actual.Method.Should().Be(expected.Method);
                actual.Url.Should().Be(expected.Url);
                actual.QueryParameters.Should().BeEquivalentTo(expected.QueryParameters);
            }

            ShouldBeTheSame(
                await RequestOf(() => realtimeChannel.GetMessageAsync(Serial)),
                await RequestOf(() => restChannel.GetMessageAsync(Serial)));
            ShouldBeTheSame(
                await RequestOf(() => realtimeChannel.GetMessageVersionsAsync(Serial, query)),
                await RequestOf(() => restChannel.GetMessageVersionsAsync(Serial, query)));
            ShouldBeTheSame(
                await RequestOf(() => realtimeChannel.Annotations.GetAsync(Serial, new AnnotationsRequestParams { Limit = 3 })),
                await RequestOf(() => restChannel.Annotations.GetAsync(Serial, new AnnotationsRequestParams { Limit = 3 })));
        }

        [Fact]
        [Trait("spec", "RTL28")]
        [Trait("spec", "RTAN3a")]
        public async Task Retrieval_OnACipherChannel_ShouldDecryptThroughTheRestRoute()
        {
            var options = new ChannelOptions(Crypto.GetDefaultParams());
            var message = new Message("n", "secret message") { Serial = Serial };
            var annotation = new Annotation { Type = "t", Name = "like", Data = "secret annotation", MessageSerial = Serial };
            MessageHandler.EncodePayloads(options.ToDecodingContext(), new IMessage[] { message, annotation }).IsSuccess.Should().BeTrue();
            message.Encoding.Should().Contain("cipher+aes-256-cbc");

            var client = await GetConnectedClient(handleRequestFunc: request =>
                new AblyResponse
                {
                    TextResponse = request.Url.EndsWith("/annotations")
                        ? JsonHelper.Serialize(new[] { annotation })
                        : JsonHelper.Serialize(message),
                }.ToTask());
            var channel = client.Channels.Get(TestChannelName, options);

            var retrieved = await channel.GetMessageAsync(Serial);
            var annotations = await channel.Annotations.GetAsync(Serial);

            retrieved.Data.Should().Be("secret message");
            annotations.Items.Should().ContainSingle().Which.Data.Should().Be("secret annotation");
        }
    }
}
