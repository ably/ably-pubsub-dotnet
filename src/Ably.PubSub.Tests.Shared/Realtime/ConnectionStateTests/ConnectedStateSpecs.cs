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
    public class ConnectedStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionConnectedState _state;

        public ConnectedStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _context = new FakeConnectionContext();
            _state = GetState();
        }

        [Fact]
        public void ConnectedState_CorrectState()
        {
            // Assert
            _state.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [InlineData(ProtocolMessage.MessageAction.Ack)]
        [InlineData(ProtocolMessage.MessageAction.Attach)]
        [InlineData(ProtocolMessage.MessageAction.Attached)]
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
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            result.Should().BeFalse();
            _context.ShouldHaveNotChangedState();
        }

        [Fact]
        public async Task ShouldHandleInboundDisconnectedMessageAndShouldQueueSetDisconnectedStateCommand()
        {
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected), null);

            // Assert
            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetDisconnectedStateCommand>();
        }

        [Fact]
        public async Task ShouldHandlesInboundErrorMessageAndGoToFailedState()
        {
            ErrorInfo targetError = new ErrorInfo("test", 123);

            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = targetError }, null);

            // Assert
            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetFailedStateCommand>(cmd => cmd.Error.Should().BeEquivalentTo(targetError));
        }

        [Fact]
        public void WhenConnectCalled_ShouldDoNothing()
        {
            // Act
            _state.Connect();

            // Assert
            _context.ShouldHaveNotChangedState();
        }

        [Fact]
        [Trait("spec", "RTN12a")]
        public void WhenCloseCalled_ShouldChangeStateToClosing()
        {
            // Act
            _state.Close();

            // Assert
            _context.ShouldQueueCommand<SetClosingStateCommand>();
        }

        [Fact]
        [Trait("spec", "RTN12c")]
        public async Task WhenCloseMessageReceived_ShouldChangeStateToClosed()
        {
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Close), null);

            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetClosedStateCommand>();
        }

        private ConnectionConnectedState GetState()
        {
            return new ConnectionConnectedState(_context);
        }
    }
}
