using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class ConnectingStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionConnectingState _state;
        private readonly FakeTimer _timer;

        public ConnectingStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _context = new FakeConnectionContext();
            _timer = new FakeTimer();
            _state = new ConnectionConnectingState(_context, _timer, Logger);
        }

        [Fact]
        public void HasCorrectState()
        {
            _state.State.Should().Be(Ably.Realtime.ConnectionState.Connecting);
        }

        [Theory]
        [InlineData(ProtocolMessage.MessageAction.Ack)]
        [InlineData(ProtocolMessage.MessageAction.Attach)]
        [InlineData(ProtocolMessage.MessageAction.Attached)]
        [InlineData(ProtocolMessage.MessageAction.Close)]
        [InlineData(ProtocolMessage.MessageAction.Closed)]
        [InlineData(ProtocolMessage.MessageAction.Connect)]
        [InlineData(ProtocolMessage.MessageAction.Detach)]
        [InlineData(ProtocolMessage.MessageAction.Detached)]
        [InlineData(ProtocolMessage.MessageAction.Disconnect)]
        [InlineData(ProtocolMessage.MessageAction.Heartbeat)]
        [InlineData(ProtocolMessage.MessageAction.Message)]
        [InlineData(ProtocolMessage.MessageAction.Nack)]
        [InlineData(ProtocolMessage.MessageAction.Presence)]
        [InlineData(ProtocolMessage.MessageAction.Sync)]
        public async Task ShouldNotHandleInboundMessageAction(ProtocolMessage.MessageAction action)
        {
            bool result = await _state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ShouldHandleInboundConnectedMessage()
        {
            _context.Transport = new FakeTransport { State = TransportState.Connecting };

            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected), null);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task WithInboundConnectedMessageAndClosingTransport_ShouldNotGoToConnected()
        {
            // Arrange
            _context.Transport = new FakeTransport { State = TransportState.Closing };

            // Act
            await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected), null);

            // Assert
            _context.LastSetState.Should().BeNull();
        }

        [Fact]
        public async Task WithConnectedTransportAndInboundConnectedMessage_ShouldGoToConnected()
        {
            // Arrange
            _context.Transport = GetConnectedTransport();

            // Act
            await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected), null);

            // Assert
            _context.ShouldQueueCommand<SetConnectedStateCommand>();
        }

        [Fact]
        public async Task ConnectingState_HandlesInboundErrorMessage()
        {
            // Arrange
            _context.Transport = GetConnectedTransport();

            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error), null);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void Connect_ShouldDoNothing()
        {
            // Act
            _state.Connect();
        }

        [Fact]
        public void Close_ShouldGoToClosing()
        {
            // Act
            _state.Close();

            // Assert
            _context.ShouldQueueCommand<SetClosingStateCommand>();
        }

        [Fact]
        public async Task ConnectingState_SendsHandleConnectionDisconnectedCommand()
        {
            // Act
            _state.StartTimer();
            _timer.OnTimeOut();

            // Assert
            _timer.StartedWithAction.Should().BeTrue();
            _context.ShouldQueueCommand<HandleConnectingDisconnectedCommand>();
        }

        private static FakeTransport GetConnectedTransport()
        {
            return new FakeTransport { State = TransportState.Connected };
        }
    }
}
