using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    public class PublishResultSpecs : AblyRealtimeSpecs
    {
        public PublishResultSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private static PublishResult Serials(params string[] serials) => new PublishResult(serials);

        private static ProtocolMessage Ack(long msgSerial, int count, params PublishResult[] res) =>
            new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = msgSerial, Count = count, Res = res.Length == 0 ? null : res };

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel)> GetAttachedChannel(Action<ClientOptions> optionsAction = null)
        {
            var (client, channel) = await GetClientAndChannel(optionsAction);
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);
            return (client, channel);
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "TR4s")]
        public async Task PublishAsync_WithASingleMessage_ShouldReturnTheSerialFromTheAck()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync("name", "data");
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1")));

            var result = await publish;
            result.IsSuccess.Should().BeTrue();
            result.Value.Serials.Should().Equal("s1");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "PBR2a")]
        public async Task PublishAsync_WithABatchOfMessages_ShouldReturnOneSerialPerMessageIncludingNulls()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync(new[] { new Message("a", "1"), new Message("b", "2"), new Message("c", "3") });
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1", null, "s3")));

            var result = await publish;
            result.IsSuccess.Should().BeTrue();
            result.Value.Serials.Should().Equal("s1", null, "s3");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WithAMessageObject_ShouldReturnTheSerialFromTheAck()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync(new Message("name", "data"));
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1")));

            (await publish).Value.Serials.Should().Equal("s1");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "TR4s")]
        public async Task PublishAsync_WithIncrementingMsgSerial_WhenAckedOutOfOrder_ShouldDeliverTheLaterPublishItsOwnSerial()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            LastCreatedTransport.SentMessages.Select(x => x.Original.MsgSerial).Should().Equal(0L, 1L);

            // Acknowledge out of order: the later publish first.
            client.FakeProtocolMessageReceived(Ack(1, 1, Serials("s2")));
            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1")));

            // The first ACK, with msgSerial 1, also implicitly acknowledges serial 0 (no result for it).
            (await first).IsSuccess.Should().BeTrue();
            (await second).Value.Serials.Should().Equal("s2");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "TR4s")]
        public async Task PublishAsync_WithIncrementingMsgSerial_WhenAckedInOrder_ShouldMatchEachAckToItsOwnPublish()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            LastCreatedTransport.SentMessages.Select(x => x.Original.MsgSerial).Should().Equal(0L, 1L);

            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1")));
            client.FakeProtocolMessageReceived(Ack(1, 1, Serials("s2")));

            (await first).Value.Serials.Should().Equal("s1");
            (await second).Value.Serials.Should().Equal("s2");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "TR4s")]
        public async Task PublishAsync_WhenOneAckSpansSeveralProtocolMessages_ShouldDeliverTheResultAtEachPosition()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            client.FakeProtocolMessageReceived(Ack(0, 2, Serials("s1"), Serials("s2")));

            (await first).Value.Serials.Should().Equal("s1");
            (await second).Value.Serials.Should().Equal("s2");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenTheAckHasNoRes_ShouldSucceedWithANullValue()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync("name", "data");
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, 1));

            var result = await publish;
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenResIsShorterThanTheAcknowledgedCount_ShouldSucceedWithANullValueForTheMissingPosition()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            client.FakeProtocolMessageReceived(Ack(0, 2, Serials("s1")));

            (await first).Value.Serials.Should().Equal("s1");
            var secondResult = await second;
            secondResult.IsSuccess.Should().BeTrue();
            secondResult.Value.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenAnEarlierPublishIsAckedBySerialBeforeTheAckRange_ShouldSucceedWithANullValue()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            // msgSerial 1 acknowledges serial 0 as well, but serial 0 lies before the range res describes.
            client.FakeProtocolMessageReceived(Ack(1, 1, Serials("s2")));

            var firstResult = await first;
            firstResult.IsSuccess.Should().BeTrue();
            firstResult.Value.Should().BeNull();
            (await second).Value.Serials.Should().Equal("s2");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        [Trait("spec", "RTN7a")]
        public async Task PublishAsync_WhenTheServerSendsANack_ShouldReturnAFailedResultWithTheError()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync("name", "data");
            await client.ProcessCommands();
            var error = new ErrorInfo("rejected", 40160);
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 1, Error = error });

            var result = await publish;
            result.IsFailure.Should().BeTrue();
            result.Error.Should().BeSameAs(error);
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenTheServerSendsANackWithoutAnError_ShouldReturnAFailedResultWithAnUnknownReason()
        {
            var (client, channel) = await GetAttachedChannel();

            var publish = channel.PublishAsync("name", "data");
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 1 });

            var result = await publish;
            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenTheAckNeverArrives_ShouldReturnAFailedResultRatherThanThrowing()
        {
            var (client, channel) = await GetAttachedChannel(o => o.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200));

            var result = await channel.PublishAsync("name", "data");

            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("timeout");
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task PublishAsync_WhenTheClientIdIsRejected_ShouldReturnAFailedResult()
        {
            var (client, channel) = await GetAttachedChannel();

            var result = await channel.PublishAsync(new Message("name", "data") { ClientId = "someone-else" });

            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task Publish_WithACallback_ShouldStillCallBackWithSuccessAndNoError()
        {
            var (client, channel) = await GetAttachedChannel();
            var calls = new List<(bool Success, ErrorInfo Error)>();

            channel.Publish("name", "data", (success, error) => calls.Add((success, error)));
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(Ack(0, 1, Serials("s1")));
            await client.ProcessCommands();

            calls.Should().Equal((true, (ErrorInfo)null));
        }

        [Fact]
        [Trait("spec", "RTL6j")]
        public async Task Publish_WithACallback_WhenNacked_ShouldCallBackWithFailureAndTheError()
        {
            var (client, channel) = await GetAttachedChannel();
            var calls = new List<(bool Success, ErrorInfo Error)>();
            var error = new ErrorInfo("rejected", 40160);

            channel.Publish("name", "data", (success, e) => calls.Add((success, e)));
            await client.ProcessCommands();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 1, Error = error });
            await client.ProcessCommands();

            calls.Should().HaveCount(1);
            calls[0].Success.Should().BeFalse();
            calls[0].Error.Should().BeSameAs(error);
        }
    }
}
