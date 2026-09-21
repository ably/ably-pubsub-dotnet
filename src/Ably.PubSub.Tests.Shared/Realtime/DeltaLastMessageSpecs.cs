using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("spec", "RTL19")]
    public class DeltaLastMessageSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTL19b")]
        public async Task WithStringMessage_ShouldKeepItWithoutProcessing()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);

            var successfulProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
            {
                Channel = channel.Name,
                Messages = new[] { new Message { Data = "test", Encoding = "custom" }, },
            };
            realtime.ExecuteCommand(ProcessMessageCommand.Create(successfulProtocolMessage));
            await realtime.ProcessCommands();

            var previousPayload = channel.MessageDecodingContext.PreviousPayload;
            previousPayload.Encoding.Should().Be("custom");
            previousPayload.StringData.Should().Be("test");
            previousPayload.ByteData.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RTL19b")]
        public async Task WithBinaryMessage_ShouldKeepItPreviousPayloadInBinaryForm()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);

            var binaryData = new byte[] { 112, 100, 111 };
            var successfulProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
            {
                Channel = channel.Name,
                Messages = new[] { new Message { Data = binaryData }, },
            };
            realtime.ExecuteCommand(ProcessMessageCommand.Create(successfulProtocolMessage));
            await realtime.ProcessCommands();

            var previousPayload = channel.MessageDecodingContext.PreviousPayload;
            previousPayload.StringData.Should().BeNull();
            previousPayload.ByteData.Should().Equal(binaryData);
        }

        [Fact]
        [Trait("spec", "RTL19a")]
        public async Task WithBase64Message_ShouldKeepBase64DecodedPayload()
        {
            var (realtime, c) = await GetClientAndChannel();
            RealtimeChannel channel = (RealtimeChannel)c;
            channel.SetChannelState(ChannelState.Attached);

            const string testString = "Testing, testing, testing";
            var successfulProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
            {
                Channel = channel.Name,
                Messages = new[] { new Message { Data = testString.ToBase64(), Encoding = "base64" }, },
            };
            realtime.ExecuteCommand(ProcessMessageCommand.Create(successfulProtocolMessage));

            await realtime.ProcessCommands();

            var previousPayload = channel.MessageDecodingContext.PreviousPayload;
            previousPayload.StringData.Should().BeNull();
            previousPayload.Encoding.Should().BeEmpty();
            previousPayload.ByteData.Should().Equal(testString.GetBytes());
        }

        public DeltaLastMessageSpecs(ITestOutputHelper output)
        : base(output)
        {
        }
    }
}
