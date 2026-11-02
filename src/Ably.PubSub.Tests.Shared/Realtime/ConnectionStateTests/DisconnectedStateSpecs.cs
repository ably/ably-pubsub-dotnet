using System;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Realtime;
using Ably.PubSub.Realtime.Workflow;
using Ably.PubSub.Transport;
using Ably.PubSub.Transport.States.Connection;
using Ably.PubSub.Types;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests
{
    public class DisconnectedStateSpecs : AblySpecs
    {
        private readonly FakeConnectionContext _context;
        private readonly ConnectionDisconnectedState _state;
        private readonly FakeTimer _timer;

        public DisconnectedStateSpecs(ITestOutputHelper output)
            : base(output)
        {
            _context = new FakeConnectionContext();
            _timer = new FakeTimer();
            _state = GetState();
        }

        [Fact]
        public void ShouldHaveDisconnectedTypes()
        {
            _state.State.Should().Be(ConnectionState.Disconnected);
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
            // Arrange
            var state = GetState(ErrorInfo.ReasonClosed);

            // Act
            bool handled = await state.OnMessageReceived(new ProtocolMessage(action), null);

            // Assert
            handled.Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "RTN12d")]
        public void WhenCloseCalled_ShouldTransitionToClosedAndTimerAborted()
        {
            // Arrange
            var state = GetState(ErrorInfo.ReasonClosed);

            // Act
            state.Close();

            // Assert
            _context.ShouldQueueCommand<SetClosedStateCommand>();
            _timer.Aborted.Should().BeTrue();
        }

        [Fact]
        public void WhenConnectCalled_ShouldTransitionToConnecting()
        {
            // Arrange
            var state = GetState(ErrorInfo.ReasonClosed);

            // Act
            var command = state.Connect();

            // Assert
            command.Should().BeOfType<SetConnectingStateCommand>();
        }

        [Fact]
        public async Task AfterAnInterval_ShouldRetryConnection()
        {
            // Arrange
            var transport = new FakeTransport { State = TransportState.Initialized };
            _context.Transport = transport;
            var state = GetState(ErrorInfo.ReasonClosed);

            // Act
            state.StartTimer();
            _timer.OnTimeOut();

            // Assert
            _timer.StartedWithAction.Should().BeTrue();
            _context.ShouldQueueCommand<SetConnectingStateCommand>();
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        public void StartTimer_ShouldReportTheDelayItActuallyWaits()
        {
            // RTN14d - retryIn is "the time in milliseconds until the next connection attempt", so
            // the value handed to the application is the one the timer was started with, RTB1 backoff
            // and jitter included.
            _state.StartTimer();

            _timer.LastDelay.Should().BeGreaterThan(TimeSpan.Zero);
            _state.RetryIn.Should().Be(_timer.LastDelay);
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        [Trait("spec", "RTB1")]
        public void StartTimer_ShouldApplyTheBackoffAndJitter()
        {
            _state.StartTimer();

            // RTB1a's coefficient is 1 for the first retry and RTB1b's jitter is 0.8 to 1.0.
            var nominal = _context.RetryTimeout;
            _timer.LastDelay.Should().BeGreaterOrEqualTo(TimeSpan.FromMilliseconds(nominal.TotalMilliseconds * 0.8));
            _timer.LastDelay.Should().BeLessOrEqualTo(nominal);
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        public void WhenRetryingInstantly_ShouldReportNoWaitAndNotStartTheTimer()
        {
            var state = GetState();
            state.RetryInstantly = true;

            state.StartTimer();

            state.RetryIn.Should().Be(TimeSpan.Zero);
            _timer.StartedWithAction.Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        [Trait("spec", "RTN14e")]
        public void StartTimer_WhenTheBackoffWouldOvershootTheStateTtl_ShouldWakeAtTheDeadline()
        {
            // RTN14e requires SUSPENDED once connectionStateTtl has elapsed, and that is decided when
            // an attempt fails. Sleeping the whole RTB1 backoff past the deadline would postpone
            // SUSPENDED by however long we slept - unbounded, since disconnectedRetryTimeout is a
            // client option - so the wait is cut back to what is left of the ttl.
            var now = TestHelpers.Now();
            _context.RetryTimeout = TimeSpan.FromSeconds(30);

            using (var client = NewClientWithFirstAttemptAt(now.AddSeconds(-119), TimeSpan.FromSeconds(120)))
            {
                _context.Connection = new Connection(client, () => now);
                var state = GetState();

                state.StartTimer();

                // One second of the ttl is left, and the backoff would have waited at least 24.
                _timer.LastDelay.Should().Be(TimeSpan.FromSeconds(1));
                state.RetryIn.Should().Be(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        [Trait("spec", "RTN14e")]
        public void StartTimer_WhenTheClockHasSteppedBack_ShouldNotWaitBeyondTheTtl()
        {
            // A backwards clock step leaves the first attempt in the future, making the elapsed time
            // negative. Unclamped that lengthens the remaining ttl rather than shortening it, so the
            // deadline is missed by however far the clock moved.
            var now = TestHelpers.Now();
            _context.RetryTimeout = TimeSpan.FromSeconds(30);

            using (var client = NewClientWithFirstAttemptAt(now.AddMinutes(5), TimeSpan.FromSeconds(1)))
            {
                _context.Connection = new Connection(client, () => now);
                var state = GetState();

                state.StartTimer();

                // Elapsed treated as zero, so the whole ttl remains - not the ttl plus five minutes,
                // which would exceed the backoff and leave nothing clamped at all.
                _timer.LastDelay.Should().Be(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        public void StartTimer_WhenTheDeadlineIsBeyondTheBackoff_ShouldWaitTheBackoffNotTheDeadline()
        {
            // The clamp may only ever shorten the wait. The deadline is deliberately just past the
            // backoff here - 1.2s against a jittered 0.8-1.0s - so waiting until the deadline would
            // overshoot RTB1 rather than being indistinguishable from it.
            var now = TestHelpers.Now();
            _context.RetryTimeout = TimeSpan.FromSeconds(1);

            using (var client = NewClientWithFirstAttemptAt(now.AddSeconds(-10), TimeSpan.FromMilliseconds(11200)))
            {
                _context.Connection = new Connection(client, () => now);
                var state = GetState();

                state.StartTimer();

                // 1.2s of ttl remains, so an unconditional clamp would wait that. RTB1's jitter caps
                // the legitimate answer at the nominal second.
                _timer.LastDelay.Should().BeLessOrEqualTo(TimeSpan.FromSeconds(1));
                _timer.LastDelay.Should().BeGreaterOrEqualTo(TimeSpan.FromMilliseconds(800));
            }
        }

        /// <summary>
        /// A client whose RealtimeState carries an attempt history and a connectionStateTtl, which is
        /// what ClampToStateTtl reads. The shared FakeConnectionContext has no client at all, so every
        /// other test in this class takes that method's null-state early return.
        /// </summary>
        private PubSubRealtimeClient NewClientWithFirstAttemptAt(DateTimeOffset firstAttempt, TimeSpan connectionStateTtl)
        {
            var client = new PubSubRealtimeClient(new ClientOptions(ValidKey) { AutoConnect = false });
            client.State.Connection.ConnectionStateTtl = connectionStateTtl;
            client.State.AttemptsInfo.Attempts.Add(new ConnectionAttempt(firstAttempt));
            return client;
        }

        private ConnectionDisconnectedState GetState(ErrorInfo error = null)
        {
            return new ConnectionDisconnectedState(_context, error, _timer, Logger);
        }
    }
}
