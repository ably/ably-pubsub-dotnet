using System;
using System.Collections.Generic;
using System.Linq;
using IO.Ably.Transport;
using IO.Ably.Types;

namespace IO.Ably.Realtime.Workflow
{
    internal class PingCommand : RealtimeCommand
    {
        internal PingRequest Request { get; }

        private PingCommand(PingRequest request)
        {
            Request = request;
        }

        public static PingCommand Create(PingRequest request) => new PingCommand(request);

        protected override string ExplainData()
        {
            return "Ping request id: " + Request.Id;
        }
    }

    internal class PingTimerCommand : RealtimeCommand
    {
        public string PingRequestId { get; }

        protected override string ExplainData()
        {
            return "PingRequest id: " + PingRequestId;
        }

        private PingTimerCommand(string pingRequestId)
        {
            PingRequestId = pingRequestId;
        }

        public static PingTimerCommand Create(string pingRequestId) => new PingTimerCommand(pingRequestId);
    }

    internal class ListCommand : RealtimeCommand
    {
        public IEnumerable<RealtimeCommand> Commands { get; }

        private ListCommand(IEnumerable<RealtimeCommand> commands)
        {
            Commands = commands.ToList();
        }

        public static ListCommand Create(params RealtimeCommand[] commands) => new ListCommand(commands);

        protected override string ExplainData()
        {
            return "Commands: " + Commands.Select(x => x.Name).JoinStrings();
        }
    }

    internal class EmptyCommand : RealtimeCommand
    {
        public static readonly EmptyCommand Instance = new EmptyCommand();

        private EmptyCommand()
        {
        }

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }

    internal class ProcessMessageCommand : RealtimeCommand
    {
        private ProcessMessageCommand(ProtocolMessage protocolMessage)
        {
            ProtocolMessage = protocolMessage;
        }

        public ProtocolMessage ProtocolMessage { get; }

        public static ProcessMessageCommand Create(ProtocolMessage message) => new ProcessMessageCommand(message);

        protected override string ExplainData()
        {
            return "Message: " + ProtocolMessage.ToJson();
        }
    }

    internal class ConnectCommand : RealtimeCommand
    {
        private ConnectCommand()
        {
        }

        public static ConnectCommand Create() => new ConnectCommand();

        protected override string ExplainData() => string.Empty;
    }

    internal class ForceStateInitializationCommand : RealtimeCommand
    {
        private ForceStateInitializationCommand()
        {
        }

        public static ForceStateInitializationCommand Create() => new ForceStateInitializationCommand();

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }

    internal class DisposeCommand : RealtimeCommand
    {
        private DisposeCommand()
        {
        }

        public static DisposeCommand Create() => new DisposeCommand();

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }

    internal class CompleteWorkflowCommand : RealtimeCommand
    {
        private CompleteWorkflowCommand()
        {
        }

        public static CompleteWorkflowCommand Create() => new CompleteWorkflowCommand();

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }

    internal class CloseConnectionCommand : RealtimeCommand
    {
        private CloseConnectionCommand()
        {
        }

        public static CloseConnectionCommand Create() => new CloseConnectionCommand();

        protected override string ExplainData() => string.Empty;
    }

    internal class SetConnectingStateCommand : RealtimeCommand
    {
        private SetConnectingStateCommand(bool clearConnectionKey, bool retryAuth)
        {
            ClearConnectionKey = clearConnectionKey;
            RetryAuth = retryAuth;
        }

        public bool ClearConnectionKey { get; }

        public bool RetryAuth { get; }

        public static SetConnectingStateCommand Create(bool clearConnectionKey = false, bool retryAuth = false) => new SetConnectingStateCommand(clearConnectionKey, retryAuth);

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }

    internal class SetConnectedStateCommand : RealtimeCommand
    {
        public ProtocolMessage Message { get; }

        public bool IsUpdate { get; }

        private SetConnectedStateCommand(ProtocolMessage message, bool isUpdate)
        {
            Message = message;
            IsUpdate = isUpdate;
        }

        public static SetConnectedStateCommand Create(ProtocolMessage message, bool isUpdate) =>
                new SetConnectedStateCommand(message, isUpdate);

        protected override string ExplainData()
        {
            return $"IsUpdate: {IsUpdate}. Message: {Message.ToJson()}";
        }
    }

    internal class SetDisconnectedStateCommand : RealtimeCommand
    {
        private SetDisconnectedStateCommand(ErrorInfo error, bool retryInstantly, bool skipAttach, Exception exception, bool clearConnectionKey)
        {
            Error = error;
            RetryInstantly = retryInstantly;
            SkipAttach = skipAttach;
            Exception = exception;
            ClearConnectionKey = clearConnectionKey;
        }

