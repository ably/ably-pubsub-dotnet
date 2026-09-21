using System;
using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionClosingState : ConnectionStateBase
    {
        public override ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonClosed;

        private readonly bool _connectedTransport;
        private readonly ICountdownTimer _timer;

        public ConnectionClosingState(IConnectionContext context, bool connectedTransport, ILogger logger)
            : this(context, null, connectedTransport, new CountdownTimer("Closing state timer", logger), logger)
        {
        }

        public ConnectionClosingState(IConnectionContext context, ErrorInfo error, bool connectedTransport, ICountdownTimer timer, ILogger logger)
            : base(context, logger)
        {
            _connectedTransport = connectedTransport;
            _timer = timer;
            Error = error ?? ErrorInfo.ReasonClosed;
        }

        public override ConnectionState State => ConnectionState.Closing;

        public override Task<bool> OnMessageReceived(ProtocolMessage message, RealtimeState state)
        {
            switch (message.Action)
            {
                case ProtocolMessage.MessageAction.Closed:
                    TransitionState(SetClosedStateCommand.Create().TriggeredBy("ClosingState.OnMessageReceived()"));
                    return Task.FromResult(true);
                case ProtocolMessage.MessageAction.Disconnected:
                    TransitionState(SetDisconnectedStateCommand.Create(message.Error).TriggeredBy("ClosingState.OnMessageReceived()"));
                    return Task.FromResult(true);
                case ProtocolMessage.MessageAction.Error:
                    TransitionState(SetFailedStateCommand.Create(message.Error).TriggeredBy("ClosingState.OnMessageReceived()"));
                    return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        private void TransitionState(RealtimeCommand command)
        {
            _timer.Abort();
            Context.ExecuteCommand(command);
        }

        public override void AbortTimer()
        {
            _timer.Abort();
        }

        public override void StartTimer()
        {
            if (_connectedTransport)
            {
                // RTN12b - the wait for the CLOSED message is realtimeRequestTimeout, which TO3l11
                // makes a client option.
                _timer.Start(Context.DefaultTimeout, OnTimeOut);
            }
        }

        private void OnTimeOut()
        {
            Context.ExecuteCommand(SetClosedStateCommand.Create().TriggeredBy("ClosingState.OnTimeOut()"));
        }

        public override RealtimeCommand Connect()
        {
            // No key to clear for RTN11b's clean connection: entering CLOSING already ran
            // ClearKeyAndId for RTN8d and RTN9d, and the single reader processed that before it can
            // reach this. The following CONNECTED therefore finds no id to match and restarts the
            // serial sequence under RTN15c7.
            _timer.Abort();
            return SetConnectingStateCommand.Create().TriggeredBy("ClosingState.Connect()");
        }
    }
}
