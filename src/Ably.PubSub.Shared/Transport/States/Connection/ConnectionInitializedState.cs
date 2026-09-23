using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionInitializedState : ConnectionStateBase
    {
        public ConnectionInitializedState(IConnectionContext context, ILogger logger)
            : base(context, logger)
        { }

        public override bool CanQueue => true;

        public override ConnectionState State => ConnectionState.Initialized;

        public override RealtimeCommand Connect()
        {
            return SetConnectingStateCommand.Create().TriggeredBy("InitializedState.Connect()");
        }

        public override void AbortTimer()
        {
        }
    }
}