        public ErrorInfo Error { get; }

        public bool RetryInstantly { get; }

        public bool SkipAttach { get; }

        public Exception Exception { get; }

        public bool ClearConnectionKey { get; }

        protected override string ExplainData()
        {
            return $"RetryInstantly: {RetryInstantly}" +
                   "SkipAttach: " + SkipAttach +
                   ((Error != null) ? " Error: " + Error : string.Empty) +
                    ((Exception != null) ? " Exception: " + Exception.Message : string.Empty) +
                " ClearConnectionKey: " + ClearConnectionKey;
        }

        public static SetDisconnectedStateCommand Create(
            ErrorInfo error,
            bool retryInstantly = false,
            bool skipAttach = false,
            Exception exception = null,
            bool clearConnectionKey = false)
            => new SetDisconnectedStateCommand(error, retryInstantly, skipAttach, exception, clearConnectionKey);
    }

    internal class SetSuspendedStateCommand : RealtimeCommand
    {
        private SetSuspendedStateCommand(ErrorInfo error, bool clearConnectionKey)
        {
            Error = error;
            ClearConnectionKey = clearConnectionKey;
        }

        public ErrorInfo Error { get; }

        public bool ClearConnectionKey { get; }

        public static SetSuspendedStateCommand Create(ErrorInfo error, bool clearConnectionKey = false) => new SetSuspendedStateCommand(error, clearConnectionKey);

        protected override string ExplainData()
        {
            var message = (Error != null) ? " Error: " + Error : string.Empty;
            message += " ClearConnectionKey:" + ClearConnectionKey;
            return message;
        }
    }

    internal class SetFailedStateCommand : RealtimeCommand
    {
        private SetFailedStateCommand(ErrorInfo error)
        {
            Error = error ?? ErrorInfo.ReasonFailed;
        }

        public ErrorInfo Error { get; }

        protected override string ExplainData()
        {
            return (Error != null) ? $"Error: {Error}" : string.Empty;
        }

        public static SetFailedStateCommand Create(ErrorInfo error) => new SetFailedStateCommand(error);
    }

    internal class SetClosingStateCommand : RealtimeCommand
    {
        private SetClosingStateCommand()
        {
        }

        protected override string ExplainData()
        {
            return string.Empty;
        }

        public static SetClosingStateCommand Create() => new SetClosingStateCommand();
    }

    internal class SetClosedStateCommand : RealtimeCommand
    {
        public ErrorInfo Error { get; }

        public Exception Exception { get; }

        private SetClosedStateCommand(ErrorInfo error, Exception exception = null)
        {
            Exception = exception;
            Error = error ?? ErrorInfo.ReasonClosed;
        }

        public static SetClosedStateCommand Create(ErrorInfo error = null, Exception exception = null) =>
            new SetClosedStateCommand(error, exception);

        protected override string ExplainData()
        {
            return $"Error: {Error}";
        }
    }

    internal class RetryAuthCommand : RealtimeCommand
    {
        public ErrorInfo Error { get; }

        public bool UpdateState { get; }

        private RetryAuthCommand(ErrorInfo error, bool updateState)
        {
            Error = error;
            UpdateState = updateState;
        }

        public static RetryAuthCommand Create(ErrorInfo error, bool updateState) => new RetryAuthCommand(error, updateState);

        public static RetryAuthCommand Create(bool updateState) => new RetryAuthCommand(null, updateState);

        protected override string ExplainData() => "UpdatedState: " + UpdateState + ((Error != null) ? " " + Error : string.Empty);
    }

    internal class SendMessageCommand : RealtimeCommand
    {
        public ProtocolMessage ProtocolMessage { get; }

        public Action<bool, ErrorInfo> Callback { get; }

        public bool Force { get; }

        private SendMessageCommand(ProtocolMessage protocolMessage, Action<bool, ErrorInfo> callback, bool force)
        {
            ProtocolMessage = protocolMessage;
            Callback = callback;
            Force = force;
        }

        public static SendMessageCommand Create(ProtocolMessage message, Action<bool, ErrorInfo> callback = null, bool force = false) => new SendMessageCommand(message, callback, force);

        protected override string ExplainData()
        {
            return "Protocol message: " + ProtocolMessage.ToJson();
        }
    }

    internal class DelayCommand : RealtimeCommand
    {
        public TimeSpan Delay { get; }

        public RealtimeCommand CommandToQueue { get; }

