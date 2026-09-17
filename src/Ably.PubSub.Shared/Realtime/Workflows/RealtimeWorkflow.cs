using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using IO.Ably.Utils;

namespace IO.Ably.Realtime.Workflow
{
    /// <summary>
    /// Realtime workflow has 2 roles
    /// 1. It serializes requests coming from different threads and guarantees that they are executing one by one and in order
    /// on a single thread. This makes it very easy to mutate state because we are immune from thread race conditions.
    /// There requests are encapsulated in Command objects (objects inheriting from RealtimeCommand) which provide
    /// information about what needs to happen and also hold any parameters necessary for the operation. For example if we take the
    /// SetClosedStateCommand object. The intention is to change the Connection state to Closed but the Command object also contains the error
    /// if any associated with this request. This makes logging very easy as we can clearly see the intent of the command and the parameters. Also in the
    /// future we can parse the logs and easily recreate state in the library.
    /// 2. Centralizes the logic for handling Commands. It is now much easier to find where things are happening. If you exclude
    /// Channel presence and Channel state management, everything else could be found in this class. It does make it rather long but
    /// the logic block are rather small and easy to understand.
    /// </summary>
    internal sealed class RealtimeWorkflow : IQueueCommand, IDisposable
    {
        private readonly CancellationTokenSource _heartbeatMonitorCancellationTokenSource;

        // This is used for the tests so we can have a good
        // way of figuring out when processing has finished
        private volatile bool _processingCommand;
        private bool _heartbeatMonitorDisconnectRequested;
        private bool _disposedValue;

        private AblyRealtime Client { get; }

        private AblyAuth Auth => Client.RestClient.AblyAuth;

        public Connection Connection { get; }

        public RealtimeChannels Channels { get; }

        public ConnectionManager ConnectionManager => Connection.ConnectionManager;

        public ILogger Logger { get; }

        private RealtimeState State => Client.State;

        private Func<DateTimeOffset> Now => Connection.Now;

        internal ConnectionHeartbeatHandler HeartbeatHandler { get; }

        internal ChannelMessageProcessor ChannelMessageProcessor { get; }

        internal readonly List<(string, Func<ProtocolMessage, RealtimeState, Task<bool>>)> ProtocolMessageProcessors;

        internal readonly Channel<RealtimeCommand> CommandChannel = Channel.CreateUnbounded<RealtimeCommand>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        public RealtimeWorkflow(AblyRealtime client, ILogger logger)
        {
            _heartbeatMonitorCancellationTokenSource = new CancellationTokenSource();

            Client = client;
            Client.RestClient.AblyAuth.ExecuteCommand = cmd => QueueCommand(cmd);
            Connection = client.Connection;
            Channels = client.Channels;
            Logger = logger;

            SetInitialConnectionState();

            HeartbeatHandler = new ConnectionHeartbeatHandler(Connection.ConnectionManager, logger);
            ChannelMessageProcessor = new ChannelMessageProcessor(Channels, client.MessageHandler, logger);
            ProtocolMessageProcessors = new List<(string, Func<ProtocolMessage, RealtimeState, Task<bool>>)>
            {
                ("State handler", (message, state) => ConnectionManager.State.OnMessageReceived(message, state)),
                ("Heartbeat handler", HeartbeatHandler.OnMessageReceived),
                ("Ack handler", (message, _) => HandleAckMessage(message)),
            };

            Logger.Debug("Workflow initialised!");
        }

        private void SetInitialConnectionState()
        {
            var initialState = new ConnectionInitializedState(ConnectionManager, Logger);
            State.Connection.CurrentStateObject = initialState;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(
                state =>
                {
                    _ = ((RealtimeWorkflow)state).Consume();
                }, this);

            _ = Task.Run(
                async () =>
                {
                    while (true)
                    {
                        QueueCommand(HeartbeatMonitorCommand.Create(Connection.ConfirmedAliveAt, Connection.ConnectionStateTtl).TriggeredBy("AblyRealtime.HeartbeatMonitor()"));
                        await Task.Delay(Client.Options.HeartbeatMonitorDelay, _heartbeatMonitorCancellationTokenSource.Token);
                    }
                },
                _heartbeatMonitorCancellationTokenSource.Token);
        }

