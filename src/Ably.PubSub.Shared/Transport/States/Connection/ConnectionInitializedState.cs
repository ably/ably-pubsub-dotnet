using Ably.PubSub.Realtime;
using Ably.PubSub.Realtime.Workflow;

namespace Ably.PubSub.Transport.States.Connection
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
