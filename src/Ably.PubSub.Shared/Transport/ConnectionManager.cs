using System;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Realtime;
using Ably.PubSub.Realtime.Workflow;
using Ably.PubSub.Shared.Realtime;
using Ably.PubSub.Transport.States.Connection;
using Ably.PubSub.Types;
using Ably.PubSub.Utils;

namespace Ably.PubSub.Transport
{
    internal class ConnectionManager : IConnectionManager, ITransportListener, IConnectionContext
    {
        internal Func<DateTimeOffset> Now { get; set; }

        internal ILogger Logger { get; }

        private ITransportFactory GetTransportFactory()
            => Options.TransportFactory ?? Defaults.WebSocketTransportFactory;

        public TimeSpan RetryTimeout => Options.DisconnectedRetryTimeout;

        public PubSubHttpClient HttpClient => Connection.HttpClient;

        public MessageHandler Handler => HttpClient.MessageHandler;

        public ConnectionStateBase State => Connection.ConnectionState;

        public ITransport Transport { get; internal set; }

        public ClientOptions Options => HttpClient.Options;

        public TimeSpan DefaultTimeout => Options.RealtimeRequestTimeout;

        public TimeSpan SuspendRetryTimeout => Options.SuspendedRetryTimeout;

        public Connection Connection { get; }

        public ConnectionState ConnectionState => Connection.State;

        public ConnectionManager(Connection connection, Func<DateTimeOffset> nowFunc, ILogger logger)
        {
            Now = nowFunc;
            Logger = logger ?? DefaultLogger.LoggerInstance;
            Connection = connection;
        }

        public RealtimeCommand Connect()
        {
            return State.Connect();
        }

        public async Task CreateTransport(string host)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug("Creating transport");
            }

            if (Transport != null)
            {
                DestroyTransport();
            }