        public void QueueCommand(params RealtimeCommand[] commands)
        {
            foreach (var command in commands)
            {
                var writeResult = CommandChannel.Writer.TryWrite(command);

                // This can only happen if the workflow is disposed.
                if (writeResult == false)
                {
                    Logger.Warning(
                        $"Cannot schedule command: {command.Explain()} because the execution channel is closed");
                }
            }
        }

        private async Task Consume()
        {
            try
            {
                Logger.Debug("Starting to process Workflow");

                var reader = CommandChannel.Reader;
                while (await reader.WaitToReadAsync())
                {
                    if (reader.TryRead(out RealtimeCommand cmd))
                    {
                        try
                        {
                            _processingCommand = true;

                            int level = 0;
                            var cmds = new List<RealtimeCommand> { cmd };
                            while (cmds.Count > 0)
                            {
                                if (level > 5)
                                {
                                    throw new Exception("Something is wrong. There shouldn't be 5 levels of nesting");
                                }

                                var cmdsToExecute = cmds.ToArray();
                                cmds.Clear();

                                foreach (var cmdToExecute in cmdsToExecute)
                                {
                                    try
                                    {
                                        var result = await ProcessCommand(cmdToExecute);
                                        cmds.AddRange(result);
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Error($"Error Processing command: {cmdsToExecute}", e);
                                    }
                                }

                                level++;
                            }
                        }
                        catch (Exception e)
                        {
                            // TODO: Emit the error to the error reporting service
                            Logger.Error("Error processing command: " + cmd.Explain(), e);
                        }
                        finally
                        {
                            _processingCommand = false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error initialising workflow.", e);
            }
        }

        private void DelayCommandHandler(TimeSpan delay, RealtimeCommand cmd) =>
            Task.Delay(delay).ContinueWith(_ => QueueCommand(cmd));

        internal async Task<IEnumerable<RealtimeCommand>> ProcessCommand(RealtimeCommand command)
        {
            bool shouldLogCommand = !((command is EmptyCommand) || (command is ListCommand));
            try
            {
                if (Logger.IsDebug && shouldLogCommand)
                {
                    Logger.Debug("Begin - " + command.Explain());
                }

                switch (command)
                {
                    case ListCommand cmd:
                        return cmd.Commands;
                    case EmptyCommand _:
                        return Enumerable.Empty<RealtimeCommand>();
                    case DisposeCommand _:
                        if (State.Connection.State == ConnectionState.Connected)
                        {
                            return new RealtimeCommand[]
                            {
                                SendMessageCommand.Create(
                                    new ProtocolMessage(ProtocolMessage.MessageAction.Close), force: true).TriggeredBy(command),
                                CompleteWorkflowCommand.Create().TriggeredBy(command),
                            };
                        }
                        else
                        {
                            return new RealtimeCommand[] { CompleteWorkflowCommand.Create().TriggeredBy(command) };
                        }

                    case CompleteWorkflowCommand _:
                        _heartbeatMonitorCancellationTokenSource.Cancel();
                        Channels.ReleaseAll();
                        ConnectionManager.Transport?.Dispose();
                        CommandChannel.Writer.TryComplete();
                        State.Connection.CurrentStateObject?.AbortTimer();
                        return Enumerable.Empty<RealtimeCommand>();
                    case HeartbeatMonitorCommand cmd:
                        return await HandleHeartbeatMonitorCommand(cmd);
                    default:
                        var next = await ProcessCommandInner(command);
                        return new[]
                        {
                            next,
                        };
                }
            }
            finally
            {
                if (Logger.IsDebug && shouldLogCommand)
                {
                    Logger.Debug($"End - {command.Name}|{command.Id}");
                }
            }
        }

        private async Task<IEnumerable<RealtimeCommand>> HandleHeartbeatMonitorCommand(HeartbeatMonitorCommand command)
        {
            if (!command.ConfirmedAliveAt.HasValue)
            {
                return Enumerable.Empty<RealtimeCommand>();
            }

            TimeSpan delta = Now() - command.ConfirmedAliveAt.Value;
            if (delta > command.ConnectionStateTtl)
            {
                if (!_heartbeatMonitorDisconnectRequested)
                {
                    _heartbeatMonitorDisconnectRequested = true;
                    return new RealtimeCommand[] { SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected).TriggeredBy(command) };
                }
            }
            else
            {
                if (_heartbeatMonitorDisconnectRequested)
                {
                    _heartbeatMonitorDisconnectRequested = false;
                }
            }

            return Enumerable.Empty<RealtimeCommand>();
        }

        /// <summary>
        /// Processes a command and return a list of commands that need to be immediately executed.
        /// </summary>
        /// <param name="command">The current command that will be executed.</param>
        /// <returns>returns the next command that needs to be executed.</returns>
        /// <exception cref="AblyException">will throw an AblyException if anything goes wrong.</exception>
        private async Task<RealtimeCommand> ProcessCommandInner(RealtimeCommand command)
        {
            switch (command)
            {
                case ConnectCommand _:
                    var nextCommand = ConnectionManager.Connect();
                    var initFailedChannelsOnConnect =
                        ChannelCommand.CreateForAllChannels(InitialiseFailedChannelsOnConnect.Create().TriggeredBy(command));
                    return ListCommand.Create(initFailedChannelsOnConnect, nextCommand);

                case CloseConnectionCommand _:
                    ConnectionManager.CloseConnection();
                    break;

                case RetryAuthCommand retryAuth:

                    ClearTokenAndRecordRetry();

                    if (retryAuth.UpdateState)
                    {
                        return ListCommand.Create(
                            SetDisconnectedStateCommand.Create(
                                retryAuth.Error,
                                skipAttach: State.Connection.State == ConnectionState.Connecting).TriggeredBy(command),
                            SetConnectingStateCommand.Create(retryAuth: true).TriggeredBy(command));
                    }
                    else
                    {
                        await Auth.RenewToken();
                        return EmptyCommand.Instance;
                    }

                case ForceStateInitializationCommand _:
                case SetConnectedStateCommand _:
                case SetConnectingStateCommand _:
                case SetFailedStateCommand _:
                case SetDisconnectedStateCommand _:
                case SetClosingStateCommand _:
                case SetSuspendedStateCommand _:
                case SetClosedStateCommand _:
                    return await HandleSetStateCommand(command);

                case ProcessMessageCommand cmd:
                    await ProcessMessage(cmd.ProtocolMessage);
                    break;
                case SendMessageCommand cmd:
                    if (State.Connection.CurrentStateObject.CanSend || cmd.Force)
                    {
                        var sendResult = SendMessage(cmd.ProtocolMessage, cmd.Callback);
                        if (sendResult.IsFailure && State.Connection.CurrentStateObject.CanQueue && Client.Options.QueueMessages)
                        {
                            Logger.Debug("Failed to send message. Queuing it.");
                            State.PendingMessages.Add(new MessageAndCallback(
                                cmd.ProtocolMessage,
                                cmd.Callback,
                                Logger));
                        }
                    }
                    else if (State.Connection.CurrentStateObject.CanQueue && Client.Options.QueueMessages)
                    {
                        Logger.Debug("Queuing message");
                        State.PendingMessages.Add(new MessageAndCallback(
                            cmd.ProtocolMessage,
                            cmd.Callback,
                            Logger));
                    }

                    break;
                case PingCommand cmd:
                    return ListCommand.Create(HandlePingCommand(cmd).ToArray());
                case EmptyCommand _:
                    break;
                case DelayCommand cmd:
                    DelayCommandHandler(cmd.Delay, cmd.CommandToQueue);
                    break;
                case PingTimerCommand cmd:
                    HandlePingTimer(cmd);
                    break;
                case ChannelCommand cmd:
                    await Channels.ExecuteCommand(cmd);
                    break;
                case HandleConnectingTokenErrorCommand cmd:
                    try
                    {
                        if (Auth.TokenRenewable)
                        {
                            if (State.AttemptsInfo.TriedToRenewToken == false)
                            {
                                ClearTokenAndRecordRetry();
                                try
                                {
                                    await Auth.RenewToken();
                                    await AttemptANewConnection();
                                    return EmptyCommand.Instance;
                                }
                                catch (AblyException e)
                                {
                                    return SetDisconnectedStateCommand.Create(
                                            e.ErrorInfo,
                                            clearConnectionKey: true)
                                        .TriggeredBy(cmd);
                                }
                            }

                            return SetDisconnectedStateCommand.Create(cmd.Error).TriggeredBy(cmd);
                        }

                        // If Token is not renewable we go to the failed state
                        return SetFailedStateCommand.Create(cmd.Error).TriggeredBy(cmd);
                    }
                    catch (AblyException ex)
                    {
                        Logger.Error("Error trying to renew token.", ex);
                        return SetDisconnectedStateCommand.Create(ex.ErrorInfo).TriggeredBy(cmd);
                    }

                    async Task AttemptANewConnection()
                    {
                        var host = AttemptsHelpers.GetHost(State, Client.Options.FullRealtimeHost());
                        SetNewHostInState(host);

                        await ConnectionManager.CreateTransport(host);
                    }

                case HandleConnectingDisconnectedCommand cmd:
                    if (State.ShouldSuspend(Now))
                    {
                        return SetSuspendedStateCommand.Create(
                                cmd.Error ?? ErrorInfo.ReasonSuspended,
                                clearConnectionKey: true)
                            .TriggeredBy(cmd);
                    }
                    else
                    {
                        return SetDisconnectedStateCommand.Create(
                                cmd.Error ?? ErrorInfo.ReasonDisconnected,
                                clearConnectionKey: true)
                            .TriggeredBy(cmd);
                    }

                case HandleConnectingErrorCommand cmd:
                    var error = cmd.Error ?? cmd.Exception?.ErrorInfo ?? ErrorInfo.ReasonUnknown;

                    if (error.IsTokenError)
                    {
                        return HandleConnectingTokenErrorCommand.Create(error)
                            .TriggeredBy(cmd);
                    }

                    if (error.IsRetryableStatusCode())
                    {
                        if (State.ShouldSuspend(Now))
                        {
                            return SetSuspendedStateCommand.Create(
                                    error,
                                    clearConnectionKey: true)
                                .TriggeredBy(cmd);
                        }

                        return SetDisconnectedStateCommand.Create(
                                error,
                                clearConnectionKey: true)
                            .TriggeredBy(cmd);
                    }
                    else
                    {
                        return SetFailedStateCommand.Create(error)
                            .TriggeredBy(cmd);
                    }

                case HandleTransportEventCommand cmd:

                    if (ConnectionManager.Transport != null
                        && ConnectionManager.Transport.Id != cmd.TransportId)
                    {
                        Logger.Debug($"Skipping Transport Event command because the transport it relates to no longer exists. Current transport: {ConnectionManager.Transport.Id}");
                        return EmptyCommand.Instance;
                    }

                    // If it's an error or has been closed we want to do something about it
                    if (cmd.TransportState == TransportState.Closed || cmd.Exception != null)
                    {
                        switch (State.Connection.State)
                        {
                            case ConnectionState.Closing:
                                return SetClosedStateCommand.Create(exception: cmd.Exception).TriggeredBy(cmd);

                            case ConnectionState.Connecting:
                                AblyException ablyException = null;
                                if (cmd.Exception != null)
                                {
                                    ablyException = cmd.Exception as AblyException ?? new AblyException(cmd.Exception.Message, ErrorCodes.ConnectionFailed, HttpStatusCode.ServiceUnavailable);
                                }

                                return HandleConnectingErrorCommand.Create(null, ablyException).TriggeredBy(cmd);

                            case ConnectionState.Connected:
                                var errorInfo =
                                    GetErrorInfoFromTransportException(cmd.Exception, ErrorInfo.ReasonDisconnected);
                                return SetDisconnectedStateCommand.Create(
                                    errorInfo,
                                    retryInstantly: Connection.ConnectionResumable,
                                    exception: cmd.Exception).TriggeredBy(cmd);

                            case ConnectionState.Initialized:
                            case ConnectionState.Disconnected:
                            case ConnectionState.Suspended:
                            case ConnectionState.Closed:
                            case ConnectionState.Failed:
                                // Nothing to do here.
                                break;

                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    return EmptyCommand.Instance;
                case HandleAblyAuthorizeErrorCommand cmd:
                    var exception = cmd.Exception;
                    if (exception?.ErrorInfo?.StatusCode == HttpStatusCode.Forbidden)
                    {
                        Logger.Debug("Triggering Connection Error due to 403 Authorize error");
                        return HandleConnectingErrorCommand.Create(null, cmd.Exception).TriggeredBy(cmd);
                    }

                    return EmptyCommand.Instance;
                default:
                    throw new AblyException("No handler found for - " + command.Explain());
            }

            async Task ProcessMessage(ProtocolMessage message)
            {
                try
                {
                    State.Connection.SetConfirmedAlive(Now());

                    foreach (var (name, handler) in ProtocolMessageProcessors)
                    {
                        var handled = await handler(message, State);
                        if (Logger.IsDebug)
                        {
                            Logger.Debug($"Message handler '{name}' - {(handled ? "Handled" : "Skipped")}");
                        }

                        if (handled)
                        {
                            break;
                        }
                    }

                    // Notify the Channel message processor regardless of what happened above
                    await ChannelMessageProcessor.MessageReceived(message, State);
                }
                catch (Exception e)
                {
                    Logger.Error("Error processing message: " + message, e);
                    throw new AblyException(e);
                }
            }

            ErrorInfo GetErrorInfoFromTransportException(Exception ex, ErrorInfo @default)
            {
                if (ex?.Message == "HTTP/1.1 401 Unauthorized")
                {
                    return ErrorInfo.ReasonRefused;
                }

                @default.InnerException = ex;
                return @default;
            }

            return EmptyCommand.Instance;
        }

        private void SetNewHostInState(string newHost)
        {
            if (IsFallbackHost())
            {
                Client.RestClient.SetRealtimeFallbackHost(newHost);
            }
            else
            {
                Client.RestClient.ClearRealtimeFallbackHost();
            }

            State.Connection.Host = newHost;

            bool IsFallbackHost()
            {
                return State.Connection.FallbackHosts.Contains(newHost);
            }
        }

        private void ClearTokenAndRecordRetry()
        {
            Auth.CurrentToken = null;
            State.AttemptsInfo.RecordTokenRetry();
        }

        private Result SendMessage(ProtocolMessage message, Action<bool, ErrorInfo> callback)
        {
            if (message.AckRequired)
            {
                message.MsgSerial = State.Connection.IncrementSerial();
                State.AddAckMessage(message, callback);
            }

            return ConnectionManager.SendToTransport(message);
        }

        private void HandleConnectedCommand(SetConnectedStateCommand cmd)
        {
            var info = new ConnectionInfo(cmd.Message);

            // recover is used when set via clientOptions#recover initially, resume will be used for all subsequent requests.
            var isConnectionResumeOrRecoverAttempt = State.Connection.Key.IsNotEmpty() || Client.Options.Recover.IsNotEmpty();

            var failedResumeOrRecover = State.Connection.Id != info.ConnectionId && cmd.Message.Error != null; // RTN15c7, RTN16d

            State.Connection.Update(info); // RTN16d, RTN15e

            if (info.ClientId.IsNotEmpty())
            {
                Auth.ConnectionClientId = info.ClientId;
            }

            var connectedState = new ConnectionConnectedState(
                ConnectionManager,
                cmd.Message.Error,
                cmd.IsUpdate,
                Logger);

            SetState(connectedState); // RTN15c7 - if error, set on connection and part of emitted connected event

            Client.Options.Recover = null; // RTN16k, explicitly setting null so it won't be used for subsequent connection requests

            // RTN15c7
            if (isConnectionResumeOrRecoverAttempt && failedResumeOrRecover)
            {
                State.Connection.MessageSerial = 0;
            }

            // RTN15g3, RTN15c6, RTN15c7, RTN16l - for resume/recovered or when connection ttl passed, re-attach channels
            if (State.Connection.HasConnectionStateTtlPassed(Now) || isConnectionResumeOrRecoverAttempt)
            {
                foreach (var channel in Channels)
                {
                    if (channel.State == ChannelState.Attaching || channel.State == ChannelState.Attached || channel.State == ChannelState.Suspended)
                    {
                        ((RealtimeChannel)channel).Attach(null, null, null, true); // state changes as per RTL2g
                    }
                }
            }

            SendPendingMessagesOnConnected(failedResumeOrRecover); // RTN19a
        }

        private void HandlePingTimer(PingTimerCommand cmd)
        {
            var relevantRequest = State.PingRequests.FirstOrDefault(x => x.Id.EqualsTo(cmd.PingRequestId));

            if (relevantRequest != null)
            {
                // fail the request if it still exists. If it was already handled it will not be there
                relevantRequest.Callback?.Invoke(null, PingRequest.TimeOutError);

                State.PingRequests.Remove(relevantRequest);
            }
        }

        private IEnumerable<RealtimeCommand> HandlePingCommand(PingCommand cmd)
        {
            if (Connection.State != ConnectionState.Connected)
            {
                // We don't want to wait for the execution to finish
                _ = NotifyExternalClient(
                    () => { cmd.Request.Callback?.Invoke(null, PingRequest.DefaultError); },
                    "Notifying Ping callback because connection state is not Connected");
            }
            else
            {
                yield return SendMessageCommand.Create(new ProtocolMessage(ProtocolMessage.MessageAction.Heartbeat)
                {
                    Id = cmd.Request.Id, // Pass the ping request id so we can match it on the way back
                }).TriggeredBy(cmd);

                // Only trigger the timer if there is a callback
                // Question: Do we trigger the error if there is no callback but then we can just emmit it
                if (cmd.Request.Callback != null)
                {
                    State.PingRequests.Add(cmd.Request);
                    yield return DelayCommand.Create(
                        ConnectionManager.DefaultTimeout,
                        PingTimerCommand.Create(cmd.Request.Id)).TriggeredBy(cmd);
                }
            }
        }

        private Task NotifyExternalClient(Action action, string reason)
        {
            try
            {
                return Task.Run(() => ActionUtils.SafeExecute(action));
            }
            catch (Exception e)
            {
                Logger.Error("Error while notifying external client for " + reason, e);
            }

            return Task.CompletedTask;
        }

        private async Task<RealtimeCommand> HandleSetStateCommand(RealtimeCommand command)
        {
            try
            {
                switch (command)
                {
                    case ForceStateInitializationCommand _:
                        var initState = new ConnectionInitializedState(ConnectionManager, Logger);
                        SetState(initState);
                        break;
                    case SetConnectedStateCommand cmd:
                        HandleConnectedCommand(cmd);
                        break;
                    case SetConnectingStateCommand cmd:

                        try
                        {
                            if (cmd.ClearConnectionKey)
                            {
                                State.Connection.ClearKey();
                            }

                            // RTN15g - If a client has been disconnected for longer
                            // than the connectionStateTtl, it should not attempt to resume.
                            if (State.Connection.HasConnectionStateTtlPassed(Now))
                            {
                                State.Connection.ClearKeyAndId();
                            }

                            var defaultRealtimeHost = Client.Options.FullRealtimeHost();

                            // Always retry on defaultPrimaryHost first when connecting command triggered by Disconnected/Suspended state timeout.
                            var connectingHost = defaultRealtimeHost;

                            // Otherwise use host fallbacks if connecting command triggered by other commands
                            if (cmd.TriggeredByMessage.Contains("OnTimeOut()") == false)
                            {
                               connectingHost = AttemptsHelpers.GetHost(State, defaultRealtimeHost);
                            }

                            SetNewHostInState(connectingHost);

                            var connectingState = new ConnectionConnectingState(ConnectionManager, Logger);
                            SetState(connectingState);

                            if (cmd.RetryAuth)
                            {
                                try
                                {
                                    await Auth.RenewToken();
                                }
                                catch (AblyException ablyException)
                                {
                                    return SetDisconnectedStateCommand.Create(ablyException.ErrorInfo).TriggeredBy(command);
                                }
                            }

                            await ConnectionManager.CreateTransport(connectingHost);

                            break;
                        }
                        catch (AblyException ex)
                        {
                            Logger.Error("Error setting connecting state", ex);

                            // RSA4c2 & RSA4d
                            if (ex.ErrorInfo.Code == ErrorCodes.ClientAuthProviderRequestFailed & !ex.ErrorInfo.IsForbiddenError)
                            {
                                return SetDisconnectedStateCommand.Create(ex.ErrorInfo).TriggeredBy(command);
                            }

                            return HandleConnectingErrorCommand.Create(null, ex);
                        }

                    case SetFailedStateCommand cmd:

                        ClearAckQueueAndFailMessages(ErrorInfo.ReasonFailed);

                        var error = TransformIfTokenErrorAndNotRetryable();
                        var failedState = new ConnectionFailedState(ConnectionManager, error, Logger);
                        SetState(failedState);
                        State.Connection.ClearKeyAndId(); // RTN8c, RTN9c

                        ConnectionManager.DestroyTransport();

                        ErrorInfo TransformIfTokenErrorAndNotRetryable()
                        {
                            if (cmd.Error.IsTokenError && Auth.TokenRenewable == false)
                            {
                                var newError = ErrorInfo.NonRenewableToken;
                                newError.Message += $" Original: {cmd.Error.Message} ({cmd.Error.Code})";
                                return newError;
                            }

                            return cmd.Error;
                        }

                        break;
                    case SetDisconnectedStateCommand cmd:

                        if (cmd.ClearConnectionKey)
                        {
                            State.Connection.ClearKey();
                        }

                        var retryInstantly = await CheckInstantRetryFlag();

                        var disconnectedState = new ConnectionDisconnectedState(ConnectionManager, cmd.Error, Logger)
                        {
                            RetryInstantly = retryInstantly,
                            Exception = cmd.Exception,
                        };

                        SetState(disconnectedState, skipTimer: cmd.SkipAttach);

                        // RTN7d
                        if (Client.Options.QueueMessages == false)
                        {
                            var failAckMessages = new ErrorInfo(
                                "Clearing message AckQueue(created at connected state) because Options.QueueMessages is false",
                                ErrorCodes.Disconnected,
                                HttpStatusCode.BadRequest,
                                null,
                                cmd.Error);
                            ClearAckQueueAndFailMessages(failAckMessages);
                        }

                        if (cmd.SkipAttach == false)
                        {
                            ConnectionManager.DestroyTransport();
                        }

                        if (retryInstantly)
                        {
                            return SetConnectingStateCommand.Create().TriggeredBy(command);
                        }

                        async Task<bool> CheckInstantRetryFlag()
                        {
                            if (cmd.RetryInstantly)
                            {
                                return true;
                            }

                            if ((cmd.Error != null && cmd.Error.IsRetryableStatusCode()) || cmd.Exception != null)
                            {
                                return await Client.RestClient.CanConnectToAbly();
                            }

                            return false;
                        }

                        break;

                    case SetClosingStateCommand _:

                        var transport = ConnectionManager.Transport;
                        var connectedTransport = transport?.State == TransportState.Connected;

                        var closingState = new ConnectionClosingState(ConnectionManager, connectedTransport, Logger);
                        SetState(closingState);
                        State.Connection.ClearKeyAndId(); // RTN8c, RTN9c

                        if (connectedTransport)
                        {
                            return SendMessageCommand.Create(new ProtocolMessage(ProtocolMessage.MessageAction.Close), force: true).TriggeredBy(command);
                        }
                        else
                        {
                            return SetClosedStateCommand.Create().TriggeredBy(command);
                        }

                    case SetSuspendedStateCommand cmd:

                        if (cmd.ClearConnectionKey)
                        {
                            State.Connection.ClearKey();
                        }

                        ClearAckQueueAndFailMessages(ErrorInfo.ReasonSuspended);

                        var suspendedState = new ConnectionSuspendedState(ConnectionManager, cmd.Error, Logger);
                        SetState(suspendedState);
                        State.Connection.ClearKeyAndId(); // RTN8c, RTN9c

                        break;

                    case SetClosedStateCommand cmd:

                        ClearAckQueueAndFailMessages(ErrorInfo.ReasonClosed);

                        var closedState = new ConnectionClosedState(ConnectionManager, cmd.Error, Logger)
                        {
                            Exception = cmd.Exception,
                        };

                        SetState(closedState);
                        State.Connection.ClearKeyAndId(); // RTN8c, RTN9c

                        ConnectionManager.DestroyTransport();

                        break;
                }
            }
            catch (AblyException ex)
            {
                Logger.Error($"Error executing set state command {command.Name}", ex);

                if (command is SetFailedStateCommand == false)
                {
                    return SetFailedStateCommand.Create(ex.ErrorInfo).TriggeredBy(command);
                }
            }

            return EmptyCommand.Instance;
        }

        public void SetState(ConnectionStateBase newState, bool skipTimer = false)
        {
            if (Logger.IsDebug)
            {
                var message = $"Changing state from {State.Connection.State} => {newState.State}.";
                if (skipTimer)
                {
                    message += " Skip timer";
                }

                Logger.Debug(message);
            }

            try
            {
                if (newState.IsUpdate == false)
                {
                    if (State.Connection.State == newState.State)
                    {
                        if (Logger.IsDebug)
                        {
                            Logger.Debug($"State is already {State.Connection.State}. Skipping SetState action.");
                        }

                        return;
                    }

                    State.AttemptsInfo.UpdateAttemptState(newState, Logger);
                    State.Connection.CurrentStateObject.AbortTimer();
                }

                if (skipTimer == false)
                {
                    newState.StartTimer();
                }
                else if (Logger.IsDebug)
                {
                    Logger.Debug($"xx {newState.State}: Skipping attaching.");
                }

                UpdateStateAndNotifyConnection(newState);
            }
            catch (AblyException ex)
            {
                Logger.Error("Error attaching to context", ex);

                UpdateStateAndNotifyConnection(newState);

                newState.AbortTimer();

                throw;
            }
        }

        private void UpdateStateAndNotifyConnection(ConnectionStateBase newState)
        {
            var change = State.Connection.UpdateState(newState, Logger);
            if (change != null)
            {
                Connection.NotifyUpdate(change);
            }
        }

        private void SendPendingMessagesOnConnected(bool failedResumeOrRecover)
        {
            // RTN19a1
            if (failedResumeOrRecover)
            {
                foreach (var messageAndCallback in State.WaitingForAck)
                {
                    State.PendingMessages.Add(new MessageAndCallback(
                        messageAndCallback.Message,
                        messageAndCallback.Callback,
                        messageAndCallback.Logger));
                }
            }
            else
            {
                // RTN19a2 - successful resume, msgSerial doesn't change
                foreach (var message in State.WaitingForAck.Select(x => x.Message))
                {
                    ConnectionManager.SendToTransport(message);
                }
            }

            if (Logger.IsDebug && State.PendingMessages.Count > 0)
            {
                Logger.Debug("Sending pending message: Count: " + State.PendingMessages.Count);
            }

            foreach (var pendingMessage in State.PendingMessages)
            {
                var sendResult = SendMessage(pendingMessage.Message, pendingMessage.Callback);
                if (sendResult.IsFailure)
                {
                    Logger.Warning($"Error sending pending message with ID: {pendingMessage.Message.Id}. Action: {pendingMessage.Message?.Action}");
                }
            }

            State.PendingMessages.Clear();
        }

        private void ClearAckQueueAndFailMessages(ErrorInfo error)
        {
            foreach (var item in State.WaitingForAck.Where(x => x.Callback != null))
            {
                var messageError = error ?? ErrorInfo.ReasonUnknown;
                item.SafeExecute(false, messageError);
            }

            State.WaitingForAck.Clear();
        }

        public void QueueAck(ProtocolMessage message, Action<bool, ErrorInfo> callback)
        {
            if (message.AckRequired)
            {
                State.WaitingForAck.Add(new MessageAndCallback(message, callback));
                if (Logger.IsDebug)
                {
                    Logger.Debug($"Message ({message.Action}) with serial ({message.MsgSerial}) was queued to get Ack");
                }
            }
        }

        internal Task<bool> HandleAckMessage(ProtocolMessage message)
        {
            if (message.Action == ProtocolMessage.MessageAction.Ack ||
                message.Action == ProtocolMessage.MessageAction.Nack)
            {
                var endSerial = message.MsgSerial + (message.Count - 1);
                var listForProcessing = new List<MessageAndCallback>(State.WaitingForAck);
                foreach (var current in listForProcessing)
                {
                    if (current.Serial <= endSerial)
                    {
                        if (message.Action == ProtocolMessage.MessageAction.Ack)
                        {
                            current.SafeExecute(true, null);
                        }
                        else
                        {
                            current.SafeExecute(false, message.Error ?? ErrorInfo.ReasonUnknown);
                        }

                        State.WaitingForAck.Remove(current);
                    }
                }

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public bool IsProcessingCommands()
        {
            var gotCount = TryGetCount(out int count);
            return _processingCommand || (gotCount && count > 0);
        }

        /// <summary>
        /// Attempt to query the backlog length of the queue.
        /// </summary>
        /// <param name="count">The (approximate) count of items in the Channel.</param>
        private bool TryGetCount(out int count)
        {
            // get this using the reflection
            try
            {
                var prop = CommandChannel.GetType()
                    .GetProperty("ItemsCountForDebugger", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null)
                {
                    count = (int)prop.GetValue(CommandChannel);
                    return true;
                }
            }
            catch (Exception e)
            {
                ErrorPolicy.HandleUnexpected(e, Logger);
            }

            count = default(int);
            return false;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _heartbeatMonitorCancellationTokenSource.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
