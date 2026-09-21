using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.AcceptanceTests;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("SandBox Connection")]
    [Trait("type", "integration")]
    public class DeltaSandboxSpecs : SandboxSpecs
    {
        public class TestData
        {
            public string Foo { get; }

            public int Count { get; }

            public string Status { get; }

            public TestData(string foo, int count, string status)
            {
                Foo = foo;
                Count = count;
                Status = status;
            }

            public override string ToString()
            {
                return "foo = " + Foo + "; count = " + Count + "; status = " + Status;
            }

            protected bool Equals(TestData other)
            {
                return Foo == other.Foo && Count == other.Count && Status == other.Status;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                {
                    return false;
                }

                if (ReferenceEquals(this, obj))
                {
                    return true;
                }

                if (obj.GetType() != GetType())
                {
                    return false;
                }

                return Equals((TestData)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Foo != null ? Foo.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ Count;
                    hashCode = (hashCode * 397) ^ (Status != null ? Status.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        [Theory]
        [ProtocolData]
        public async Task DeltaSupport_ShouldWork(Protocol protocol)
        {
            string testName = "delta-channel".AddRandomSuffix();
            var realtime = await GetRealtimeClient(protocol);
            var channel = realtime.Channels.Get("[?delta=vcdiff]" + testName);

            var received = new List<Message>();
            channel.Subscribe(message =>
            {
                received.Add(message);
                Output.WriteLine(((RealtimeChannel)channel).LastSuccessfulMessageIds.ToString());
            });
            channel.Error += (sender, args) =>
                throw new Exception(args.Reason.Message);
            /* subscribe */
            await channel.AttachAsync();

            var testData = new[]
            {
                new TestData("bar", 1, "active"),
                new TestData("bar", 2, "active"),
                new TestData("bar", 3, "inactive"),
            };

            foreach (var data in testData)
            {
                await channel.PublishAsync(data.Count.ToString(), data);
            }

            await new ConditionalAwaiter(() => received.Count == 3);

            for (var i = 0; i < received.Count; i++)
            {
                var message = received[i];
                var data = ((JToken)message.Data).ToObject<TestData>();
                var original = testData[i];
                Assert.Equal(data, original);
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL21")]
        public async Task ChannelWithDeltasEnabled_ShouldSupportProcessingMultipleMessagesInSamePayload(Protocol protocol)
        {
            string testName = "delta-channel".AddRandomSuffix();
            var receivedMessages = new List<ProtocolMessage>();
            var realtime = await GetRealtimeClient(protocol, (options, settings) =>
            {
                var optionsTransportFactory = new TestTransportFactory
                {
                    BeforeDataProcessed = receivedMessages.Add,
                };
                options.TransportFactory = optionsTransportFactory;
            });
            var channel = realtime.Channels.Get("[?delta=vcdiff]" + testName);

            var waitForDone = new TaskCompletionAwaiter();
            var received = new List<Message>();
            int count = 0;
            channel.Subscribe(message =>
            {
                count++;
                received.Add(message);
                if (count == 3)
                {
                    waitForDone.Done();
                }
            });

            channel.Error += (sender, args) =>
                throw new Exception(args.Reason.Message);
            /* subscribe */
            await channel.AttachAsync();

            var testData = new[]
            {
                new TestData("bar", 1, "active"),
                new TestData("bar", 2, "active"),
                new TestData("bar", 3, "inactive"),
            };

            await channel.PublishAsync(testData.Select(x => new Message(x.Count.ToString(), x)));
            await waitForDone;

            for (var i = 0; i < received.Count; i++)
            {
                var message = received[i];
                var data = ((JToken)message.Data).ToObject<TestData>();
                var original = testData[i];
                Assert.Equal(data, original);
            }

            receivedMessages
                .Where(x => x.Action == ProtocolMessage.MessageAction.Message)
                .Should().HaveCount(1);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL20")]
        public async Task ChannelWithDeltaEncoding_WhenStoredLastMessageIdDoesNotMatchWithWhatServerThinks_ShouldReattachChannel(Protocol protocol)
        {
            string testName = "delta-channel".AddRandomSuffix();
            var realtime = await GetRealtimeClient(protocol);
            var channel = realtime.Channels.Get("[?delta=vcdiff]" + testName);
            await channel.AttachAsync();

            var changeStates = new List<ChannelStateChange>();
            channel.On(changeStates.Add);
            int count = 0;
            channel.Subscribe(message =>
            {
                if (count == 0)
                {
                    ((RealtimeChannel)channel).LastSuccessfulMessageIds.LastMessageId = "override";
                }

                count++;
            });

            var testData = new[]
            {
                new TestData("bar", 1, "active"),
                new TestData("bar", 2, "active"),
                new TestData("bar", 3, "inactive"),
            };

            foreach (var data in testData)
            {
                await channel.PublishAsync(data.Count.ToString(), data);
                await Task.Delay(500);
            }

            // The first message is sent twice because we messed up with lastmessageIds
            await new ConditionalAwaiter(() => count == 3, () => $"Count is {count}.");

            // Should transition to attaching
            changeStates.First().Current.Should().Be(ChannelState.Attaching);
            changeStates.First().Error.Code.Should().Be(40018);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL18")]
        [Trait("spec", "RTL18c")]
        public async Task WhenDeltaDecodeFail_ShouldSetStateToAttachingLogTheErrorAndDiscardTheMessage(Protocol protocol)
        {
            string channelName = "delta-channel".AddRandomSuffix();
            var testSink = new TestLoggerSink();
            var taskAwaiter = new TaskCompletionAwaiter(20000);
            var firstMessageReceived = new TaskCompletionAwaiter(20000);
            using (((IInternalLogger)Logger).CreateDisposableLoggingContext(testSink))
            {
                var realtime = await GetRealtimeClient(protocol);
                var channel = realtime.Channels.Get(channelName, new ChannelOptions(channelParams: new ChannelParams { { "delta", "vcdiff" } }));

                var received = new List<Message>();
                channel.Subscribe(message =>
                {
                    received.Add(message);
                    if (received.Count == 1)
                    {
                        firstMessageReceived.Done();
                    }

                    if (received.Count == 2)
                    {
                        taskAwaiter.Done();
                    }
                });

                await channel.AttachAsync();
                // We wait for the first message to be acknowledged. Any consequent messages will be deltas
                await channel.PublishAsync("firstMessage", new TestData("bar", 1, "active"));

                (await firstMessageReceived).Should().BeTrue("First message should be received before continuing with broken message");

                channel.Publish("second", "second message"); // We don't want to wait for the acknowledgement
                realtime.ExecuteCommand(ProcessMessageCommand.Create(new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                {
                    Channel = channelName,
                    Messages = new[]
                    {
                        new Message
                        {
                            Id = "badMessage", Encoding = "vcdiff", Data = Array.Empty<byte>()
                        },
                    },
                }));

                await channel.WaitForState(ChannelState.Attaching);

                await channel.WaitForAttachedState();

                var result = await taskAwaiter;

                result.Should().BeTrue("TaskAwaiter Done() Should be called.");

                received.Should().HaveCount(2);
                received[1].Data.Should().Be("second message");

                // RTL17 - we make sure the message is not emitted to subscribers
                received.Should().NotContain(x => x.Id == "badMessage");

                bool hasVcdiffErrorMessage = testSink.Messages.Any(x => x.StartsWith(LogLevel.Error.ToString())
                                                                        && x.Contains(ErrorCodes.VcDiffDecodeError
                                                                            .ToString()));

                hasVcdiffErrorMessage.Should().BeTrue();
            }
        }

        public DeltaSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }
    }
}
