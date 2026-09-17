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
            var retryInterval = Context.RetryTimeout.TotalMilliseconds;
            var noOfAttempts = Context.Connection.RealtimeClient?.State?.AttemptsInfo?.NumberOfAttempts ?? 0 + 1; // First attempt should start with 1 instead of 0.
            var retryIn = TimeSpan.FromMilliseconds(ReconnectionStrategy.GetRetryTime(retryInterval, noOfAttempts));

            if (RetryInstantly == false)
            {
                _timer.Start(retryIn, OnTimeOut);
            }
        }

        private void OnTimeOut()
        {
            Context.ExecuteCommand(SetConnectingStateCommand.Create().TriggeredBy("DisconnectedState.OnTimeOut()"));
        }
    }
}
