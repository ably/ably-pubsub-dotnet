using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IO.Ably.Transport;
using IO.Ably.Types;
using IO.Ably.Utils;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// A class that provides access to presence operations and state for the associated Channel.
    /// </summary>
    public sealed partial class Presence : IDisposable
    {
        private readonly RealtimeChannel _channel;
        private readonly string _clientId;
        private readonly Handlers<PresenceMessage> _handlers = new Handlers<PresenceMessage>();
        private readonly IConnectionManager _connection;
        private string _currentSyncChannelSerial;
        private bool _disposedValue;

        internal Presence(IConnectionManager connection, RealtimeChannel channel, string clientId, ILogger logger)
        {
            Logger = logger;
            MembersMap = new PresenceMap(channel.Name, logger);
            InternalMembersMap = new InternalPresenceMap(channel.Name, logger);
            PendingPresenceQueue = new ConcurrentQueue<QueuedPresenceMessage>();
            _connection = connection;
            _channel = channel;
            _clientId = clientId;
        }

        internal event EventHandler SyncCompletedEventHandler;

        internal ILogger Logger { get; private set; }

        /// <summary>
        /// Checks if presence sync has ended.
        /// </summary>
        ///
        [Obsolete("This property is deprecated, use SyncComplete instead")]
        public bool IsSyncComplete => SyncComplete; // RTP13.

        /// <summary>
        /// Checks if presence sync has ended.
        /// </summary>
        ///
        public bool SyncComplete => MembersMap.SyncCompleted && !SyncInProgress; // RTP13

        /// <summary>
        /// Indicates whether there is currently a sync in progress.
        /// </summary>
        [Obsolete("This property is internal, will be removed in the future")]
        public bool IsSyncInProgress => SyncInProgress;

        /// <summary>
        /// Indicates whether there is currently a sync in progress.
        /// </summary>
        internal bool SyncInProgress => MembersMap.SyncInProgress;

        /// <summary>
        /// Indicates all members present on the channel.
        /// </summary>
        internal PresenceMap MembersMap { get; } // RTP2

        /// <summary>
        /// Indicates members belonging to current connectionId.
        /// </summary>
        internal PresenceMap InternalMembersMap { get; } // RTP17

        internal ConcurrentQueue<QueuedPresenceMessage> PendingPresenceQueue { get; }

        /// <summary>
        /// Disposes the current Presence instance. Removes all listening handlers.
        /// </summary>
        internal void RemoveAllListeners()
        {
            SyncCompletedEventHandler = null;
            _handlers.RemoveAll();
        }

        /// <summary>
        ///     Get the presence state given a set of options <see cref="GetParams"/>. Implicitly attaches the Channel.
        ///     However, if the channel is in or moves to the FAILED.
        ///     state before the operation succeeds, it will result in an error.
        /// </summary>
        /// <param name="options">Options for the GetAsync. For details <see cref="GetParams"/>.</param>
        /// <returns>a list of PresenceMessages.</returns>
        public async Task<IEnumerable<PresenceMessage>> GetAsync(GetParams options)
        {
            // RTP11b
            if (_channel.State == ChannelState.Failed || _channel.State == ChannelState.Detached)
            {
                throw new AblyException(new ErrorInfo($"channel operation failed. Invalid channel state ({_channel.State})", ErrorCodes.ChannelOperationFailedWithInvalidState));
            }

            if (_channel.State == ChannelState.Initialized)
            {
                _channel.Attach();
            }

            var getOptions = options ?? new GetParams();

            if (getOptions.WaitForSync)
            {
                // RTP11d
                if (_channel.State == ChannelState.Suspended)
                {
                    throw new AblyException(new ErrorInfo($"Channel {_channel.Name}: presence state is out of sync due to the channel being in a SUSPENDED state", 91005));
                }

                _ = await WaitForSyncAsync();
            }

            var result = MembersMap.Values.Where(x => (getOptions.ClientId.IsEmpty() || x.ClientId == getOptions.ClientId)
                                               && (getOptions.ConnectionId.IsEmpty() || x.ConnectionId == getOptions.ConnectionId));
            return result;
        }

        /// <summary>
        ///     Get the presence state for the current channel, optionally waiting for Sync to complete.
        ///     Implicitly attaches the Channel. However, if the channel is in or moves to the FAILED.
        ///     state before the operation succeeds, it will result in an error.
        /// </summary>
        /// <param name="waitForSync">whether it should wait for a sync to complete.</param>
        /// <returns>the current present members.</returns>
        public async Task<IEnumerable<PresenceMessage>> GetAsync(bool waitForSync)
        {
            return await GetAsync(new GetParams { WaitForSync = waitForSync });
        }

        /// <summary>
        ///     Get the presence state for a given clientId, optionally waiting for Sync to complete.
        ///     Implicitly attaches the Channel. However, if the channel is in or moves to the FAILED.
        ///     state before the operation succeeds, it will result in an error.
        /// </summary>
        /// <param name="clientId">requests Presence for the this clientId.</param>
        /// <param name="waitForSync">whether it should wait for a sync to complete.</param>
        /// <returns>the current present members.</returns>
        public async Task<IEnumerable<PresenceMessage>> GetAsync(string clientId, bool waitForSync)
        {
            return await GetAsync(new GetParams { ClientId = clientId, WaitForSync = waitForSync });
        }

        /// <summary>
        ///     Get the presence state for a given clientId and connectionId, optionally waiting for Sync to complete.
        ///     Implicitly attaches the Channel. However, if the channel is in or moves to the FAILED.
        ///     state before the operation succeeds, it will result in an error.
        /// </summary>
        /// <param name="clientId">requests Presence for the this clientId.</param>
        /// <param name="connectionId">requests Presence for the a specific connectionId.</param>
        /// <param name="waitForSync">whether it should wait for a sync to complete.</param>
        /// <returns>the current present members.</returns>
        public async Task<IEnumerable<PresenceMessage>> GetAsync(string clientId = null, string connectionId = null, bool waitForSync = true)
        {
            return await GetAsync(new GetParams { ClientId = clientId, ConnectionId = connectionId, WaitForSync = waitForSync });
        }

        private async Task<bool> WaitForSyncAsync()
        {
            var tsc = new TaskCompletionSource<bool>();

            // The InternalSync should be completed and the channels Attached or Attaching
            void CheckAndSet()
            {
                if (SyncComplete
                    && (_channel.State == ChannelState.Attached || _channel.State == ChannelState.Attaching))
                {
                    tsc.TrySetResult(true);
                }
            }

            // if the channel state changes and is not Attached or Attaching then we should exit
            void OnChannelStateChanged(object sender, ChannelStateChange args)
            {
                if (_channel.State != ChannelState.Attached && _channel.State != ChannelState.Attaching)
                {
                    tsc.TrySetResult(false);
                }
            }

            void OnSyncEvent(object sender, EventArgs args) => CheckAndSet();

            _channel.StateChanged += OnChannelStateChanged;
            SyncCompletedEventHandler += OnSyncEvent;

            // Do a manual check in case we are already in the desired state
            CheckAndSet();
            bool syncIsComplete = await tsc.Task;

            // unsubscribe from events
            _channel.StateChanged -= OnChannelStateChanged;
            SyncCompletedEventHandler -= OnSyncEvent;

            if (!syncIsComplete)
            {
                /* invalid channel state */
                int errorCode;
                string errorMessage;
                if (_channel.State == ChannelState.Suspended)
                {
                    /* (RTP11d) If the Channel is in the SUSPENDED state then the get function will by default,
                    * or if waitForSync is set to true, result in an error with code 91005 and a message stating
                    * that the presence state is out of sync due to the channel being in a SUSPENDED state */
                    errorCode = 91005;
                    errorMessage = $"Channel {_channel.Name}: presence state is out of sync due to the channel being in a SUSPENDED state";
                }
                else
                {
                    /* RTP11b */
                    errorCode = ErrorCodes.ChannelOperationFailedWithInvalidState;
                    errorMessage = $"Channel {_channel.Name}: cannot get presence state when the channel is in a {_channel.State} state.";
                }

                Logger.Debug($"{errorMessage} (Error Code: {errorCode})");
                throw new AblyException(new ErrorInfo(errorMessage, errorCode));
            }

            return tsc.Task.Result;
        }

        /// <summary>
        /// Subscribe to presence events on the associated Channel. This implicitly
        /// attaches the Channel if it is not already attached.
        /// </summary>
        /// <param name="handler">handler to be notified for the arrival of presence messages.</param>
        public void Subscribe(Action<PresenceMessage> handler)
        {
            if (_channel.State != ChannelState.Attached && _channel.State != ChannelState.Attaching)
            {
                _channel.Attach();
            }

            _handlers.Add(handler.ToHandlerAction());
        }

        /// <summary>
        /// Subscribe to presence events with a specific action on the associated Channel. This implicitly
        /// attaches the Channel if it is not already attached.
        /// </summary>
        /// <param name="action">action to be observed.</param>
        /// <param name="handler">handler to be notified for the arrival of presence messages.</param>
        public void Subscribe(PresenceAction action, Action<PresenceMessage> handler)
        {
            if ((_channel.State != ChannelState.Attached) && (_channel.State != ChannelState.Attaching))
            {
                _channel.Attach();
            }

            _handlers.Add(action.ToString(), new MessageHandlerAction<PresenceMessage>(handler));
        }

        /// <summary>
        /// Subscribe to presence events with a specific action on the associated Channel. This implicitly
        /// attaches the Channel if it is not already attached.
        /// </summary>
        /// <param name="action">action to be observed.</param>
        /// <param name="handler">handler to be notified for the arrival of presence messages.</param>
        public void Subscribe(PresenceAction action, Func<PresenceMessage, Task> handler)
        {
            Subscribe(action, message => { _ = handler(message); });
        }

        /// <summary>
        /// Unsubscribe a previously subscribed handler.
        /// </summary>
        /// <param name="handler">the handler to be unsubscribed.</param>
        /// <returns>true if unsubscribed, false if the handler doesn't exist.</returns>
        public bool Unsubscribe(Action<PresenceMessage> handler)
        {
            return _handlers.Remove(handler.ToHandlerAction());
        }

        /// <summary>
        /// Unsubscribes all attached handlers.
        /// </summary>
        public void Unsubscribe()
        {
            _handlers.RemoveAll();
        }

        /// <summary>
        /// Unsubscribe a specific handler for a specific action.
        /// </summary>
        /// <param name="presenceAction">the specific action.</param>
        /// <param name="handler">the handler to be unsubscribed.</param>
        /// <returns>true if unsubscribed, false if the handler is not found.</returns>
        public bool Unsubscribe(PresenceAction presenceAction, Action<PresenceMessage> handler)
        {
            return _handlers.Remove(presenceAction.ToString(), handler.ToHandlerAction());
        }

        /// <summary>
        /// Enter this client into this channel. This client will be added to the presence set
        /// and presence subscribers will see an enter message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void Enter(object data = null, Action<bool, ErrorInfo> callback = null)
        {
            EnterClient(_clientId, data, callback);
        }

        /// <summary>
        /// Enter this client into this channel. This client will be added to the presence set
        /// and presence subscribers will see an enter message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> EnterAsync(object data = null)
        {
            return EnterClientAsync(_clientId, data);
        }

        /// <summary>
        /// Update the presence data for this client. If the client is not already a member of
        /// the presence set it will be added, and presence subscribers will see an enter or
        /// update message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void Update(object data = null, Action<bool, ErrorInfo> callback = null)
        {
            UpdateClient(_clientId, data, callback);
        }

        /// <summary>
        /// Update the presence data for this client. If the client is not already a member of
        /// the presence set it will be added, and presence subscribers will see an enter or
        /// update message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> UpdateAsync(object data = null)
        {
            return UpdateClientAsync(_clientId, data);
        }

        /// <summary>
        ///  Update the presence data for a specified client into this channel.
        ///  If the client is not already a member of the presence set it will be added,
        ///  and presence subscribers will see a corresponding presence message
        ///  with an empty data payload.As for #enterClient above, the connection
        ///  must be authenticated in a way that enables it to represent an arbitrary clientId.
        /// </summary>
        /// <param name="clientId">the id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void UpdateClient(string clientId, object data, Action<bool, ErrorInfo> callback = null)
        {
            UpdatePresence(new PresenceMessage(PresenceAction.Update, clientId, data), callback);
        }

        /// <summary>
        ///  Update the presence data for a specified client into this channel.
        ///  If the client is not already a member of the presence set it will be added,
        ///  and presence subscribers will see a corresponding presence message
        ///  with an empty data payload.As for #enterClient above, the connection
        ///  must be authenticated in a way that enables it to represent an arbitrary clientId.
        /// </summary>
        /// <param name="clientId">the id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> UpdateClientAsync(string clientId, object data)
        {
            return UpdatePresenceAsync(new PresenceMessage(PresenceAction.Update, clientId, data));
        }

        /// <summary>
        /// Enter a specified client into this channel.The given clientId will be added to
        /// the presence set and presence subscribers will see a corresponding presence message
        /// with an empty data payload.
        /// This method is provided to support connections (eg connections from application
        /// server instances) that act on behalf of multiple clientIds. In order to be able to
        /// enter the channel with this method, the client library must have been instanced
        /// either with a key, or with a token bound to the wildcard clientId.
        /// </summary>
        /// <param name="clientId">id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void EnterClient(string clientId, object data, Action<bool, ErrorInfo> callback = null)
        {
            UpdatePresence(new PresenceMessage(PresenceAction.Enter, clientId, data), callback);
        }

        /// <summary>
        /// Enter a specified client into this channel.The given clientId will be added to
        /// the presence set and presence subscribers will see a corresponding presence message
        /// with an empty data payload.
        /// This method is provided to support connections (eg connections from application
        /// server instances) that act on behalf of multiple clientIds. In order to be able to
        /// enter the channel with this method, the client library must have been instanced
        /// either with a key, or with a token bound to the wildcard clientId.
        /// </summary>
        /// /// <param name="clientId">id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> EnterClientAsync(string clientId, object data)
        {
            return UpdatePresenceAsync(new PresenceMessage(PresenceAction.Enter, clientId, data));
        }

        /// <summary>
        /// Leave this client from this channel. This client will be removed from the presence
        /// set and presence subscribers will see a leave message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void Leave(object data = null, Action<bool, ErrorInfo> callback = null)
        {
            LeaveClient(_clientId, data, callback);
        }

        /// <summary>
        /// Leave this client from this channel. This client will be removed from the presence
        /// set and presence subscribers will see a leave message for this client.
        /// </summary>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> LeaveAsync(object data = null)
        {
            return LeaveClientAsync(_clientId, data);
        }

        /// <summary>
        /// Leave a given client from this channel. This client will be removed from the
        /// presence set and presence subscribers will see a corresponding presence message
        /// with an empty data payload.
        /// This method is provided to support connections (eg connections from application
        /// server instances) that act on behalf of multiple clientIds. In order to be able to
        /// enter the channel with this method, the client library must have been instanced
        /// either with a key, or with a token bound to the wildcard clientId.
        /// </summary>
        /// <param name="clientId">the id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <param name="callback">a listener to be notified on completion of the operation.</param>
        public void LeaveClient(string clientId, object data, Action<bool, ErrorInfo> callback = null)
        {
            UpdatePresence(new PresenceMessage(PresenceAction.Leave, clientId, data), callback);
        }

        /// <summary>
        /// Leave a given client from this channel. This client will be removed from the
        /// presence set and presence subscribers will see a corresponding presence message
        /// with an empty data payload.
        /// This method is provided to support connections (eg connections from application
        /// server instances) that act on behalf of multiple clientIds. In order to be able to
        /// enter the channel with this method, the client library must have been instanced
        /// either with a key, or with a token bound to the wildcard clientId.
        /// </summary>
        /// <param name="clientId">the id of the client.</param>
        /// <param name="data">optional data (eg a status message) for this member.</param>
        /// <returns>Result whether the operation was success or error.</returns>
        public Task<Result> LeaveClientAsync(string clientId, object data)
        {
            return UpdatePresenceAsync(new PresenceMessage(PresenceAction.Leave, clientId, data));
        }

        internal async Task<Result> UpdatePresenceAsync(PresenceMessage msg)
        {
            return await TaskWrapper.Wrap(callback => UpdatePresence(msg, callback));
        }

        internal void UpdatePresence(PresenceMessage msg, Action<bool, ErrorInfo> callback)
        {
            // RTP16a, RTL6c
            switch (_connection.Connection.State)
            {
                case ConnectionState.Initialized:
                case ConnectionState.Connecting:
                case ConnectionState.Disconnected:
                case ConnectionState.Connected:
                    break;
                default:
                    var error = new ErrorInfo(
                        $"Unable to enter presence channel when connection is in a ${_connection.Connection.State} state.",
                        ErrorCodes.UnableToEnterPresenceChannelInvalidState,
                        HttpStatusCode.BadRequest);
                    Logger.Warning(error.ToString());
                    ActionUtils.SafeExecute(() => callback?.Invoke(false, error), Logger, nameof(UpdatePresence));
                    return;
            }

            // RTP16
            switch (_channel.State)
            {
                case ChannelState.Initialized: // RTP16b
                    if (PendingPresenceEnqueue(new QueuedPresenceMessage(msg, callback)))
                    {
                        _channel.Attach();
                    }

                    break;
                case ChannelState.Attaching: // RTP16b
                    PendingPresenceEnqueue(new QueuedPresenceMessage(msg, callback));

                    break;
                case ChannelState.Attached: // RTP16a
                    var message = new ProtocolMessage(ProtocolMessage.MessageAction.Presence, _channel.Name)
                    {
                        Presence = new[] { msg },
                    };
                    _connection.Send(message, callback);
                    break;
                default: // RTP16c
                    var error = new ErrorInfo($"Unable to enter presence channel in {_channel.State} state", ErrorCodes.UnableToEnterPresenceChannelInvalidState);
                    Logger.Warning(error.ToString());
                    ActionUtils.SafeExecute(() => callback?.Invoke(false, error), Logger, nameof(UpdatePresence));
                    return;
            }
        }

        // RTP16b
        private bool PendingPresenceEnqueue(QueuedPresenceMessage msg)
        {
            if (!_connection.Options.QueueMessages)
            {
                msg.Callback?.Invoke(
                    false,
                    new ErrorInfo("Unable enqueue message because Options.QueueMessages is set to False.", _connection.Connection.ConnectionState.DefaultErrorInfo.Code, HttpStatusCode.ServiceUnavailable));

                return false;
            }

            PendingPresenceQueue.Enqueue(msg);
            return true;
        }

        // RTP18
        internal void OnSyncMessage(ProtocolMessage protocolMessage)
        {
            string syncCursor = null;
            var syncChannelSerial = protocolMessage.ChannelSerial;

            // RTP18a
            if (syncChannelSerial.IsNotEmpty())
            {
                var serials = syncChannelSerial.Split(':');
                var syncSequenceId = serials[0];
                syncCursor = serials.Length > 1 ? serials[1] : string.Empty;

                /* If a new sequence identifier is sent from Ably, then the client library
                 * must consider that to be the start of a new sync sequence
                 * and any previous in-flight sync should be discarded. (part of RTP18)*/
                if (SyncInProgress && _currentSyncChannelSerial.IsNotEmpty() && _currentSyncChannelSerial != syncSequenceId)
                {
                    EndSync();
                }

                StartSync();

                if (syncCursor.IsNotEmpty())
                {
                    _currentSyncChannelSerial = syncSequenceId;
                }
            }

            OnPresence(protocolMessage.Presence);

            // RTP18b, RTP18c
            if (syncChannelSerial.IsEmpty() || syncCursor.IsEmpty())
            {
                EndSync();
                _currentSyncChannelSerial = null;
            }
        }

        internal void OnPresence(PresenceMessage[] messages)
        {
            try
            {
                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        bool updateInternalPresence = message.ConnectionId == _channel.RealtimeClient.Connection.Id; // RTP17
                        var broadcast = true;
                        switch (message.Action)
                        {
                            // RTP2d
                            case PresenceAction.Enter:
                            case PresenceAction.Update:
                            case PresenceAction.Present:
                                broadcast &= MembersMap.Put(message);
                                if (updateInternalPresence)
                                {
                                    InternalMembersMap.Put(message); // RTP17b
                                }

                                break;

                            // RTP2e
                            case PresenceAction.Leave:
                                broadcast &= MembersMap.Remove(message);
                                if (updateInternalPresence && !message.IsServerSynthesized())
                                {
                                    InternalMembersMap.Remove(message);
                                }

                                break;
                        }

                        // RTP2g
                        if (broadcast)
                        {
                            Publish(message);
                        }
                    }
                }
                else
                {
                    Logger.Debug("Sync with no presence");
                }
            }
            catch (Exception ex)
            {
                var errInfo = new ErrorInfo(
                    $"An error occurred processing Presence Messages for channel '{_channel.Name}'. See the InnerException for more details.");
                Logger.Error($"{errInfo.Message} Error: {ex.Message}");
                _channel.OnError(errInfo);
            }
        }

        internal void StartSync()
        {
            if (!SyncInProgress)
            {
                MembersMap.StartSync();
            }
        }

        private void EndSync()
        {
            if (!SyncInProgress)
            {
                return;
            }

            // RTP19
            var localNonUpdatedMembersDuringSync = MembersMap.EndSync();
            foreach (var presenceMember in localNonUpdatedMembersDuringSync)
            {
                presenceMember.Action = PresenceAction.Leave;
                presenceMember.Id = null;
                presenceMember.Timestamp = DateTimeOffset.UtcNow;
            }

            Publish(localNonUpdatedMembersDuringSync);

            NotifySyncCompleted();
        }

        private void EnterMembersFromInternalPresenceMap()
        {
            // RTP17g
            foreach (var item in InternalMembersMap.Values)
            {
                try
                {
                    var itemToSend = new PresenceMessage(PresenceAction.Enter, item.ClientId, item.Data, item.Id);
                    UpdatePresence(itemToSend, (success, info) =>
                    {
                        if (!success)
                        {
                            EmitErrorUpdate(item.ClientId, _channel.Name, info.Message);
                        }
                    });
                }
                catch (AblyException e)
                {
                    EmitErrorUpdate(item.ClientId, _channel.Name, e.ErrorInfo.Message);
                }
            }

            // (RTP17e)
            void EmitErrorUpdate(string clientId, string channelName, string errorMessage)
            {
                var errorString =
                    $"Cannot automatically re-enter {clientId} on channel {channelName} ({errorMessage})";
                Logger.Error(errorString);
                _channel.EmitErrorUpdate(new ErrorInfo(errorString, 91004), true);
            }
        }

        private void Publish(params PresenceMessage[] messages)
        {
            foreach (var message in messages)
            {
                NotifySubscribers(message);
            }
        }

        private void NotifySubscribers(PresenceMessage message)
        {
            var handlers = _handlers.GetHandlers();
            if (Logger.IsDebug)
            {
                Logger.Debug("Notifying Presence handlers: " + handlers.Count());
            }

            foreach (var handler in handlers)
            {
                var loopHandler = handler;
                _channel.RealtimeClient.NotifyExternalClients(() => loopHandler.SafeHandle(message, Logger));
            }

            var specificHandlers = _handlers.GetHandlers(message.Action.ToString());
            if (Logger.IsDebug)
            {
                Logger.Debug("Notifying specific handlers for Message: " + message.Action + ". Count: " + specificHandlers.Count());
            }

            foreach (var specificHandler in specificHandlers)
            {
                var loopHandler = specificHandler;
                _channel.RealtimeClient.NotifyExternalClients(() => loopHandler.SafeHandle(message, Logger));
            }
        }

        // RTP5a
        internal void ChannelDetachedOrFailed(ErrorInfo error)
        {
            FailQueuedMessages(error);
            MembersMap.Clear();
            InternalMembersMap.Clear();
        }

        // RTP5f
        internal void ChannelSuspended(ErrorInfo error)
        {
            FailQueuedMessages(error);
        }

        internal void ChannelAttached(ProtocolMessage attachedMessage)
        {
            // RTP1
            var hasPresence = attachedMessage != null && attachedMessage.HasFlag(ProtocolMessage.Flag.HasPresence);

            // RTP19
            StartSync();

            if (!hasPresence)
            {
                EndSync(); // RTP19
            }

            // RTP5b
            SendQueuedMessages();

            // RTP17f
            EnterMembersFromInternalPresenceMap();
        }

        private void SendQueuedMessages()
        {
            if (PendingPresenceQueue.Count == 0)
            {
                return;
            }

            var message = new ProtocolMessage(ProtocolMessage.MessageAction.Presence, _channel.Name)
            {
                Presence = new PresenceMessage[PendingPresenceQueue.Count],
            };
            var callbacks = new List<Action<bool, ErrorInfo>>();
            var i = 0;

            while (!PendingPresenceQueue.IsEmpty)
            {
                if (PendingPresenceQueue.TryDequeue(out var queuedPresenceMessage))
                {
                    message.Presence[i++] = queuedPresenceMessage.Message;
                    if (queuedPresenceMessage.Callback != null)
                    {
                        callbacks.Add(queuedPresenceMessage.Callback);
                    }
                }
            }

            _connection.Send(message, (s, e) =>
            {
                foreach (var callback in callbacks)
                {
                    callback(s, e);
                }
            });
        }

        private void FailQueuedMessages(ErrorInfo reason)
        {
            while (!PendingPresenceQueue.IsEmpty)
            {
                if (PendingPresenceQueue.TryDequeue(out var queuedPresenceMessage))
                {
                    queuedPresenceMessage.Callback?.Invoke(false, reason);
                }
            }
        }

        /// <summary>
        /// Obtain recent history for this channel using the REST API.
        /// The history provided relates to all clients of this application,
        /// not just this instance.
        /// </summary>
        /// <param name="untilAttach">optionally can add until attached parameter.</param>
        /// <exception cref="AblyException">can throw if untilAttached=true and the current channel is not attached.</exception>
        /// <returns>Paginated list of Presence messages.</returns>
        public Task<PaginatedResult<PresenceMessage>> HistoryAsync(bool untilAttach = false)
        {
            var query = new PaginatedRequestParams();
            if (untilAttach)
            {
                _channel.AddUntilAttachParameter(query);
            }

            return _channel.RestChannel.Presence.HistoryAsync(query);
        }

        /// <summary>
        /// Obtain recent history for this channel using the REST API.
        /// The history provided relates to all clients of this application,
        /// not just this instance.
        /// </summary>
        /// <param name="query">the request params. See the Ably REST API documentation for more details.</param>
        /// <param name="untilAttach">add until attached parameter.</param>
        /// <exception cref="AblyException">can throw if untilAttached=true and the current channel is not attached.</exception>
        /// <returns>Paginated list of Presence messages.</returns>
        public Task<PaginatedResult<PresenceMessage>> HistoryAsync(PaginatedRequestParams query, bool untilAttach = false)
        {
            query = query ?? new PaginatedRequestParams();
            if (untilAttach)
            {
                _channel.AddUntilAttachParameter(query);
            }

            return _channel.RestChannel.Presence.HistoryAsync(query);
        }

        private void NotifySyncCompleted()
        {
            SyncCompletedEventHandler?.Invoke(this, EventArgs.Empty);
        }

        internal JToken GetState() => new JObject
        {
            ["handlers"] = _handlers.GetState(),
            ["members"] = MembersMap.GetState(),
            ["pendingQueue"] = new JArray(PendingPresenceQueue.Select(x => JObject.FromObject(x.Message))),
        };

        /// <summary>
        /// Dispose(bool disposing) executes in two distinct scenarios. If disposing equals true, the method has
        /// been called directly or indirectly by a user's code. Managed and unmanaged resources can be disposed.
        /// If disposing equals false, the method has been called by the runtime from inside the finalizer and
        /// you should not reference other objects. Only unmanaged resources can be disposed.
        /// </summary>
        /// <param name="disposing">Refer to the summary.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _handlers.Dispose();
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// Implement IDisposable.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
        }
    }
}
