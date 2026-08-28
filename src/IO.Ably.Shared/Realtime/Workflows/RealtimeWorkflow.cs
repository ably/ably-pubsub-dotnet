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

        // Null until first asked. See ProtocolHeartbeatsNotRequestedByCaller.
        private bool? _protocolHeartbeatsNotRequested;

        private bool _warnedIdleCheckInactive;
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
                        QueueCommand(HeartbeatMonitorCommand.Create(Now()).TriggeredBy("AblyRealtime.HeartbeatMonitor()"));
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
            // Ticks every second, so logging each one would bury everything else at Debug.
            bool shouldLogCommand = !((command is EmptyCommand) || (command is ListCommand) || (command is HeartbeatMonitorCommand));
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
                        return HandleHeartbeatMonitorCommand(cmd);
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

        /// <summary>
        /// RTN23a - a transport silent for longer than maxIdleInterval plus realtimeRequestTimeout
        /// is treated as dead and disconnected. Any inbound message counts as activity, not just
        /// Heartbeats, which is why ProcessMessage refreshes the timestamp rather than the Heartbeat
        /// handler. Data we send does not count.
        /// </summary>
        private IEnumerable<RealtimeCommand> HandleHeartbeatMonitorCommand(HeartbeatMonitorCommand command)
        {
            var connection = State.Connection;

            // Only meaningful while Connected: elsewhere there is no live transport, and
            // ConfirmedAliveAt may still hold a previous transport's timestamp.
            if (connection.State != ConnectionState.Connected)
            {
                _heartbeatMonitorDisconnectRequested = false;

                // Re-armed per transport, since maxIdleInterval is a per-transport promise.
                _warnedIdleCheckInactive = false;
                return Enumerable.Empty<RealtimeCommand>();
            }

            // RTN23b - without protocol heartbeats Ably may satisfy maxIdleInterval with websocket
            // ping frames, which this library cannot observe, leaving nothing to measure.
            if (ProtocolHeartbeatsNotRequestedByCaller())
            {
                return Enumerable.Empty<RealtimeCommand>();
            }

            // No promised idle period to measure against.
            var maxIdleInterval = connection.MaxIdleInterval;
            if (maxIdleInterval.HasValue == false || maxIdleInterval.Value <= TimeSpan.Zero)
            {
                if (_warnedIdleCheckInactive == false)
                {
                    _warnedIdleCheckInactive = true;

                    // Logged because the two causes differ: absent means no CONNECTED carried the
                    // field, zero is CD2h's explicit "arbitrarily-long levels of inactivity".
                    Logger.Debug(
                        maxIdleInterval.HasValue
                            ? "Ably set maxIdleInterval to 0, so it guarantees no inactivity limit. Idle connection detection is off."
                            : "No maxIdleInterval received from Ably, so idle connection detection is off.");
                }

                return Enumerable.Empty<RealtimeCommand>();
            }

            if (connection.ConfirmedAliveAt.HasValue == false)
            {
                return Enumerable.Empty<RealtimeCommand>();
            }

            // Measured to when the tick was queued, not to now. The workflow is a single reader, so
            // work queued ahead of the tick - an inbound AUTH awaiting the application's
            // authCallback - would otherwise be charged to the transport.
            var idleFor = command.QueuedAt - connection.ConfirmedAliveAt.Value;

            // A window that cannot be represented can never elapse. maxIdleInterval is unbounded off
            // the wire, and an OverflowException here would be logged and dropped by the command
            // loop, silently killing detection for the life of the connection.
            if (maxIdleInterval.Value >= TimeSpan.MaxValue - Client.Options.RealtimeRequestTimeout)
            {
                return Enumerable.Empty<RealtimeCommand>();
            }

            // Read live, unlike the two per-transport snapshots above: this term is the caller's own
            // slack rather than anything Ably promised on this transport, its setter validates the
            // range, and it is only ever additive.
            var allowedIdleTime = maxIdleInterval.Value + Client.Options.RealtimeRequestTimeout;

            if (idleFor <= allowedIdleTime)
            {
                _heartbeatMonitorDisconnectRequested = false;
                return Enumerable.Empty<RealtimeCommand>();
            }

            if (_heartbeatMonitorDisconnectRequested)
            {
                return Enumerable.Empty<RealtimeCommand>();
            }

            _heartbeatMonitorDisconnectRequested = true;

            var error = ErrorInfo.NoActivityFrom(idleFor);
            Logger.Warning($"{error.Message} The limit was {allowedIdleTime.TotalSeconds:0.#}s.");

            // RTN15a - a transport we have given up on counts as disconnected unexpectedly, so
            // RTN15h3's immediate reconnect applies. Requested explicitly because NoActivityFrom
            // carries 408, which the instant retry check does not recognise on its own.
            return new RealtimeCommand[]
            {
                SetDisconnectedStateCommand.Create(error, retryInstantly: true).TriggeredBy(command),
            };
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

                        // Never queue a message already awaiting an ACK. One instance in both queues
                        // would be sent twice on reconnect, and SendMessage's second MsgSerial
                        // assignment would renumber the copy WaitingForAck reads live, leaving a hole
                        // in the sequence RTN7b requires to be unique and serially incrementing.
                        //
                        // Unreachable today only by coincidence - AckRequired implies CanSend, and
                        // CanQueue is false in CONNECTED - so the invariant is stated rather than
                        // left to three unrelated facts. ably-js keeps it deliberately, via
                        // MessageQueue's sendAttempted flag.
                        if (sendResult.IsFailure &&
                            cmd.ProtocolMessage.AckRequired == false &&
                            State.Connection.CurrentStateObject.CanQueue &&
                            Client.Options.QueueMessages)
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

                    // Suspending is decided in the SetDisconnectedStateCommand handler, for every path.
                    return SetDisconnectedStateCommand.Create(
                            cmd.Error ?? ErrorInfo.ReasonDisconnected,
                            clearConnectionKey: true)
                        .TriggeredBy(cmd);

                case HandleConnectingErrorCommand cmd:
                    var error = cmd.Error ?? cmd.Exception?.ErrorInfo ?? ErrorInfo.ReasonUnknown;

                    if (error.IsTokenError)
                    {
                        return HandleConnectingTokenErrorCommand.Create(error)
                            .TriggeredBy(cmd);
                    }

                    if (error.IsRetryableStatusCode())
                    {
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

        /// <summary>
        /// Whether the caller's own transportParams entry has displaced ours, leaving RTN23b's
        /// protocol heartbeats unrequested. RTN23b guarantees them only for exactly
        /// `heartbeats=true`; anything else lets Ably use transport-level pings, which
        /// ClientWebSocket does not surface.
        /// </summary>
        /// <returns>true when protocol heartbeats have not been requested.</returns>
        private bool ProtocolHeartbeatsNotRequestedByCaller()
        {
            // Answered once - TransportParams is fixed at client construction.
            if (_protocolHeartbeatsNotRequested.HasValue)
            {
                return _protocolHeartbeatsNotRequested.Value;
            }

            _protocolHeartbeatsNotRequested = ComputeProtocolHeartbeatsNotRequested();
            return _protocolHeartbeatsNotRequested.Value;
        }

        private bool ComputeProtocolHeartbeatsNotRequested()
        {
            var transportParams = Client.Options.TransportParams;
            if (transportParams == null)
            {
                return false;
            }

            // Case-insensitive because DictionaryExtensions.Merge drops our own heartbeats param on a
            // case-insensitive key match, so "Heartbeats" reaches the wire in place of ours.
            var callerEntry = transportParams.FirstOrDefault(x => x.Key.EqualsTo("heartbeats"));
            if (callerEntry.Key == null)
            {
                return false;
            }

            // Two conditions, because Merge has already dropped ours on a case-insensitive key match:
            //  - the value must be "true"; RTN23b guarantees protocol heartbeats only for that, and
            //    treats false or unspecified as permission to use any transport-level mechanism.
            //  - the key must be exactly "heartbeats"; any other spelling is a param Ably ignores,
            //    which reads as unspecified.
            string value;
            try
            {
                value = callerEntry.Value?.ToString();
            }
            catch (Exception ex)
            {
                // Unguarded, a throwing ToString would escape every tick and be dropped by the
                // command loop, killing RTN23a silently. TransportParams.ConvertValue guards it too.
                Logger.Error($"Could not read transportParams['{callerEntry.Key}'] as a string.", ex);
                value = null;
            }

            var keyIsExact = callerEntry.Key.EqualsTo("heartbeats", caseSensitive: true);
            var valueIsTrue = value.EqualsTo("true");
            var disabled = keyIsExact == false || valueIsTrue == false;

            if (disabled)
            {
                // Named separately because the two halves need different fixes.
                var reason = keyIsExact
                    ? $"transportParams sets heartbeats to '{value}', not 'true'."
                    : $"transportParams sets '{callerEntry.Key}'; Ably only reads 'heartbeats'.";

                Logger.Warning(
                    $"{reason} Ably may then keep this connection alive with websocket pings, which " +
                    "this library cannot see, so idle connection detection is off and a silently " +
                    "dropped connection will not be detected. Set heartbeats to 'true' to enable it.");
            }

            return disabled;
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

            // Whether this Connected continues the message serial sequence we are already part of.
            // One that does not must restart at zero per RTN15c7, renumbering anything still
            // awaiting an ACK. Three ways to continue:
            //
            //  - an RTN24 update, which arrives on the connection we already hold;
            //  - a successful resume (RTN15c6), judged on the connectionId alone. RTN15c6's "and no
            //    error property" describes what Ably sends, while the reset belongs to RTN15c7,
            //    which is keyed on "a new connectionId". ably-js discriminates the same way, on
            //    connIdChanged;
            //  - a successful recover, which deliberately adopts a previous connection's counter.
            //    RTN16f initialises it from the recovery key, which carries no connectionId, so
            //    success is judged by the absence of an error.
            //
            // Broader than testing for an error on the message, which a connection cleared per
            // RTN15g would fail: it reconnects fresh, and Ably answers with a new connectionId and
            // no error - exactly the case that most needs a new sequence.
            //
            // Must be evaluated before Update below, which overwrites the id being compared, and
            // before Options.Recover is cleared for RTN16k.
            var isRecoverAttempt = Client.Options.Recover.IsNotEmpty();
            var connectionContinues = cmd.IsUpdate ||
                                      (isRecoverAttempt && cmd.Message.Error == null) ||
                                      (State.Connection.Id.IsNotEmpty() && State.Connection.Id == info.ConnectionId);

            State.Connection.Update(info, cmd.IsUpdate); // RTN16d, RTN15e, RTN23a

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

            // RTN15c7, RTN15g3, RTN11d - a connection that is not a continuation of the one we held
            // restarts the message serial sequence at zero.
            if (connectionContinues == false)
            {
                State.Connection.MessageSerial = 0;
            }

            // The RTL3d reattach lives in RealtimeChannel.ConnectionStateChanged, not here: that
            // handler runs inside NotifyUpdate's internal handlers, so the channel transitions land
            // before CONNECTED reaches external listeners, as RTL3d1 requires.
            SendPendingMessagesOnConnected(connectionContinues, cmd.IsUpdate); // RTN19a
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

                            // RTN17 - every attempt considers a fallback, including the timer driven
                            // ones. Excluding those would lock a client out once the immediate retry
                            // budget is spent, since every remaining attempt is timer driven and so
                            // pinned to the primary.
                            //
                            // Asked speculatively, which costs nothing: GetHost only reads state, and
                            // RTN17i is its job - it returns to the primary whenever the last host
                            // was a fallback.
                            var candidateHost = AttemptsHelpers.GetHost(State, defaultRealtimeHost);
                            var connectingHost = defaultRealtimeHost;

                            // RTN17j - the connectivity check comes before the decision to use an
                            // alternative host. If the internet is unreachable the problem is not this
                            // host, so stay on the primary rather than working through fallbacks that
                            // cannot answer either.
                            //
                            // The answer is carried on the command, so it cannot outlive the decision
                            // it was taken for or be picked up by a CONNECTING another path queued.
                            // Held on the workflow it would have no bound, because the command loop
                            // abandons a nested batch once its depth guard trips.
                            //
                            // Note HandleConnectingTokenError reaches CreateTransport through
                            // AttemptANewConnection without a check - an RTN17j hole this does not
                            // close.
                            var alreadyConfirmed = cmd.ConnectivityConfirmed;

                            if (candidateHost == defaultRealtimeHost ||
                                alreadyConfirmed == true ||
                                (alreadyConfirmed == null && await Client.RestClient.CanConnectToAbly()))
                            {
                                connectingHost = candidateHost;
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

                        // RTN14e - measured here because this is the one place every path into
                        // DISCONNECTED converges. Checking only the two connection-attempt failure
                        // handlers misses the token and auth retry paths, which do not pass through
                        // either: a client whose token source keeps failing would loop CONNECTING and
                        // DISCONNECTED indefinitely without ever suspending.
                        //
                        // Ordered before CheckInstantRetryFlag so that suspending beats retrying.
                        //
                        // SkipAttach is excluded: the caller has already queued the next command, so
                        // diverting would emit SUSPENDED and then immediately CONNECTING.
                        if (cmd.SkipAttach == false && State.ShouldSuspend(Now))
                        {
                            return SetSuspendedStateCommand.Create(
                                    cmd.Error ?? ErrorInfo.ReasonSuspended,
                                    clearConnectionKey: true)
                                .TriggeredBy(command);
                        }

                        if (cmd.ClearConnectionKey)
                        {
                            State.Connection.ClearKey();
                        }

                        bool? connectivityAnswer = null;
                        var retryInstantly = await CheckInstantRetryFlag();

                        var disconnectedState = new ConnectionDisconnectedState(ConnectionManager, cmd.Error, Logger)
                        {
                            RetryInstantly = retryInstantly,
                            Exception = cmd.Exception,
                        };

                        if (cmd.SkipAttach)
                        {
                            // RTN14d - retryIn must be the delay actually waited. skipAttach means
                            // the caller has already queued the next command, so there is no wait,
                            // and StartTimer, which records the real figure, never runs.
                            disconnectedState.RetryIn = TimeSpan.Zero;
                        }

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
                            State.AttemptsInfo.RecordInstantRetry();

                            // Handed to the command returned on the next line, and only to that one.
                            // Both this handler and the CONNECTING one need to know whether the
                            // internet is reachable - one to decide whether to retry now, the other
                            // whether to accept a fallback - and both were asking, on the workflow's
                            // single reader thread, one after the other. Two checks at up to
                            // MaxHttpOpenTimeout each is a loop held for twice as long as it needs to
                            // be on every failing attempt, and it eats into the RTN14e budget this
                            // series worked to make punctual.
                            //
                            // Carried on the command so it cannot go stale or be consumed by anything
                            // else. A timer driven retry carries no answer and takes its own check.
                            return SetConnectingStateCommand.Create(connectivityConfirmed: connectivityAnswer)
                                .TriggeredBy(command);
                        }

                        async Task<bool> CheckInstantRetryFlag()
                        {
                            if (cmd.RetryInstantly)
                            {
                                return true;
                            }

                            // RTN17j sanctions reconnecting immediately, rather than waiting out the
                            // disconnected retry timeout, to work through the fallback domains. It
                            // does not sanction doing so without end, and every failed attempt
                            // produces another DISCONNECTED carrying an exception that qualifies
                            // again - so the traversal is bounded by the number of domains to
                            // traverse. Past that we are in RTN14d, where attempts are periodic and
                            // spaced per RTB1, and host selection continues at that slower pace.
                            var domainCount = 1 + State.Connection.FallbackHosts.Count;
                            if (State.AttemptsInfo.InstantRetryCount >= domainCount)
                            {
                                return false;
                            }

                            // RTN15a and RTN15h3 - an unexpected transport drop or a non-token
                            // DISCONNECTED both earn an immediate reconnect. The first two tests
                            // cover the drop, the third the DISCONNECTED, which carries no status
                            // code of its own.
                            //
                            // Token errors are excluded because RTN15h3 is the "error other than a
                            // token error" clause: RTN15h2 owns them and has already queued its own
                            // CONNECTING behind this command, so granting a retry here too gives two
                            // overlapping attempts.
                            //
                            // Gated on the connectivity check, because the retry this grants is
                            // where host selection happens and RTN17j requires a check before an
                            // alternative host is used.
                            var reconnectImmediately = cmd.Exception != null
                                                       || (cmd.Error != null && cmd.Error.IsRetryableStatusCode())
                                                       || (State.Connection.State == ConnectionState.Connected
                                                           && cmd.Error?.IsTokenError != true);

                            if (reconnectImmediately)
                            {
                                // Remembered so the CONNECTING behind this command does not repeat it.
                                connectivityAnswer = await Client.RestClient.CanConnectToAbly();
                                return connectivityAnswer.Value;
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

                    State.AttemptsInfo.UpdateAttemptState(newState, State.Connection.State, Logger);
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

        private void SendPendingMessagesOnConnected(bool connectionContinues, bool isUpdate)
        {
            // RTN19 is scoped to "when a transport is disconnected for any reason", which puts an
            // RTN24 update outside it: nothing was disconnected, and the transport that will ACK the
            // in-flight messages is the one they went out on. Resending would put a duplicate of each
            // on the wire for Ably to discard by msgSerial, and requeueing would renumber messages
            // the server is still expecting under their original serials. ably-js does not re-drain
            // on a CONNECTED received while already connected either.
            //
            // The RTL6c2 queue below is still flushed. It should be empty while connected, and
            // skipping it would strand anything that did land there.
            if (isUpdate == false)
            {
                if (connectionContinues)
                {
                    // RTN19a2 - the same connection is still expecting the serials these messages were
                    // originally given, so resend them unchanged and leave them awaiting their ACK.
                    foreach (var message in State.WaitingForAck.Select(x => x.Message))
                    {
                        ConnectionManager.SendToTransport(message);
                    }
                }
                else
                {
                    // RTN19a1, RTN19a2 - a different connection means a fresh serial sequence, so
                    // requeue rather than resend. The loop below hands each message to SendMessage,
                    // which assigns a serial from the counter that HandleConnectedCommand has just
                    // reset and re-registers it for its ACK.
                    //
                    // Resending these unchanged would leave the server's sequence sitting at the old
                    // high water mark while ours restarted at zero, and Ably silently discards a
                    // message whose serial is below what it has already seen - no ACK, no NACK, so the
                    // publish callback would never be called at all.
                    //
                    // WaitingForAck is cleared because SendMessage re-registers each message as it
                    // goes; stale entries would hold serials of the old sequence that the next ACK also
                    // matches, running their callbacks twice.
                    //
                    // Inserted at the front, not appended: PendingMessages is the RTL6c2 queue and
                    // already holds anything published while disconnected, which happened *after* these.
                    // Appending would give the newer messages the lower serials and reverse publish
                    // order. ably-js prepends for the same reason.
                    State.PendingMessages.InsertRange(
                        0,
                        State.WaitingForAck.Select(x => new MessageAndCallback(x.Message, x.Callback, x.Logger)));

                    State.WaitingForAck.Clear();
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

        /// <summary>
        /// RTN7e - when the connection enters SUSPENDED, CLOSED or FAILED, everything that has not
        /// been acknowledged has failed and must be reported as such.
        /// </summary>
        private void ClearAckQueueAndFailMessages(ErrorInfo error)
        {
            var messageError = error ?? ErrorInfo.ReasonUnknown;

            foreach (var item in State.WaitingForAck.Where(x => x.Callback != null))
            {
                item.SafeExecute(false, messageError);
            }

            State.WaitingForAck.Clear();

            // RTN7e covers RTL6c2 as well as RTL6c1: a message submitted via either "should be
            // considered failed ... and removed from any RTN19a retry queue". PendingMessages is the
            // RTL6c2 queue.
            foreach (var item in State.PendingMessages.Where(x => x.Callback != null))
            {
                item.SafeExecute(false, messageError);
            }

            State.PendingMessages.Clear();
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
