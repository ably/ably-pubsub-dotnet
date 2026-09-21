using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionClosedState : ConnectionStateBase
    {
        public override ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonClosed;

        public ConnectionClosedState(IConnectionContext context, ILogger logger)
            : this(context, null, logger)
        {
        }

        public ConnectionClosedState(IConnectionContext context, ErrorInfo error, ILogger logger)
            : base(context, logger)
        {
            Error = error ?? ErrorInfo.ReasonClosed;
        }

        public override ConnectionState State => ConnectionState.Closed;

        public override RealtimeCommand Connect()
        {
            return SetConnectingStateCommand.Create();
        }
    }
}
