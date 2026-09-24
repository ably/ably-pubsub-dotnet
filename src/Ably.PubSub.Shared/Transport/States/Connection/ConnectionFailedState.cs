using Ably.PubSub.Realtime;
using Ably.PubSub.Realtime.Workflow;

namespace Ably.PubSub.Transport.States.Connection
{
    internal class ConnectionFailedState : ConnectionStateBase
    {
        public override ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonFailed;

        public ConnectionFailedState(IConnectionContext context, ErrorInfo error, ILogger logger)
            : base(context, logger)
        {
            Error = error ?? ErrorInfo.ReasonFailed;
        }

        public override ConnectionState State => ConnectionState.Failed;

        public override RealtimeCommand Connect()
        {
            return SetConnectingStateCommand.Create();
        }
    }
}
