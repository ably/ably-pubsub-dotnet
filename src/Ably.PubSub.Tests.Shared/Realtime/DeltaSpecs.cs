using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    public class DeltaSpecs : AblyRealtimeSpecs
    {
        private MessageExtras CreateExtrasWithDelta(DeltaExtras delta)
        {
            var jObject = new JObject();
            jObject[MessageExtras.DeltaProperty] = JObject.FromObject(delta);
            return new MessageExtras(jObject);
        }

        [Fact]
        [Trait("spec", "RSL6c2")]
        public async Task WhenMessageReceived_WithDeltaError_ShouldNotPassMessageToChannelSubscriber()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);
            List<Message> receivedMessages = new List<Message>();
            channel.Subscribe(receivedMessages.Add);

            realtime.ExecuteCommand(ProcessMessageCommand.Create(
                new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                {
                    Channel = channel.Name,
                    Messages = new[] { new Message { Id = "goodMessage", Data = "test" }, },
                }));

            realtime.ExecuteCommand(ProcessMessageCommand.Create(
                new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                {
                    Channel = channel.Name,
                    Messages = new[]
                    {
                        new Message { Extras = CreateExtrasWithDelta(new DeltaExtras("1", string.Empty)) },
                    },
                }));

            await Task.Delay(2000); // wait for 2 seconds

            // We want to ensure just the good message was received
            receivedMessages.Should().HaveCount(1);
            receivedMessages.Should().Contain(x => x.Id == "goodMessage");
        }

        [Fact]
        [Trait("spec", "RSL6c3")]
        [Trait("spec", "RSL6e")]
        public async Task
            WhenMessageReceived_WithNotMatchingDeltaFromProperty_ShouldStartDecodeRecoveryAndMoveToAttachingWithError()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);
            var awaiter = new TaskCompletionAwaiter();
            ChannelStateChange stateChange = null;
            channel.On(ChannelEvent.Attaching, change =>
            {
                stateChange = change;
                channel.DecodeRecovery.Should().BeTrue();
                awaiter.Done();
            });

            realtime.ExecuteCommand(ProcessMessageCommand.Create(
                new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                {
                    Channel = channel.Name,
                    Messages = new[]
                    {
                        new Message { Extras = CreateExtrasWithDelta(new DeltaExtras("1", string.Empty)) },
                    },
                }));

            await awaiter.Task;

            stateChange.Current.Should().Be(ChannelState.Attaching);
            stateChange.Error.Code.Should().Be(ErrorCodes.VcDiffDecodeError);
        }

        [Fact]
        [Trait("spec", "RSL6c3")]
        public async Task
            WhenMessageReceivedAndFailsVcdiffDecoding_ShouldSendAttachProtocolMessageWithLastSuccessfulChannelSerial()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);

            var successfulProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
            {
                Channel = channel.Name,
                ChannelSerial = "testSerial",
                Messages = new[] { new Message { Data = "test", Encoding = string.Empty }, },
            };
            realtime.ExecuteCommand(ProcessMessageCommand.Create(successfulProtocolMessage));

            var awaiter = new TaskCompletionAwaiter();
            channel.On(ChannelEvent.Attaching, change => { awaiter.Tick(); });

            realtime.ExecuteCommand(ProcessMessageCommand.Create(
                new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                {
                    Channel = channel.Name,
                    Messages = new[]
                    {
                        new Message { Extras = CreateExtrasWithDelta(new DeltaExtras("1", string.Empty)) },
                    },
                }));

            await awaiter.Task;
            await realtime.ProcessCommands();

            var fakeTransport = FakeTransportFactory.LastCreatedTransport;
            var lastMessageSend = fakeTransport.LastMessageSend;
            lastMessageSend.Should().NotBeNull();
            lastMessageSend.Action.Should().Be(ProtocolMessage.MessageAction.Attach);
            lastMessageSend.ChannelSerial.Should().Be("testSerial");
        }

        [Fact(Skip = "Keeps failing")]
        [Trait("spec", "RSL6f")]
        [Trait("linux", "skip")]
        public async Task
            WhenMultipleMessagesWithAMixOfNormalAndDeltaMessages_ShouldDecodeCorrectly()
        {
            using var logging = EnableDebugLogging();
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);

            List<Message> messages = new List<Message>();
            var taskAwaiter = new TaskCompletionAwaiter(15000);
            channel.Subscribe(x =>
            {
                messages.Add(x);
                if (messages.Count == 4)
                {
                    taskAwaiter.Done();
                }
            });

            var protocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
            {
                Channel = channel.Name,
                Messages = new[]
                {
                    CreateMessage("delta.1", false),
                    CreateMessage("delta.1.vcdiff", true),
                    CreateMessage("delta.2.vcdiff", true),
                    CreateMessage("delta.3.vcdiff", true),
                },
            };

            await realtime.ProcessMessage(protocolMessage);
            await realtime.ProcessCommands();

            var result = await taskAwaiter;
            result.Should().BeTrue("Four messages should have been received.");

            messages[0].Data.Should().BeOfType<string>();
            IsCorrectFile(((string)messages[0].Data).GetBytes(), "delta.1");
            messages[1].Data.Should().BeOfType<byte[]>();
            IsCorrectFile((byte[])messages[1].Data, "delta.2");
            messages[2].Data.Should().BeOfType<byte[]>();
            IsCorrectFile((byte[])messages[2].Data, "delta.3");
            messages[3].Data.Should().BeOfType<byte[]>();
            IsCorrectFile((byte[])messages[3].Data, "delta.4");

            static void IsCorrectFile(byte[] actual, string expectedFile)
            {
                actual.SequenceEqual(ResourceHelper.GetBinaryResource(expectedFile)).Should().BeTrue("Bytes are not the same as " + expectedFile);
            }

            static Message CreateMessage(string filename, bool isDelta)
            {
                if (isDelta)
                {
                    return new Message
                    {
                        Data = ResourceHelper.GetBinaryResource(filename).ToBase64(),
                        Encoding = "vcdiff/base64",
                    };
                }

                return new Message
                {
                    Data = ResourceHelper.GetResource(filename),
                    Encoding = string.Empty,
                };
            }
        }

        public DeltaSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
