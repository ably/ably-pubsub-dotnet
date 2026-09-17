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
    public class FailedStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionFailedState _state;

        public FailedStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _context = new FakeConnectionContext();
            _state = GetState();
        }

        [Fact]
        public void ShouldHaveCorrectState()
        {
            // Arrange
            _state.State.Should().Be(ConnectionState.Failed);
        }

        [Fact]
        public void ConnectCalled_ShouldGoToConnecting()
        {
            // Act
            var command = _state.Connect();

            // Assert
            command.Should().BeOfType<SetConnectingStateCommand>();
        }

        [Fact]
        public void Close_ShouldDoNothing()
        {
            // Act
            _state.Close();
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
        public async Task ShouldNotHandleInboundMessages(ProtocolMessage.MessageAction action)
        {
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            result.Should().BeFalse();
        }

        private ConnectionFailedState GetState(ErrorInfo info = null)
        {
            return new ConnectionFailedState(_context, info, Logger);
        }
    }
}
