using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        private List<ProtocolMessage> SentMessageFrames() =>
            LastCreatedTransport.SentMessages
                .Select(x => x.Original)
                .Where(x => x.Action == ProtocolMessage.MessageAction.Message)
                .ToList();

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

            // The first ACK, with msgSerial 1, skips past serial 0, which therefore fails; the later ACK
            // for serial 0 relates to a message that is no longer pending and is ignored.
            (await first).IsFailure.Should().BeTrue();
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
        public async Task PublishAsync_WhenAnEarlierPublishLiesBelowTheAckRange_ShouldFailWithTheUnknownError()
        {
            var (client, channel) = await GetAttachedChannel();

            var first = channel.PublishAsync("one", "1");
            await client.ProcessCommands();
            var second = channel.PublishAsync("two", "2");
            await client.ProcessCommands();

            // The ACK covers only serial 1; serial 0 lies below its range, so it counts as NACKed.
            client.FakeProtocolMessageReceived(Ack(1, 1, Serials("s2")));

            var firstResult = await first;
            firstResult.IsFailure.Should().BeTrue();
            firstResult.Error.Code.Should().Be(ErrorInfo.ReasonUnknown.Code);
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

        [Theory]
        [InlineData(ChannelState.Suspended)]
        [InlineData(ChannelState.Failed)]
        [Trait("spec", "RTL6c4")]
        public async Task PublishAsync_InAChannelStateWhichRefusesPublishing_ShouldThrowTheRefusalErrorUnwrapped(ChannelState state)
        {
            var (client, channel) = await GetAttachedChannel();
            ((RealtimeChannel)channel).SetChannelState(state);

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync("name", "data"));
            await client.ProcessCommands();

            exception.ErrorInfo.Code.Should().Be(ErrorCodes.BadRequest);
            exception.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            exception.ErrorInfo.Message.Should().Be($"Unable to publish in {state} state");
            exception.InnerException.Should().BeNull();
            SentMessageFrames().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTL6c2")]
        [Trait("spec", "RTL6c4")]
        public async Task PublishAsync_WhenTheConnectionCannotQueueTheMessage_ShouldThrowTheRefusalErrorUnwrapped()
        {
            var client = GetClientWithFakeTransport(o =>
            {
                o.AutoConnect = false;
                o.QueueMessages = false;
            });
            var channel = client.Channels.Get(TestChannelName);
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync(new Message("name", "data")));

            client.Connection.State.Should().Be(ConnectionState.Initialized);
            exception.ErrorInfo.Code.Should().Be(ErrorCodes.BadRequest);
            exception.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            exception.ErrorInfo.Message.Should().Contain("connection is in Initialized state");
            exception.InnerException.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RTL6c4")]
        public async Task PublishAsync_WhenAnUnexpectedExceptionIsThrown_ShouldStillWrapItAsAnInternalError()
        {
            var (_, channel) = await GetAttachedChannel();

            var exception = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync((IEnumerable<Message>)null));

            exception.ErrorInfo.Code.Should().Be(ErrorCodes.InternalError);
            exception.ErrorInfo.Message.Should().StartWith("Unexpected error :");
            exception.InnerException.Should().BeOfType<ArgumentNullException>();
        }
    }
}