        private DelayCommand(TimeSpan delay, RealtimeCommand commandToQueue)
        {
            Delay = delay;
            CommandToQueue = commandToQueue;
            CommandToQueue.TriggeredBy(this);
        }

        public static DelayCommand Create(TimeSpan delay, RealtimeCommand command) => new DelayCommand(delay, command);

        protected override string ExplainData()
        {
            return $"Delay: {Delay.ToString()}. Command: {CommandToQueue.Name}";
        }
    }

    internal class HandleConnectingTokenErrorCommand : RealtimeCommand
    {
        public ErrorInfo Error { get; }

        private HandleConnectingTokenErrorCommand(ErrorInfo error)
        {
            Error = error;
        }

        public static RealtimeCommand Create(ErrorInfo error)
        {
            if (error == null || error.IsTokenError == false)
            {
#if DEBUG
                throw new ArgumentException("Cannot create a TokenError command with an error that is not a token error.");
#else
                DefaultLogger.Warning("Cannot create a TokenError command with an error that is not a token error");

                // TODO: Sentry alert
                return EmptyCommand.Instance;
#endif
            }

            return new HandleConnectingTokenErrorCommand(error);
        }

        protected override string ExplainData()
        {
            return $"Error: {Error}";
        }
    }

    internal class HandleConnectingDisconnectedCommand : RealtimeCommand
    {
        public ErrorInfo Error { get; }

        private HandleConnectingDisconnectedCommand(ErrorInfo error)
        {
            Error = error;
        }

        public static HandleConnectingDisconnectedCommand Create(ErrorInfo error = null) =>
            new HandleConnectingDisconnectedCommand(error);

        protected override string ExplainData()
        {
            return $"Error: {Error}.";
        }
    }

    internal class HandleAblyAuthorizeErrorCommand : RealtimeCommand
    {
        public AblyException Exception { get; }

        private HandleAblyAuthorizeErrorCommand(AblyException ex)
        {
            Exception = ex;
        }

        public static HandleAblyAuthorizeErrorCommand Create(AblyException ex = null) =>
            new HandleAblyAuthorizeErrorCommand(ex);

        protected override string ExplainData()
        {
            return $" Exception: {Exception?.Message} ";
        }
    }

    internal class HandleConnectingErrorCommand : RealtimeCommand
    {
        public ErrorInfo Error { get; }

        public AblyException Exception { get; }

        public bool ClearConnectionKey { get; }

        private HandleConnectingErrorCommand(ErrorInfo error, AblyException ex, bool clearConnectionKey)
        {
            Error = error;
            Exception = ex;
            ClearConnectionKey = clearConnectionKey;
        }

        public static HandleConnectingErrorCommand Create(ErrorInfo error = null, AblyException ex = null, bool clearConnectionKey = false) =>
            new HandleConnectingErrorCommand(error, ex, clearConnectionKey);

        protected override string ExplainData()
        {
            return $"Error: {Error}. Exception: {Exception?.Message}. ClearConnectionKey: {ClearConnectionKey}";
        }
    }

    internal class HandleTransportEventCommand : RealtimeCommand
    {
        public Guid TransportId { get; }

        public TransportState TransportState { get; }

        public Exception Exception { get; }

        private HandleTransportEventCommand(Guid transportId, TransportState transportState, Exception ex)
        {
            TransportId = transportId;
            TransportState = transportState;
            Exception = ex;
        }

        public static HandleTransportEventCommand Create(Guid transportId, TransportState state, Exception ex) =>
            new HandleTransportEventCommand(transportId, state, ex);

        protected override string ExplainData()
        {
            return $"Transport Id: {TransportId}. TransportState: {TransportState}. Exception: {Exception?.Message}";
        }
    }

    internal class HeartbeatMonitorCommand : RealtimeCommand
    {
        private HeartbeatMonitorCommand(DateTimeOffset? confirmedAliveAt, TimeSpan connectionStateTtl)
        {
            ConfirmedAliveAt = confirmedAliveAt;
            ConnectionStateTtl = connectionStateTtl;
        }

        public DateTimeOffset? ConfirmedAliveAt { get; }

        public TimeSpan ConnectionStateTtl { get; }

        public static HeartbeatMonitorCommand Create(DateTimeOffset? confirmedAliveAt, TimeSpan connectionStateTtl) =>
            new HeartbeatMonitorCommand(confirmedAliveAt, connectionStateTtl);

        protected override string ExplainData()
        {
            return $"ConfirmedAliveAt: {ConfirmedAliveAt}. ConnectionStateTtl {ConnectionStateTtl}";
        }
    }
}
