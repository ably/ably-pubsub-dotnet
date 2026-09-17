using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Transport.States.Connection
{
    internal class ConnectionConnectedState : ConnectionStateBase
    {
        public ConnectionConnectedState(
                                        IConnectionContext context,
                                        ErrorInfo error = null,
                                        bool isUpdate = false,
                                        ILogger logger = null)
                                        : base(context, logger)
        {
            Error = error;
            IsUpdate = isUpdate;
        }

        public override ConnectionState State => ConnectionState.Connected;

        public override bool CanSend => true;

        public override void Close()
        {
            Context.ExecuteCommand(SetClosingStateCommand.Create().TriggeredBy("ConnectedState.Close()"));
        }

        public override async Task<bool> OnMessageReceived(ProtocolMessage message, RealtimeState state)
        {
            switch (message.Action)
            {
                case ProtocolMessage.MessageAction.Auth:
                    Context.ExecuteCommand(RetryAuthCommand.Create(false).TriggeredBy("ConnectedState.OnMessageReceived()"));
                    return true;
                case ProtocolMessage.MessageAction.Connected:
                    Context.ExecuteCommand(SetConnectedStateCommand.Create(message, isUpdate: true).TriggeredBy("ConnectedState.OnMessageReceived()"));
                    return true;
                case ProtocolMessage.MessageAction.Close:
                    Context.ExecuteCommand(SetClosedStateCommand.Create(message.Error).TriggeredBy("ConnectedState.OnMessageReceived()"));
                    return true;
                case ProtocolMessage.MessageAction.Disconnected:
                    if (message.Error?.IsTokenError ?? false)
                    {
                        if (Context.ShouldWeRenewToken(message.Error, state))
                        {
                            Context.ExecuteCommand(RetryAuthCommand.Create(message.Error, true).TriggeredBy("ConnectedState.OnMessageReceived()"));
                        }
                        else
                        {
                            Context.ExecuteCommand(SetFailedStateCommand.Create(message.Error).TriggeredBy("ConnectedState.OnMessageReceived()"));
                        }

                        return true;
                    }

                    Context.ExecuteCommand(SetDisconnectedStateCommand.Create(message.Error).TriggeredBy("ConnectedState.OnMessageReceived()"));
                    return true;
                case ProtocolMessage.MessageAction.Error:
                    // an error message may signify an error state in the connection or in a channel
                    // Only handle connection errors here.
                    if (message.Channel.IsEmpty())
                    {
                        Context.ExecuteCommand(SetFailedStateCommand.Create(message.Error).TriggeredBy("ConnectedState.OnMessageReceived()"));
                    }

                    return true;
            }

            return false;
        }
    }
}
