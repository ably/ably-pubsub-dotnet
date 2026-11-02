using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    public class MessageEditSpecs : AblyRealtimeSpecs
    {
        private const string Serial = "01826232498871-001@abcdefghij:001";

        public MessageEditSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

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

        private static ProtocolMessage Ack(long msgSerial, params PublishResult[] res) =>
            new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = msgSerial, Count = 1, Res = res.Length == 0 ? null : res };

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel)> GetAttachedChannel()
        {
            var (client, channel) = await GetClientAndChannel();
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);
            return (client, channel);
        }

        private static Task<Result<UpdateDeleteResult>> Edit(IRealtimeChannel channel, MessageAction action, Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null)
        {
            switch (action)
            {
                case MessageAction.MessageUpdate: return channel.UpdateMessageAsync(message, operation, parameters);
                case MessageAction.MessageDelete: return channel.DeleteMessageAsync(message, operation, parameters);
                default: return channel.AppendMessageAsync(message, operation, parameters);
            }
        }

        private List<RealtimeTransportData> SentMessageFrames() =>
            LastCreatedTransport.SentMessages.Where(x => x.Original.Action == ProtocolMessage.MessageAction.Message).ToList();

        [Theory]
        [InlineData(MessageAction.MessageUpdate, 1)]
        [InlineData(MessageAction.MessageDelete, 2)]
        [InlineData(MessageAction.MessageAppend, 5)]
        [Trait("spec", "RTL32b")]
        [Trait("spec", "RTL32b1")]
        public async Task ShouldSendAMessageProtocolMessageWithASingleMessageAndTheAction(MessageAction action, int wireAction)
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = Edit(channel, action, new Message("n", "d") { Serial = Serial });
            await client.ProcessCommands();

            var frames = SentMessageFrames();
            frames.Should().HaveCount(1);
            frames[0].Original.Channel.Should().Be(TestChannelName);
            frames[0].Original.Messages.Should().HaveCount(1);
            var sent = frames[0].Original.Messages[0];
            sent.Action.Should().Be(action);
            sent.Serial.Should().Be(Serial);
            sent.Name.Should().Be("n");

            var wire = JObject.Parse(frames[0].Text)["messages"][0];
            wire["action"].Value<int>().Should().Be(wireAction);
            wire["serial"].Value<string>().Should().Be(Serial);
            wire["data"].Value<string>().Should().Be("d");
        }

        [Fact]
        [Trait("spec", "RTL32b2")]
        public async Task ShouldSendTheOperationAsTheVersion()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.UpdateMessageAsync(
                new Message { Serial = Serial, Data = "d" },
                new MessageOperation
                {
                    ClientId = "user1",
                    Description = "fixed typo",
                    Metadata = new Dictionary<string, string> { { "reason", "typo" } },
                });
            await client.ProcessCommands();

            var version = JObject.Parse(SentMessageFrames().Single().Text)["messages"][0]["version"];
            version["clientId"].Value<string>().Should().Be("user1");
            version["description"].Value<string>().Should().Be("fixed typo");
            version["metadata"]["reason"].Value<string>().Should().Be("typo");
        }

        [Fact]
        [Trait("spec", "RTL32b2")]
        public async Task ShouldOmitTheVersionWhenThereIsNoOperation()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.UpdateMessageAsync(new Message { Serial = Serial, Data = "d" });
            await client.ProcessCommands();

            JObject.Parse(SentMessageFrames().Single().Text)["messages"][0].Should().NotContain(x => ((JProperty)x).Name == "version");
        }

        [Fact]
        [Trait("spec", "RTL32c")]
        public async Task ShouldNotMutateTheCallersMessage()
        {
            var (client, channel) = await GetAttachedChannel();
            var message = FullMessage();
            var before = Snapshot(message);

            var edit = channel.UpdateMessageAsync(message, new MessageOperation { ClientId = "c", Description = "d" });
            await client.ProcessCommands();

            Snapshot(message).Should().Equal(before);
        }

        [Fact]
        [Trait("spec", "RTL32d")]
        public async Task WhenAcked_ShouldReturnTheFirstSerialOfTheResAsTheVersionSerial()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.DeleteMessageAsync(new Message { Serial = Serial });
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, new PublishResult(new[] { "vs1" })));

            var result = await edit;
            result.IsSuccess.Should().BeTrue();
            result.Value.VersionSerial.Should().Be("vs1");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [Trait("spec", "RTL32d")]
        [Trait("spec", "UDR2a")]
        public async Task WhenAckedWithoutAUsableSerial_ShouldReturnAResultWithANullVersionSerial(int variant)
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.UpdateMessageAsync(new Message { Serial = Serial });
            await client.ProcessCommands();
            switch (variant)
            {
                case 0: client.FakeProtocolMessageReceived(Ack(0)); break; // no res
                case 1: client.FakeProtocolMessageReceived(Ack(0, new PublishResult(new string[0]))); break; // no serials
                default: client.FakeProtocolMessageReceived(Ack(0, new PublishResult(new string[] { null }))); break; // superseded
            }

            var result = await edit;
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.VersionSerial.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RTL32d")]
        public async Task WhenNacked_ShouldReturnAFailedResultWithTheError()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.AppendMessageAsync(new Message { Serial = Serial, Data = "x" });
            await client.ProcessCommands();
            var error = new ErrorInfo("rejected", 40160);
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 1, Error = error });

            var result = await edit;
            result.IsFailure.Should().BeTrue();
            result.Error.Should().BeSameAs(error);
        }

        [Fact]
        [Trait("spec", "RTL32e")]
        public async Task ShouldSendTheParamsInTheProtocolMessageParams()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.UpdateMessageAsync(
                new Message { Serial = Serial, Data = "x" },
                null,
                new Dictionary<string, string> { { "foo", "bar" } });
            await client.ProcessCommands();

            var frame = SentMessageFrames().Single();
            frame.Original.Params.Should().Contain("foo", "bar");
            JObject.Parse(frame.Text)["params"]["foo"].Value<string>().Should().Be("bar");
        }

        [Fact]
        [Trait("spec", "RTL32e")]
        public async Task WithoutParams_ShouldNotSendProtocolMessageParams()
        {
            var (client, channel) = await GetAttachedChannel();

            var edit = channel.UpdateMessageAsync(new Message { Serial = Serial, Data = "x" });
            await client.ProcessCommands();

            SentMessageFrames().Single().Original.Params.Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [Trait("spec", "RTL32a")]
        public async Task WithoutASerial_ShouldThrow40003AndSendNothing(string serial)
        {
            var (client, channel) = await GetAttachedChannel();

            foreach (var action in new[] { MessageAction.MessageUpdate, MessageAction.MessageDelete, MessageAction.MessageAppend })
            {
                var ex = await Assert.ThrowsAsync<AblyException>(() => Edit(channel, action, new Message { Serial = serial, Data = "d" }));
                ex.ErrorInfo.Code.Should().Be(40003);
                ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            await client.ProcessCommands();
            SentMessageFrames().Should().BeEmpty();
        }

        [Theory]
        [InlineData(ChannelState.Suspended)]
        [InlineData(ChannelState.Failed)]
        [Trait("spec", "RTL32")]
        [Trait("spec", "RTL6c4")]
        public async Task InAChannelStateWhichRefusesPublishing_ShouldThrow(ChannelState state)
        {
            var (client, channel) = await GetAttachedChannel();
            ((RealtimeChannel)channel).SetChannelState(state);

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.UpdateMessageAsync(new Message { Serial = Serial, Data = "d" }));
            await client.ProcessCommands();

            exception.ErrorInfo.Code.Should().Be(40000);
            SentMessageFrames().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "TM2j")]
        [Trait("spec", "TM2r")]
        [Trait("spec", "TM2s")]
        public async Task InboundMessageUpdate_ShouldReachASubscriberWithActionSerialAndVersion()
        {
            var (client, channel) = await GetAttachedChannel();
            var received = new TaskCompletionSource<Message>();
            channel.Subscribe(m => received.TrySetResult(m));

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Message, TestChannelName)
            {
                Id = "pm:1",
                ConnectionId = "other-connection",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000),
                Messages = new[]
                {
                    new Message("n", "updated")
                    {
                        Serial = Serial,
                        Action = MessageAction.MessageUpdate,
                        Version = new MessageVersion { Serial = "version-serial-2", ClientId = "editor", Description = "fix" },
                    },
                },
            });

            var winner = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            winner.Should().BeSameAs(received.Task);
            var message = await received.Task;
            message.Action.Should().Be(MessageAction.MessageUpdate);
            message.Serial.Should().Be(Serial);
            message.Version.Serial.Should().Be("version-serial-2");
            message.Version.ClientId.Should().Be("editor");
            message.Version.Description.Should().Be("fix");
            message.Data.Should().Be("updated");
        }
    }
}