            try
            {
                var transportParams = await CreateTransportParameters(host);
                if (transportParams.RecoverValue.IsNotEmpty())
                {
                    var recoveryKeyContext = RecoveryKeyContext.Decode(transportParams.RecoverValue, Logger);
                    if (recoveryKeyContext != null)
                    {
                        Connection.RealtimeClient.Channels.SetChannelSerialsFromRecoverOption(recoveryKeyContext.ChannelSerials);
                        Connection.InnerState.MessageSerial = recoveryKeyContext.MsgSerial;
                    }
                }

                // RTN23b - taken from the params this transport is actually built with, so the
                // RTN23a monitor measures against what went on the wire rather than against
                // ClientOptions, which the caller can mutate at any time. Read after the merge, so
                // a caller entry that displaced ours is already reflected however it was spelled.
                var wireParams = transportParams.GetParams();
                var heartbeatsRequested = wireParams.TryGetValue("heartbeats", out var heartbeatsValue)
                                          && heartbeatsValue.EqualsTo("true");
                Connection.InnerState.ProtocolHeartbeatsRequested = heartbeatsRequested;

                if (heartbeatsRequested == false)
                {
                    Logger.Warning(
                        "This connection did not ask Ably for protocol heartbeats, so Ably may keep " +
                        "it alive with websocket pings, which this library cannot see. Idle " +
                        "connection detection is off and a silently dropped connection will not be " +
                        "detected. Set transportParams heartbeats to 'true' to enable it.");
                }

                var transport = GetTransportFactory().CreateTransport(transportParams);
                transport.Listener = this;
                Transport = transport;
                Transport.Connect();
            }
            catch (Exception ex)
            {
                Logger.Error("Error while creating transport!.", ex.Message);
                if (ex is AblyException)
                {
                    throw;
                }

                throw new AblyException(new ErrorInfo("Error creating Socket Transport", ErrorCodes.ConnectionFailed, HttpStatusCode.ServiceUnavailable), ex);
            }
        }

        public void DestroyTransport()
        {
            if (Logger.IsDebug)
            {
                Logger.Debug("Destroying transport");
            }

            if (Transport == null)
            {
                return;
            }

            try
            {
                Transport.Listener = null;
                Transport.Dispose();
            }
            catch (Exception e)
            {
                Logger.Warning("Error while destroying transport. Nothing to worry about. Cleaning up. Error: " +
                               e.Message);
            }
            finally
            {
                Transport = null;
            }
        }

        public bool ShouldWeRenewToken(ErrorInfo error, RealtimeState state)
        {
            if (error == null)
            {
                return false;
            }

            return error.IsTokenError && state.AttemptsInfo.TriedToRenewToken == false && HttpClient.AblyAuth.TokenRenewable;
        }

        public void CloseConnection()
        {
            State.Close();
        }

        internal async Task OnAuthUpdated(TokenDetails tokenDetails, bool wait)
        {
            if (Connection.State != ConnectionState.Connected)
            {
                ExecuteCommand(Connect());
                return;
            }

            try
            {
                var msg = new ProtocolMessage(ProtocolMessage.MessageAction.Auth)
                {
                    Auth = new AuthDetails
                    {
                        AccessToken = tokenDetails.Token,
                    },
                };

                Send(msg);
            }
            catch (AblyException e)
            {
                Logger.Warning("OnAuthUpdated: closing transport after send failure");
                Logger.Debug(e.Message);
                Transport?.Close();
            }

            if (wait)
            {
                var waiter = new ConnectionChangeAwaiter(Connection);

                try
                {
                    while (true)
                    {
                        // Options rather than Defaults, since TO3l11 makes realtimeRequestTimeout a
                        // client option.
                        var (success, newState) = await waiter.Wait(Options.RealtimeRequestTimeout);
                        if (success == false)
                        {
                            throw new AblyException(
                                new ErrorInfo(
                                $"Connection state didn't change after Auth updated within {Options.RealtimeRequestTimeout}",
                                40140));
                        }

                        switch (newState)
                        {
                            case ConnectionState.Connected:
                                Logger.Debug("onAuthUpdated: Successfully connected");
                                return;
                            case ConnectionState.Connecting:
                            case ConnectionState.Disconnected:
                                continue;
                            default: // if it's one of the failed states
                                Logger.Debug("onAuthUpdated: Failed to reconnect");
                                throw new AblyException(Connection.ErrorReason);
                        }
                    }
                }
                catch (AblyException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Error("Error in AuthUpdated handler", e);
                    throw new AblyException(
                        new ErrorInfo("Error while waiting for connection to update after updating Auth"), e);
                }
            }
        }

        public void Send(
            ProtocolMessage message,
            Action<bool, ErrorInfo> callback = null,
            ChannelOptions channelOptions = null)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"Current state: {Connection.State}. Sending message: {message}");
            }

            if (message.ConnectionId.IsNotEmpty())
            {
                Logger.Warning(
                    "Setting ConnectionId to null. ConnectionId should never be included in an outbound message on a realtime connection, it’s always implicit");
                message.ConnectionId = null;
            }

            Result result = VerifyMessageHasCompatibleClientId(message);
            if (result.IsFailure)
            {
                callback?.Invoke(false, result.Error);
                return;
            }

            Result encodingResult = Handler.EncodeProtocolMessage(message, channelOptions.ToDecodingContext());
            if (encodingResult.IsFailure)
            {
                Logger.Error($"Failed to encode protocol message: {encodingResult.Error.Message}");
            }

            if (State.CanSend == false)
            {
                if (Options.QueueMessages == false)
                {
                    throw new AblyException(
                        $"Not queuing messages for [{State.State}] since Options.QueueMessages is set to False.",
                        ErrorInfo.ReasonUnknown.Code,
                        HttpStatusCode.BadRequest);
                }

                if (State.CanQueue == false)
                {
                    throw new AblyException(
                        $"The current connection state [{State.State}] does not allow messages to be sent.",
                        ErrorInfo.ReasonUnknown.Code,
                        HttpStatusCode.BadRequest);
                }
            }

            ExecuteCommand(SendMessageCommand.Create(message, callback).TriggeredBy("ConnectionManager.Send()"));

            Result VerifyMessageHasCompatibleClientId(ProtocolMessage protocolMessage)
            {
                var messagesResult = HttpClient.AblyAuth.ValidateClientIds(protocolMessage.Messages);
                var presenceResult = HttpClient.AblyAuth.ValidateClientIds(protocolMessage.Presence);

                return Result.Combine(messagesResult, presenceResult);
            }
        }

        public Result SendToTransport(ProtocolMessage message)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"Sending message ({message.Action}) to transport");
            }

            var data = Handler.GetTransportData(message);
            try
            {
                return Transport.Send(data);
            }
            catch (Exception e)
            {
                Logger.Error("Error while sending to transport. Trying to reconnect.", e);
                ((ITransportListener)this).OnTransportEvent(Transport.Id, TransportState.Closed, e);
                return Result.Fail("Error while sending to transport.");
            }
        }

        void ITransportListener.OnTransportEvent(Guid transportId, TransportState transportState, Exception ex)
            => ExecuteCommand(HandleTransportEventCommand.Create(transportId, transportState, ex).TriggeredBy("ConnectionManager.OnTransportEvent()"));

        void ITransportListener.OnTransportDataReceived(RealtimeTransportData data)
        {
            var message = Handler.ParseRealtimeData(data);
            ExecuteCommand(ProcessMessageCommand.Create(message).TriggeredBy("ConnectionManager.OnTransportDataReceived()"));
        }

        public void ExecuteCommand(RealtimeCommand cmd)
        {
            Connection.RealtimeClient.Workflow.QueueCommand(cmd);
        }

        internal async Task<TransportParams> CreateTransportParameters(string host)
        {
            return await TransportParams.Create(
                host,
                HttpClient.AblyAuth,
                Options,
                Connection.Key);
        }

        public void HandleNetworkStateChange(NetworkState state)
        {
            switch (state)
            {
                case NetworkState.Online:
                    // RTN20b
                    if (ConnectionState == ConnectionState.Disconnected ||
                        ConnectionState == ConnectionState.Suspended)
                    {
                        if (Logger.IsDebug)
                        {
                            Logger.Debug("Network state is Online. Attempting reconnect.");
                        }

                        ExecuteCommand(ConnectCommand.Create().TriggeredBy("ConnectionManager.HandleNetworkStateChange(Online)"));
                    }

                    // RTN20c
                    if (ConnectionState == ConnectionState.Connecting)
                    {
                        if (Logger.IsDebug)
                        {
                            Logger.Debug("Network state is Online. Attempting reconnect.");
                        }

                        ExecuteCommand(SetConnectingStateCommand.Create().TriggeredBy("ConnectionManager.HandleNetworkStateChange(Online)"));
                    }

                    break;

                // RTN20a
                case NetworkState.Offline:
                    if (ConnectionState == ConnectionState.Connected ||
                        ConnectionState == ConnectionState.Connecting)
                    {
                        if (Logger.IsDebug)
                        {
                            Logger.Debug("Network state is Offline. Moving to disconnected.");
                        }

                        // RTN20a
                        var errorInfo =
                            new ErrorInfo(
                                "Connection disconnected due to Operating system network going offline",
                                80017);
                        ExecuteCommand(SetDisconnectedStateCommand.Create(errorInfo, retryInstantly: true).TriggeredBy("ConnectionManager.HandleNetworkStateChange(Offline)"));
                    }

                    break;
            }
        }
    }
}
