using System;
using System.Linq;
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
    public class SuspendedStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionSuspendedState _state;
        private readonly FakeTimer _timer;

        private ConnectionSuspendedState GetState(ErrorInfo info = null)
        {
            return new ConnectionSuspendedState(_context, info, _timer, Logger);
        }

        public SuspendedStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _timer = new FakeTimer();
            _context = new FakeConnectionContext();
            _state = GetState();
        }

        private void LastCommandShouldBe<T>(Action<T> assert = null)
            where T : RealtimeCommand
        {
            var lastCommand = _context.ExecutedCommands.Last();
            lastCommand.Should().BeOfType<T>();
            assert?.Invoke(lastCommand as T);
        }

        [Fact]
        public void ShouldHaveSuspendedState()
        {
            _state.State.Should().Be(ConnectionState.Suspended);
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
        public async Task ShouldIgnoreInboundMessages(ProtocolMessage.MessageAction action)
        {
            // Act
            var result = await _state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "RTN12d")]
        public void Close_ChangesStateToClosedAndAbortsTimer()
        {
            // Act
            _state.Close();

            // Assert
            LastCommandShouldBe<SetClosedStateCommand>();
            _timer.Aborted.Should().BeTrue();
        }

        [Fact]
        public void Connect_ShouldChangeStateToConnecting()
        {
            // Act
            var command = _state.Connect();

            // Assert
            command.Should().BeOfType<SetConnectingStateCommand>();
        }

        [Fact]
        [Trait("spec", "RTN14e")]
        public async Task ShouldRetryConnection()
        {
            _context.Transport = new FakeTransport(TransportState.Initialized);

            // Act
            _state.StartTimer();
            _timer.StartedWithAction.Should().BeTrue();
            _timer.OnTimeOut();

            // Assert
            _context.ShouldQueueCommand<SetConnectingStateCommand>();
        }
    }
}
