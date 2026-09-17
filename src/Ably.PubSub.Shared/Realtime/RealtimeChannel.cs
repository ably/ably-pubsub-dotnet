using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IO.Ably.MessageEncoders;
using IO.Ably.Push;
using IO.Ably.Rest;
using IO.Ably.Shared.Utils;
using IO.Ably.Transport;
using IO.Ably.Types;
using IO.Ably.Utils;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Realtime
{
    [DebuggerDisplay("{Name}. State = {_state}. Error = {ErrorReason} ")]
    internal class RealtimeChannel : EventEmitter<ChannelEvent, ChannelStateChange>, IRealtimeChannel
    {
#pragma warning disable CA2213
        private readonly Handlers<Message> _handlers = new Handlers<Message>();
#pragma warning restore CA2213

        private ChannelOptions _options;
        private ChannelState _state;
        private readonly PushChannel _pushChannel;
        private int _retryCount = 0;

        /// <summary>
        /// True when the channel moves to the @ATTACHED@ state, and False
        /// when the channel moves to the @DETACHING@ or @FAILED@ states.
        /// </summary>
        internal bool AttachResume { get; set; }

        private int _decodeRecoveryInProgress;

        // We use interlocked exchange because it is a thread safe way to read a variable
        // without the need of locking. Generally DecodeRecovery is set from a method triggered by
        // the RealtimeWorkflow but it can also be called inside a callback which can potentially
        // triggered on another thread.
        internal bool DecodeRecovery
        {
            get => Interlocked.CompareExchange(ref _decodeRecoveryInProgress, 0, 0) == 1;
            set => Interlocked.Exchange(ref _decodeRecoveryInProgress, value ? 1 : 0);
        }

        internal LastMessageIds LastSuccessfulMessageIds { get; set; } = LastMessageIds.Empty;

        internal DecodingContext MessageDecodingContext { get; private set; }

        public event EventHandler<ChannelStateChange> StateChanged = delegate { };

        internal event EventHandler<ChannelStateChange> InternalStateChanged = delegate { };

        public event EventHandler<ChannelErrorEventArgs> Error = delegate { };

        internal AblyRealtime RealtimeClient { get; }

        private string PreviousConnectionId { get; set; }

        private ConnectionState ConnectionState => Connection.State;

        private IConnectionManager ConnectionManager => RealtimeClient.ConnectionManager;

        private Connection Connection => RealtimeClient.Connection;

        internal IRestChannel RestChannel => RealtimeClient.RestClient.Channels.Get(Name);

        internal ChannelAwaiter AttachedAwaiter { get; }

        internal ChannelAwaiter DetachedAwaiter { get; }

        protected override Action<Action> NotifyClient => RealtimeClient.NotifyExternalClients;

        public ErrorInfo ErrorReason { get; internal set; }

        public ChannelProperties Properties { get; } = new ChannelProperties();

        public ChannelOptions Options
        {
            get => _options;
            set => _options = value ?? new ChannelOptions();
        }

        public ReadOnlyChannelParams Params { get; internal set; } = new ReadOnlyChannelParams(new Dictionary<string, string>());

        public ReadOnlyChannelModes Modes { get; internal set; } = new ReadOnlyChannelModes(new List<ChannelMode>());

        public string Name { get; }

        public ChannelState State
        {
            get => _state;
            internal set
            {
                if (value != _state)
                {
                    PreviousState = _state;
                    _state = value;
                }
            }
        }

        internal ChannelState PreviousState { get; set; }

        public Presence Presence { get; }

        /// <inheritdoc />
        public PushChannel Push
        {
            get
            {
                if (_pushChannel is null)
                {
                    Logger.Warning("The current device is does not support or is not configured for Push notifications.");
                    return null;
                }

                return _pushChannel;
            }
        }

        internal RealtimeChannel(
            string name,
            string clientId,
            AblyRealtime realtimeClient,
            ChannelOptions options = null,
            IMobileDevice mobileDevice = null)
            : base(options?.Logger)
        {
            Name = name;
            Options = options;
            MessageDecodingContext = new DecodingContext(options);
            Presence = new Presence(realtimeClient.ConnectionManager, this, clientId, Logger);
            RealtimeClient = realtimeClient;
            State = ChannelState.Initialized;
            AttachedAwaiter = new ChannelAwaiter(this, ChannelState.Attached, Logger, OnAttachTimeout);
            DetachedAwaiter = new ChannelAwaiter(this, ChannelState.Detached, Logger, OnDetachTimeout);

            if (mobileDevice != null)
            {
                _pushChannel = new PushChannel(name, realtimeClient.RestClient);
            }
        }

        internal void ConnectionStateChanged(ConnectionStateChange connectionStateChange)
        {
            var connectionRefreshed = PreviousConnectionId != Connection.Id;
            if (connectionRefreshed)
            {
                PreviousConnectionId = Connection.Id;
            }

            switch (connectionStateChange.Current)
            {
                case ConnectionState.Connected:
                    if (State == ChannelState.Suspended)
                    {
                        Attach();
                    }

                    if (State == ChannelState.Attaching && AttachedAwaiter.Waiting == false)
                    {
                        Attach(null, emitUpdate: true);
                    }

                    /*
                     * Connection state is only maintained server-side for a brief period,
                     * given by the connectionStateTtl in the connectionDetails (2 minutes at time of writing, see CD2f).
                     * If a client has been disconnected for longer than the connectionStateTtl
                     * it should clear the local connection state and any connection attempts should be made as for a fresh connection
                     *
                     * (RTN15g3) When a connection attempt succeeds after the connection state has been cleared in this way,
                     * channels that were previously ATTACHED, ATTACHING, or SUSPENDED must be automatically reattached,
                     * just as if the connection was a resume attempt which failed per RTN15c3
                     *
                     * Given the above, if the channel is ATTACHED and the connection is fresh
                     * then set the channel to ATTACHING to trigger an ATTACH attempt
                     */
                    if (State == ChannelState.Attached && connectionRefreshed)
                    {
                        Attach(null, force: true, emitUpdate: false);
                    }

                    if (State == ChannelState.Detaching && DetachedAwaiter.Waiting == false)
                    {
                        Detach(null, force: true, emitUpdate: true);
                    }

                    break;
                case ConnectionState.Disconnected:
                    AttachedAwaiter.Fail(new ErrorInfo("Connection is Disconnected"));
                    DetachedAwaiter.Fail(new ErrorInfo("Connection is Disconnected"));
                    break;
                case ConnectionState.Closed:
                    AttachedAwaiter.Fail(new ErrorInfo("Connection is closed"));
                    DetachedAwaiter.Fail(new ErrorInfo("Connection is closed"));
                    if (State == ChannelState.Attached || State == ChannelState.Attaching)
                    {
                        Detach();
                    }

                    break;
                case ConnectionState.Suspended:
                    AttachedAwaiter.Fail(new ErrorInfo("Connection is suspended"));
                    DetachedAwaiter.Fail(new ErrorInfo("Connection is suspended"));
                    if (State == ChannelState.Attached || State == ChannelState.Attaching)
                    {
                        SetChannelState(ChannelState.Suspended, ErrorInfo.ReasonSuspended);
                    }

                    break;
                case ConnectionState.Failed:
                    if (State != ChannelState.Detached && State != ChannelState.Initialized &&
                        State != ChannelState.Failed)
                    {
                        SetChannelState(ChannelState.Failed, connectionStateChange.Reason ?? ErrorInfo.ReasonFailed);
                    }

                    break;
            }
        }

        public void Attach(Action<bool, ErrorInfo> callback = null)
        {
            Attach(null, null, callback);
        }

        internal void Attach(
            ErrorInfo error,
            ProtocolMessage msg = null,
            Action<bool, ErrorInfo> callback = null,
            bool force = false,
            bool emitUpdate = false)
        {
            if (force == false)
            {
                if (State == ChannelState.Attached)
                {
                    ActionUtils.SafeExecute(() => callback?.Invoke(true, null), Logger, $"{Name}-Attach()");
                    return;
                }

                /* TODO: Handle RTL4h where Attach operation should be queued if another attach is in progress. */
            }

            if (IsInStateThatShouldFailAttach())
            {
                var connectionState = RealtimeClient.State.Connection.CurrentStateObject;
                var connectionStateError = connectionState.Error ?? connectionState.DefaultErrorInfo;
                ActionUtils.SafeExecute(() => callback?.Invoke(
                    false,
                    new ErrorInfo(
                        $"Cannot attach when connection is in {ConnectionState} state",
                        ErrorCodes.ChannelOperationFailed,
                        cause: connectionStateError)));
                return;
            }

            var actualError = error == null && msg?.Error != null ? msg.Error : error;
            SetChannelState(ChannelState.Attaching, actualError, msg, emitUpdate);

            if (AttachedAwaiter.StartWait(callback, ConnectionManager.Options.RealtimeRequestTimeout, force))
            {
                if (ConnectionState == ConnectionState.Initialized)
                {
                    Connection.Connect();
                }

                var protocolMessage = CreateAttachMessage();

                SendMessage(protocolMessage);
            }

            // RTL4b States that should fail an Attach call
            bool IsInStateThatShouldFailAttach()
            {
                return ConnectionState == ConnectionState.Closed ||
                        ConnectionState == ConnectionState.Closing ||
                        ConnectionState == ConnectionState.Failed ||
                        ConnectionState == ConnectionState.Suspended;
            }

            ProtocolMessage CreateAttachMessage()
            {
                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Attach, Name);

                // RTL4c1
                message.ChannelSerial = Properties.ChannelSerial;

                if (DecodeRecovery && LastSuccessfulMessageIds != LastMessageIds.Empty)
                {
                    message.ChannelSerial = LastSuccessfulMessageIds.ProtocolMessageChannelSerial; // Excludes PresenceMessage ChannelSerial (not included in backlogs anyways)
                }

                if (Options.Params.Any())
                {
                    message.Params = Options.Params;
                }

                if (Options.Modes.Any())
                {
                    message.SetModesAsFlags(Options.Modes);
                }

                if (AttachResume)
                {
                    message.SetFlag(ProtocolMessage.Flag.AttachResume);
                }

                return message;
            }
        }

        public async Task<Result> AttachAsync()
        {
            return await TaskWrapper.Wrap(Attach);
        }

        internal void StartDecodeFailureRecovery(ErrorInfo reason)
        {
            Logger.Debug("DecodeRecovery: Starting decode failure recovery.");
            if (DecodeRecovery)
            {
                Logger.Warning("Decode recovery already in progress. Skipping ...");
                return;
            }

            DecodeRecovery = true;

            Attach(
                error: reason,
                msg: null,
                callback: (success, error) =>
                {
                    if (success)
                    {
                        Logger.Debug("DecodeRecovery: Successfully recovered from a decode failure.");
                    }
                    else
                    {
                        Logger.Debug("DecodeRecovery: Failed to recover from decode failure.");
                    }

                    DecodeRecovery = false;
                },
                force: true);
        }

        private void OnAttachTimeout()
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"#{Name} didn't Attach within {ConnectionManager.Options.RealtimeRequestTimeout}. Setting state to {ChannelState.Suspended}");
            }

            // RTL4f
            SetChannelState(ChannelState.Suspended, new ErrorInfo($"Channel didn't attach within  {ConnectionManager.Options.RealtimeRequestTimeout}", ErrorCodes.ChannelOperationFailedNoServerResponse, HttpStatusCode.RequestTimeout));
        }

        private void OnDetachTimeout()
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"#{Name} didn't Detach within {ConnectionManager.Options.RealtimeRequestTimeout}. Setting state back to {PreviousState}");
            }

            SetChannelState(PreviousState, new ErrorInfo("Channel didn't detach within the default timeout", ErrorCodes.InternalError));
        }

        public void Detach(Action<bool, ErrorInfo> callback = null)
        {
            Detach(callback, force: false, emitUpdate: false);
        }

        private void Detach(Action<bool, ErrorInfo> callback, bool force, bool emitUpdate)
        {
            if (force == false && (State == ChannelState.Initialized || State == ChannelState.Detaching ||
                State == ChannelState.Detached))
            {
                ActionUtils.SafeExecute(() => callback?.Invoke(true, null), Logger, $"{Name}-Detach()");
                return;
            }

            if (State == ChannelState.Failed)
            {
                Logger.Warning("Cannot Detach channel because it is in Failed state.");
                var error = new ErrorInfo("Cannot Detach channel because it is in Failed state.", ErrorCodes.ChannelOperationFailed);
                ActionUtils.SafeExecute(() => callback?.Invoke(false, error), Logger, $"{Name}-Detach()");
                return;
            }

            if (DetachedAwaiter.StartWait(callback, ConnectionManager.Options.RealtimeRequestTimeout, force))
            {
                SetChannelState(ChannelState.Detaching, emitUpdate);

                if (ConnectionState == ConnectionState.Closed || ConnectionState == ConnectionState.Connecting ||
                    ConnectionState == ConnectionState.Suspended)
                {
                    SetChannelState(ChannelState.Detached);
                }
                else if (ConnectionState != ConnectionState.Failed)
                {
                    SendMessage(new ProtocolMessage(ProtocolMessage.MessageAction.Detach, Name));
                }
                else
                {
                    Logger.Warning($"#{Name}. Cannot send Detach messages when connection is in Failed State");
                }
            }
            else
            {
                Logger.Debug("Detach is already happening. Channel: " + Name);
            }
        }

        public async Task<Result> DetachAsync()
        {
            return await TaskWrapper.Wrap(Detach);
        }

        public void SetOptions(ChannelOptions options, Action<bool, ErrorInfo> callback = null)
        {
            // We need to make the check before we replace the options
            var shouldReAttach = ShouldReAttach(options);
            Options = options;
            if (shouldReAttach)
            {
                Attach(null, null, callback, force: true);
            }
            else
            {
                ActionUtils.SafeExecute(() => callback?.Invoke(true, null), Logger, "SetOptions - no need to attach");
            }
        }

        public async Task<Result> SetOptionsAsync(ChannelOptions options)
        {
            void Action(Action<bool, ErrorInfo> callback) => SetOptions(options, callback);

            return await TaskWrapper.Wrap(Action);
        }

        public void Subscribe(Action<Message> handler)
        {
            if (State != ChannelState.Attached && State != ChannelState.Attaching)
            {
                Attach();
            }

            _handlers.Add(new MessageHandlerAction<Message>(handler));
        }

        public void Subscribe(string eventName, Action<Message> handler)
        {
            if (State != ChannelState.Attached && State != ChannelState.Attaching)
            {
                Attach();
            }

            _handlers.Add(eventName, handler.ToHandlerAction());
        }

        public void Unsubscribe(Action<Message> handler)
        {
            _handlers.Remove(handler.ToHandlerAction());
        }

        public void Unsubscribe(string eventName, Action<Message> handler)
        {
            _handlers.Remove(eventName, handler.ToHandlerAction());
        }

        public void Unsubscribe()
        {
            _handlers.RemoveAll();
        }

        public void Publish(string name, object data, Action<bool, ErrorInfo> callback = null, string clientId = null)
        {
            PublishImpl(new[] { new Message(name, data, clientId) }, callback);
        }

        /// <summary>Publish a single message on this channel based on a given event name and payload.</summary>
        public Task<Result> PublishAsync(string name, object data, string clientId = null)
        {
            return PublishAsync(new[] { new Message(name, data, clientId) });
        }

        public void Publish(Message message, Action<bool, ErrorInfo> callback = null)
        {
            Publish(new[] { message }, callback);
        }

        public Task<Result> PublishAsync(Message message)
        {
            return PublishAsync(new[] { message });
        }

        /// <summary>Publish several messages on this channel.</summary>
        public void Publish(IEnumerable<Message> messages, Action<bool, ErrorInfo> callback = null)
        {
            PublishImpl(messages, callback);
        }

        /// <summary>Publish several messages on this channel.</summary>
        public async Task<Result> PublishAsync(IEnumerable<Message> messages)
        {
            var tw = new TaskWrapper();
            try
            {
                PublishImpl(messages, tw.Callback);
            }
            catch (Exception ex)
            {
                tw.SetException(ex);
            }

            var failResult = Result.Fail(new ErrorInfo("PublishAsync timeout expired. Message was not confirmed by the server"));
            return await tw.Task.TimeoutAfter(RealtimeClient.Options.RealtimeRequestTimeout, failResult);
        }

        public Task<PaginatedResult<Message>> HistoryAsync()
        {
            var query = new PaginatedRequestParams();
            return RestChannel.HistoryAsync(query);
        }

        public Task<PaginatedResult<Message>> HistoryAsync(bool untilAttach)
        {
            var query = new PaginatedRequestParams();
            if (untilAttach)
            {
                AddUntilAttachParameter(query);
            }

            return RestChannel.HistoryAsync(query);
        }

        public Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query)
        {
            query = query ?? new PaginatedRequestParams();
            return RestChannel.HistoryAsync(query);
        }

        public Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query, bool untilAttach)
        {
            query = query ?? new PaginatedRequestParams();
            if (untilAttach)
            {
                AddUntilAttachParameter(query);
            }

            return RestChannel.HistoryAsync(query);
        }

        public void OnError(ErrorInfo error)
        {
            ErrorReason = error; // Set or clear the error

            if (error != null)
            {
                RealtimeClient.NotifyExternalClients(() => Error?.Invoke(this, new ChannelErrorEventArgs(error)));
            }
        }

        internal void RemoveAllListeners()
        {
            AttachedAwaiter?.Dispose();
            DetachedAwaiter?.Dispose();
            _handlers.RemoveAll();
            Presence?.RemoveAllListeners();
            StateChanged = null;
            Error = null;
            InternalStateChanged = null;
        }

        internal void AddUntilAttachParameter(PaginatedRequestParams query)
        {
            if (State != ChannelState.Attached)
            {
                throw new AblyException("Channel is not attached. Cannot use untilAttach parameter");
            }

            query.ExtraParameters.Add("fromSerial", Properties.AttachSerial);
        }

        private void PublishImpl(IEnumerable<Message> messages, Action<bool, ErrorInfo> callback)
        {
            if (State == ChannelState.Suspended || State == ChannelState.Failed)
            {
                throw new AblyException(new ErrorInfo($"Unable to publish in {State} state", ErrorCodes.BadRequest, HttpStatusCode.BadRequest));
            }

            if (!Connection.CanPublishMessages)
            {
                throw new AblyException(new ErrorInfo($"Message cannot be published. Client is not allowed to queue messages when connection is in {State} state", ErrorCodes.BadRequest, HttpStatusCode.BadRequest));
            }

            var msg = new ProtocolMessage(ProtocolMessage.MessageAction.Message, Name)
            {
                Messages = messages.ToArray(),
            };

            SendMessage(msg, callback);
        }

        internal void SetChannelState(ChannelState state, ProtocolMessage protocolMessage)
        {
            SetChannelState(state, protocolMessage?.Error, protocolMessage);
        }

        private void SetChannelState(ChannelState state, bool emitUpdate)
        {
            SetChannelState(state, null, null, emitUpdate);
        }

        internal void SetChannelState(ChannelState state, ErrorInfo error = null, ProtocolMessage protocolMessage = null, bool emitUpdate = false)
        {
            if (Logger.IsDebug)
            {
                var errorMessage = error != null ? "Error: " + error : string.Empty;
                Logger.Debug($"#{Name}: Changing state to: '{state}'. {errorMessage}");
            }

            OnError(error);

            // never emit a ChannelState ChannelEvent for a state equal to the previous state (RTL2g)
            if (!emitUpdate && State == state)
            {
                Logger.Debug($"#{Name}: Duplicate state '{state}' received, not updating.");
                return;
            }

            ChannelEvent channelEvent = emitUpdate ? ChannelEvent.Update : (ChannelEvent)state;

            var channelStateChange = new ChannelStateChange(channelEvent, state, State, error, protocolMessage);
            HandleStateChange(state, error, protocolMessage);

            InternalStateChanged?.Invoke(this, channelStateChange);

            // Notify external client using the thread they subscribe on
            RealtimeClient.NotifyExternalClients(() =>
            {
                try
                {
                    StateChanged?.Invoke(this, channelStateChange);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error notifying event handlers for state change: {state}", ex);
                }

                Emit(channelEvent, channelStateChange);
            });
        }

        private void HandleStateChange(ChannelState state, ErrorInfo error, ProtocolMessage protocolMessage)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"HandleStateChange state change from {State} to {state}");
            }

            // RTP5a1
            if (state == ChannelState.Detached || state == ChannelState.Suspended || state == ChannelState.Failed)
            {
                Properties.ChannelSerial = null;
            }

            var previousState = State;
            State = state;

            switch (state)
            {
                case ChannelState.Attaching:
                    DetachedAwaiter.Fail(new ErrorInfo("Channel transitioned to Attaching", ErrorCodes.InternalError));
                    break;
                case ChannelState.Detaching:
                    AttachedAwaiter.Fail(new ErrorInfo("Channel transitioned to detaching", ErrorCodes.InternalError));
                    AttachResume = false;
                    break;
                case ChannelState.Attached:
                    _retryCount = 0;
                    AttachResume = true;
                    break;
                case ChannelState.Detached:
                    /* RTL13a check for unexpected detach */
                    switch (previousState)
                    {
                        /* (RTL13a) If the channel is in the @ATTACHED@ or @SUSPENDED@ states,
                         an attempt to reattach the channel should be made immediately */
                        case ChannelState.Attached:
                        case ChannelState.Suspended:
                            // TODO: Cleanup - I don't think we need that
                            // SetChannelState(ChannelState.Detached, error, protocolMessage);
                            Reattach(error, protocolMessage);
                            break;

                        case ChannelState.Attaching:
                            // Since attachtimeout will transition state to suspended, no need to suspend it twice
                            AttachedAwaiter.Fail(new ErrorInfo("Channel transitioned to suspended", ErrorCodes.InternalError));
                            /* RTL13b says we need to become suspended, but continue to retry */
                            Logger.Debug($"Server initiated detach for channel {Name} whilst attaching; moving to suspended");
                            SetChannelState(ChannelState.Suspended, error, protocolMessage);
                            ReattachAfterTimeout(error, protocolMessage);
                            break;

                        case ChannelState.Initialized:
                        case ChannelState.Detaching:
                        case ChannelState.Detached:
                        case ChannelState.Failed:
                            // Nothing to do here.
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    Presence.ChannelDetachedOrFailed(error);

                    break;
                case ChannelState.Failed:
                    _retryCount = 0;
                    AttachResume = false;
                    AttachedAwaiter.Fail(error);
                    DetachedAwaiter.Fail(error);
                    Presence.ChannelDetachedOrFailed(error);
                    break;
                case ChannelState.Suspended:
                    Presence.ChannelSuspended(error);
                    break;
            }
        }

        private void Reattach(ErrorInfo error, ProtocolMessage msg)
        {
            try
            {
                Attach(error, msg, (success, info) =>
                {
                    if (!success)
                    {
                        // If the attach timed out the channel will set SUSPENDED (as described by RTL4f and RTL13b)
                        if (State == ChannelState.Suspended)
                        {
                            ReattachAfterTimeout(error, msg);
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Error("Reattach channel failed; channel = " + Name, e);
            }
        }

        /// <summary>
        /// should only be called when the channel gets into SUSPENDED.
        /// RTL13b.
        /// </summary>
        private void ReattachAfterTimeout(ErrorInfo error, ProtocolMessage msg)
        {
            _retryCount++;

            var retryTimeout = TimeSpan.FromMilliseconds(ReconnectionStrategy.
                GetRetryTime(RealtimeClient.Options.ChannelRetryTimeout.TotalMilliseconds, _retryCount));

            // We capture the task but ignore it to make sure an error doesn't take down
            // the thread
            _ = Task.Run(async () =>
            {
                await Task.Delay(retryTimeout);

                // only retry if the connection is connected (RTL13c)
                if (Connection.State == ConnectionState.Connected)
                {
                    Reattach(error, msg);
                }
            }).ConfigureAwait(false);
        }

        internal void OnMessage(Message message)
        {
            var channelHandlers = _handlers.GetHandlers().ToList();
            if (Logger.IsDebug)
            {
                Logger.Debug($"Notifying {channelHandlers.Count} handlers in #{Name} channel");
            }

            foreach (var handler in channelHandlers)
            {
                var loopHandler = handler;
                RealtimeClient.NotifyExternalClients(() => loopHandler.SafeHandle(message, Logger));
            }

            if (message.Name.IsNotEmpty())
            {
                var namedHandlers = _handlers.GetHandlers(message.Name).ToList();
                if (Logger.IsDebug)
                {
                    Logger.Debug($"Notifying {namedHandlers.Count} handlers for messages {message.Name} in #{Name} channel");
                }

                foreach (var specificHandler in namedHandlers)
                {
                    var loopHandler = specificHandler;
                    RealtimeClient.NotifyExternalClients(() => loopHandler.SafeHandle(message, Logger));
                }
            }
        }

        private void SendMessage(ProtocolMessage protocolMessage, Action<bool, ErrorInfo> callback = null)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"RealtimeChannel.SendMessage:{protocolMessage.Action}");
            }

            ConnectionManager.Send(protocolMessage, callback, Options);
        }

        internal void EmitErrorUpdate(ErrorInfo errorInfo, bool resumed, ProtocolMessage message = null)
        {
            Emit(ChannelEvent.Update, new ChannelStateChange(ChannelEvent.Update, State, State, errorInfo, resumed)
            {
                ProtocolMessage = message,
            });
        }

        internal bool ShouldReAttach(ChannelOptions options)
        {
            bool isAttachedOrAttaching = State == ChannelState.Attached || State == ChannelState.Attaching;
            bool hasModesWhichAreDifferentThanCurrentOptions =
                (options.Modes.All(x => _options.Modes.Contains(x)) &&
                _options.Modes.All(x => options.Modes.Contains(x))) == false;
            bool hasParamsWhichAreDifferentThanCurrentOptions =
                (options.Params.All(x => _options.Params.Contains(x))
                && _options.Params.All(x => options.Params.Contains(x))) == false;

            return isAttachedOrAttaching && (hasModesWhichAreDifferentThanCurrentOptions || hasParamsWhichAreDifferentThanCurrentOptions);
        }

        public JObject GetCurrentState()
        {
            var state = new JObject
            {
                ["name"] = Name,
                ["options"] = JObject.FromObject(_options),
                ["state"] = JToken.FromObject(_state),
                ["timers"] = JObject.FromObject(new
                {
                    awaitTimer = AttachedAwaiter.Waiting,
                    detachTimer = DetachedAwaiter.Waiting,
                }),
                ["emitters"] = GetState(),
                ["handlers"] = _handlers.GetState(),
                ["presence"] = Presence.GetState(),
            };
            return state;
        }
    }
}
