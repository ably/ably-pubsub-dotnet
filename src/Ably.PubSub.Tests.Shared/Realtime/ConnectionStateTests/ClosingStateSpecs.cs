using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class ClosingStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionClosingState _state;
        private readonly FakeTimer _timer;

        public ClosingStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _timer = new FakeTimer();
            _context = new FakeConnectionContext();
            _state = GetState();
        }

        [Fact]
        public void ShouldHaveClosingState()
        {
            _state.State.Should().Be(ConnectionState.Closing);
        }

        [Fact]
        public void CloseCalled_ShouldDoNothing()
        {
            // Act
            _state.Close();
        }

        [Theory]
        [InlineData(ProtocolMessage.MessageAction.Ack)]
        [InlineData(ProtocolMessage.MessageAction.Attach)]
        [InlineData(ProtocolMessage.MessageAction.Attached)]
        [InlineData(ProtocolMessage.MessageAction.Close)]
        [InlineData(ProtocolMessage.MessageAction.Connect)]
        [InlineData(ProtocolMessage.MessageAction.Connected)]
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
        }

        [Fact]
        public async Task ShouldHandleInboundClosedMessageAndMoveToClosed()
        {
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Closed), null);

            // Assert
            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetClosedStateCommand>();
        }

        [Fact]
        public async Task ShouldHandleInboundErrorMessageAndMoveToFailedState()
        {
            ErrorInfo targetError = new ErrorInfo("test", 123);

            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = targetError }, null);

            // Assert
            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetFailedStateCommand>(cmd => cmd.Error.Should().BeEquivalentTo(targetError));
        }

        [Fact]
        public async Task ShouldHandleInboundDisconnectedMessageAndGoToDisconnectedState()
        {
            // Act
            bool result = await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected), null);

            // Assert
            result.Should().BeTrue();
            _context.ShouldQueueCommand<SetDisconnectedStateCommand>();
        }

        [Fact]
        [Trait("spec", "RTN12b")]
        public async Task AfterTimeoutExpires_ShouldForceStateToClosed()
        {
            var state = GetState(connectedTransport: true);
            state.StartTimer();

            _timer.StartedWithAction.Should().BeTrue();
            _timer.OnTimeOut();

            _context.ShouldQueueCommand<SetClosedStateCommand>();
        }

        [Fact]
        [Trait("spec", "RTN12a")]
        public async Task WhenClosedMessageReceived_ShouldAbortTimerAndMoveToClosedState()
        {
            // Arrange
            _context.Transport = new FakeTransport(TransportState.Connected);

            // Act
            _state.StartTimer();
            await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Closed), null);

            // Assert
            _timer.StartedWithAction.Should().BeTrue();
            _timer.Aborted.Should().BeTrue();
            _context.ShouldQueueCommand<SetClosedStateCommand>();
        }

        [Fact]
        public async Task OnErrorReceived_TimerIsAbortedAndStateIsFailedState()
        {
            // Arrange
            _context.Transport = new FakeTransport(TransportState.Connected);

            // Act
            _state.StartTimer();
            await _state.OnMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error), null);

            // Assert
            _timer.StartedWithAction.Should().BeTrue();
            _timer.Aborted.Should().BeTrue();
            _context.ShouldQueueCommand<SetFailedStateCommand>();
        }

        private ConnectionClosingState GetState(ErrorInfo info = null, bool connectedTransport = true)
        {
            return new ConnectionClosingState(_context, info, connectedTransport, _timer, Logger);
        }
    }
}
