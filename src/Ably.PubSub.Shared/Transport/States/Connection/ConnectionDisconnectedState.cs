using System;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Shared.Utils;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionDisconnectedState : ConnectionStateBase
    {
        private readonly ICountdownTimer _timer;

        public ConnectionDisconnectedState(IConnectionContext context, ErrorInfo error, ILogger logger)
            : this(context, error, new CountdownTimer("Disconnected state timer", logger), logger)
        {
        }

        public ConnectionDisconnectedState(IConnectionContext context, ErrorInfo error, ICountdownTimer timer, ILogger logger)
            : base(context, logger)
        {
            _timer = timer;
            Error = error;
            RetryIn = context.RetryTimeout;
        }

        public override ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonDisconnected;

        public bool RetryInstantly { get; set; }

        public override ConnectionState State => ConnectionState.Disconnected;

        public override bool CanQueue => true;

        public override RealtimeCommand Connect()
        {
           return SetConnectingStateCommand.Create().TriggeredBy("DisconnectedState.Connect()");
        }

        public override void Close()
        {
            AbortTimer();
            Context.ExecuteCommand(SetClosedStateCommand.Create().TriggeredBy("DisconnectedState.Close()"));
        }

        public override void AbortTimer()
        {
            _timer.Abort();
        }

        // RTN14d
        public override void StartTimer()
        {
            if (RetryInstantly)
            {
                // RTN14d - there is no wait, so say so rather than advertising the nominal timeout.
                RetryIn = TimeSpan.Zero;
                return;
            }

            var state = Context.Connection.RealtimeClient?.State;
            var retryInterval = Context.RetryTimeout.TotalMilliseconds;

            // RTB1a's coefficient sequence is indexed from the first attempt, so a count of zero
            // would apply 2/3 where the first retry should get 1. On a drop straight out of
            // CONNECTED there is no recorded attempt, because entering CONNECTED cleared them.
            var noOfAttempts = Math.Max(state?.AttemptsInfo?.NumberOfAttempts ?? 0, 1);

            var retryIn = ClampToStateTtl(
                TimeSpan.FromMilliseconds(ReconnectionStrategy.GetRetryTime(retryInterval, noOfAttempts)),
                state);

            // RTN14d - retryIn must be "the time in milliseconds until the next connection attempt",
            // so report the delay we are about to wait rather than the nominal
            // disconnectedRetryTimeout, which ignores the RTB1 coefficient, its jitter and the clamp
            // above. SetState calls this before emitting, so this is what the application sees.
            RetryIn = retryIn;

            _timer.Start(retryIn, OnTimeOut);
        }

        /// <summary>
        /// RTN14e requires the move to SUSPENDED once the connection state ttl has elapsed, and that
        /// decision is taken when an attempt fails. Sleeping past the deadline would delay SUSPENDED
        /// by however long we slept, unbounded because disconnectedRetryTimeout is a client option.
        /// Waking at the deadline lets the attempt fail there and be converted, keeping one timer.
        /// </summary>
        private TimeSpan ClampToStateTtl(TimeSpan retryIn, RealtimeState state)
        {
            var firstAttempt = state?.AttemptsInfo?.FirstAttempt;
            if (firstAttempt.HasValue == false)
            {
                return retryIn;
            }

            var elapsed = Context.Connection.Now() - firstAttempt.Value;

            // Both operands clamped before subtracting: a backwards clock step makes elapsed
            // negative, and a ttl near TimeSpan.MaxValue then overflows. SetState calls StartTimer
            // inside a catch that only handles AblyException, so the throw would be dropped and the
            // whole transition abandoned - no DISCONNECTED, no transport, no timer.
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (state.Connection.ConnectionStateTtl >= TimeSpan.MaxValue - elapsed)
            {
                return retryIn;
            }

            var remaining = state.Connection.ConnectionStateTtl - elapsed;

            if (remaining <= TimeSpan.Zero || remaining >= retryIn)
            {
                // The deadline has passed, so the next attempt converts to SUSPENDED whenever it
                // happens, or it is further out than the backoff and there is nothing to clamp.
                return retryIn;
            }

            return remaining;
        }

        private void OnTimeOut()
        {
            Context.ExecuteCommand(SetConnectingStateCommand.Create().TriggeredBy("DisconnectedState.OnTimeOut()"));
        }
    }
}
