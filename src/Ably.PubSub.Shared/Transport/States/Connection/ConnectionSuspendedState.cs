using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionSuspendedState : ConnectionStateBase
    {
        private readonly ICountdownTimer _timer;

        public override ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonSuspended;

        public ConnectionSuspendedState(IConnectionContext context, ILogger logger)
            : this(context, null, new CountdownTimer("Suspended state timer", logger), logger)
        {
        }

        public ConnectionSuspendedState(IConnectionContext context, ErrorInfo error, ILogger logger)
            : this(context, error, new CountdownTimer("Suspended state timer", logger), logger)
        {
        }

        public ConnectionSuspendedState(IConnectionContext context, ErrorInfo error, ICountdownTimer timer, ILogger logger)
            : base(context, logger)
        {
            _timer = timer;
            Error = error ?? ErrorInfo.ReasonSuspended;
            RetryIn = context.SuspendRetryTimeout;
        }

        public override ConnectionState State => ConnectionState.Suspended;

        public override RealtimeCommand Connect()
        {
            return SetConnectingStateCommand.Create().TriggeredBy("SuspendedState.Connect()");
        }

        public override void Close()
        {
            _timer.Abort();
            Context.ExecuteCommand(SetClosedStateCommand.Create().TriggeredBy("SuspendedState.Close()"));
        }

        public override void AbortTimer()
        {
            _timer.Abort();
        }

        public override void StartTimer()
        {
            if (RetryIn.HasValue)
            {
                _timer.Start(RetryIn.Value, OnTimeOut);
            }
        }

        private void OnTimeOut()
        {
            Context.ExecuteCommand(SetConnectingStateCommand.Create().TriggeredBy("SuspendedState.OnTimeOut()"));
        }
    }
}
