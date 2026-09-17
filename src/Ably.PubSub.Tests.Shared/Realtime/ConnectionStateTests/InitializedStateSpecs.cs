using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class InitializedStateSpecs : AblySpecs
    {
        private readonly ConnectionInitializedState _state;

        public InitializedStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            var context = new FakeConnectionContext();
            _state = new ConnectionInitializedState(context, Logger);
        }

        [Fact]
        public void InitializedState_CorrectState()
        {
            // Assert
            _state.State.Should().Be(ConnectionState.Initialized);
        }

        [Theory]
        [InlineData(ProtocolMessage.MessageAction.Ack)]
        [InlineData(ProtocolMessage.MessageAction.Attach)]
        [InlineData(ProtocolMessage.MessageAction.Attached)]
        [InlineData(ProtocolMessage.MessageAction.Close)]
        [InlineData(ProtocolMessage.MessageAction.Closed)]
        [InlineData(ProtocolMessage.MessageAction.Connect)]
        [InlineData(ProtocolMessage.MessageAction.Connected)]
        [InlineData(ProtocolMessage.MessageAction.Detach)]
        [InlineData(ProtocolMessage.MessageAction.Detached)]
        [InlineData(ProtocolMessage.MessageAction.Disconnect)]
        [InlineData(ProtocolMessage.MessageAction.Disconnected)]
        [InlineData(ProtocolMessage.MessageAction.Error)]
        [InlineData(ProtocolMessage.MessageAction.Heartbeat)]
        [InlineData(ProtocolMessage.MessageAction.Message)]
        [InlineData(ProtocolMessage.MessageAction.Nack)]
        [InlineData(ProtocolMessage.MessageAction.Presence)]
        [InlineData(ProtocolMessage.MessageAction.Sync)]
        public async Task ShouldNotHandleInboundMessageAction(ProtocolMessage.MessageAction action)
        {
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void CloseShouldDoNothing()
        {
            // Act
            _state.Close();
        }

        [Fact]
        public void OnConnect_ShouldGoToConnectionState()
        {
            // Act
            var command = _state.Connect();

            // Assert
            command.Should().BeOfType<SetConnectingStateCommand>();
        }
    }
}
