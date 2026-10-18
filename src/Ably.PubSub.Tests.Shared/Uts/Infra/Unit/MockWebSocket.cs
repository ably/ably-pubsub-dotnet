using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>MockWebSocket</c> (<c>uts/realtime/unit/helpers/mock_websocket.md</c>), installed through
    /// the public <c>ClientOptions.TransportFactory</c> seam: it is the factory, and every connection attempt
    /// gets a fresh <see cref="MockTransport"/>. One mock serves a client's whole life and keeps one
    /// timeline (<see cref="Events"/>) across all its connections, so a reconnect is a second attempt.
    ///
    /// <para><b>Connection attempts</b> are answered by, in this order: a waiter registered by
    /// <see cref="AwaitConnectionAttempt"/> before the attempt (it takes precedence; the handler isn't
    /// called); or <see cref="OnConnectionAttempt"/>. An attempt with no handler, or one the handler leaves
    /// unanswered, is parked and handed to the next <see cref="AwaitConnectionAttempt"/>, so awaiting after
    /// <c>connect()</c> never races; until the test answers it the client stays CONNECTING (its connect
    /// timer still runs). A parked attempt that was answered meanwhile (e.g. by a handler's scheduled
    /// answer) or abandoned by the client (its transport closed or disposed first, e.g. when the connect
    /// timer fired) is skipped, so the await gets the next live attempt.</para>
    ///
    /// <para><b>Messages from the client</b> are recorded, then every handler runs
    /// (<see cref="OnTextDataFrame"/>, <see cref="OnMessageFromClient"/>, then those added by
    /// <see cref="OnMessage(int, JObject)"/> / <see cref="OnClientMessage"/>), then waiters see the message
    /// too: handlers and awaits mix freely for messages and an await never consumes an event a handler
    /// needs. <see cref="AwaitNextMessageFromClient"/> sees only messages sent after the call;
    /// <see cref="AwaitClientMessage"/> also returns one sent before it that no earlier
    /// <see cref="AwaitClientMessage"/> returned.</para>
    ///
    /// <para><b>Threads.</b> Handlers run synchronously on the SDK's workflow thread (inside
    /// <c>ITransport.Connect</c> / <c>Send</c>), so they must not block or await; anything they send to the
    /// client is delivered asynchronously, in order. A handler's exception is recorded in
    /// <see cref="HandlerErrors"/> and rethrown into the SDK. <see cref="OnBinaryDataFrame"/> exists for the
    /// spec's shape but never fires: this SDK build speaks JSON only.</para>
    ///
    /// <para><b>Time.</b> Timestamps come from <see cref="Clock"/> when the unit base installs one.</para>
    /// </summary>
    public sealed class MockWebSocket : ITransportFactory
    {
        private readonly object _lock = new object();
        private readonly List<MockEvent> _events = new List<MockEvent>();
        private readonly List<MockTransport> _transports = new List<MockTransport>();
        private readonly List<Exception> _handlerErrors = new List<Exception>();
        private readonly List<Action<ProtocolMessage, MockConnection>> _clientMessageHandlers =
            new List<Action<ProtocolMessage, MockConnection>>();

        private readonly Queue<TaskCompletionSource<PendingWebSocketConnection>> _connectionWaiters =
            new Queue<TaskCompletionSource<PendingWebSocketConnection>>();

        private readonly Queue<PendingWebSocketConnection> _unansweredConnections = new Queue<PendingWebSocketConnection>();
        private readonly Queue<TaskCompletionSource<ProtocolMessage>> _messageWaiters =
            new Queue<TaskCompletionSource<ProtocolMessage>>();

        private readonly Queue<TaskCompletionSource<ClientCloseEvent>> _closeWaiters =
            new Queue<TaskCompletionSource<ClientCloseEvent>>();

        private readonly List<KeyValuePair<int, TaskCompletionSource<ProtocolMessage>>> _actionWaiters =
            new List<KeyValuePair<int, TaskCompletionSource<ProtocolMessage>>>();

        private readonly Dictionary<int, int> _claimedByAction = new Dictionary<int, int>();

        public MockWebSocket(
            Action<PendingWebSocketConnection> onConnectionAttempt = null,
            Action<ProtocolMessage> onMessageFromClient = null,
            Action<string> onTextDataFrame = null,
            Action<byte[]> onBinaryDataFrame = null)
        {
            OnConnectionAttempt = onConnectionAttempt;
            OnMessageFromClient = onMessageFromClient;
            OnTextDataFrame = onTextDataFrame;
            OnBinaryDataFrame = onBinaryDataFrame;
        }

        /// <summary>The specs' <c>onConnectionAttempt</c>; reassignable mid-test (<c>mock_ws.onConnectionAttempt = ...</c>).</summary>
        public Action<PendingWebSocketConnection> OnConnectionAttempt { get; set; }

        /// <summary>The specs' <c>onMessageFromClient</c>: every decoded message the client sends.</summary>
        public Action<ProtocolMessage> OnMessageFromClient { get; set; }

        /// <summary>The specs' <c>onTextDataFrame</c>: the raw JSON of every frame the client sends, before decoding.</summary>
        public Action<string> OnTextDataFrame { get; set; }

        /// <summary>The specs' <c>onBinaryDataFrame</c>. Never called: msgpack is compiled out of this SDK build.</summary>
        public Action<byte[]> OnBinaryDataFrame { get; set; }

        /// <summary>The fake clock, or null for the real one. Set by the unit base's factory.</summary>
        public FakeClock Clock { get; set; }

        /// <summary>The specs' <c>events</c>: the whole timeline, a snapshot.</summary>
        public IReadOnlyList<MockEvent> Events => Copy(_events);

        /// <summary>Whatever a handler threw, or the SDK threw when a delivery reached it, in order.</summary>
        public IReadOnlyList<Exception> HandlerErrors => Copy(_handlerErrors);

        /// <summary>The corpus' <c>connect_attempts</c>: every connection attempt, in order.</summary>
        public IReadOnlyList<PendingWebSocketConnection> ConnectionAttempts
            => EventsOfType(MockEventType.ConnectionAttempt).Select(e => (PendingWebSocketConnection)e.Data).ToList();

        /// <summary>The corpus' <c>last_connect_url</c>: the URL of the latest attempt, or null.</summary>
        public RecordedUrl LastConnectUrl => ConnectionAttempts.LastOrDefault()?.Url;

        /// <summary>Every connection that was established, in order.</summary>
        public IReadOnlyList<MockConnection> Connections
            => EventsOfType(MockEventType.ConnectionSuccess).Select(e => (MockConnection)e.Data).ToList();

        /// <summary>The specs' <c>active_connection</c>: the latest connection still open, or null.</summary>
        public MockConnection ActiveConnection
        {
            get
            {
                lock (_lock)
                {
                    return _transports.LastOrDefault(t => t.State == TransportState.Connected)?.Connection;
                }
            }
        }

        /// <summary>Every message the client sent, decoded, in order.</summary>
        public IReadOnlyList<ProtocolMessage> MessagesFromClient
            => EventsOfType(MockEventType.MessageFromClient).Select(e => (ProtocolMessage)e.Data).ToList();

        /// <summary>True when every transport has handed everything posted to the SDK.</summary>
        public bool IsIdle
        {
            get
            {
                lock (_lock)
                {
                    return _transports.All(t => t.IsIdle);
                }
            }
        }

        /// <summary>The specs' <c>events.filter(e =&gt; e.type == type)</c>.</summary>
        public IReadOnlyList<MockEvent> EventsOfType(MockEventType type) => Copy(_events).Where(e => e.Type == type).ToList();

        /// <summary>Every client message with this action (a <see cref="ProtocolMessages"/> constant).</summary>
        public IReadOnlyList<ProtocolMessage> MessagesFromClientOfAction(int action)
            => MessagesFromClient.Where(m => m != null && (int)m.Action == action).ToList();

        ITransport ITransportFactory.CreateTransport(TransportParams parameters) => new MockTransport(this, parameters);

        /// <summary><c>mock_ws.send_to_client(msg)</c> on <see cref="ActiveConnection"/>.</summary>
        public void SendToClient(JObject message, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).SendToClient(message);

        /// <summary><c>mock_ws.send_to_client_and_close(msg)</c> on <see cref="ActiveConnection"/>.</summary>
        public void SendToClientAndClose(JObject message, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).SendToClientAndClose(message);

        /// <summary><c>mock_ws.send_to_client_raw(json)</c> on <see cref="ActiveConnection"/>.</summary>
        public void SendToClientRaw(string json, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).SendToClientRaw(json);

        /// <summary><c>mock_ws.simulate_disconnect(error?)</c> on <see cref="ActiveConnection"/>.</summary>
        public void SimulateDisconnect(ErrorInfo error = null, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).SimulateDisconnect(error);

        /// <summary><c>close_from_server()</c> on <see cref="ActiveConnection"/>.</summary>
        public void CloseFromServer([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).CloseFromServer();

        /// <summary><c>send_ping_frame()</c> on <see cref="ActiveConnection"/>; records PING_FRAME only (see <see cref="MockConnection.SendPingFrame"/>).</summary>
        public void SendPingFrame([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            => RequireActive(callerFilePath, callerLineNumber).SendPingFrame();

        /// <summary>The alternative API's <c>on_connect(handler)</c>: sets <see cref="OnConnectionAttempt"/>.</summary>
        public void OnConnect(Action<PendingWebSocketConnection> handler) => OnConnectionAttempt = handler;

        /// <summary>The alternative API's <c>on_connect(respond_with: msg)</c>: every attempt succeeds and gets a copy of <paramref name="respondWith"/>.</summary>
        public void OnConnect(JObject respondWith)
            => OnConnectionAttempt = connection => connection.RespondWithSuccess((JObject)respondWith.DeepClone());

        /// <summary>
        /// The alternative API's <c>on_message(action:, respond_with: msg)</c>: adds a handler that answers
        /// every client message with that action by sending a copy of <paramref name="respondWith"/> on the
        /// connection it came from.
        /// </summary>
        public void OnMessage(int action, JObject respondWith)
            => AddClientMessageHandler((message, connection) =>
            {
                if ((int)message.Action == action)
                {
                    connection.SendToClient((JObject)respondWith.DeepClone());
                }
            });

        /// <summary>The alternative API's <c>on_message(action:, handler)</c>: adds a handler for client messages with that action.</summary>
        public void OnMessage(int action, Action<ProtocolMessage> handler)
            => AddClientMessageHandler((message, connection) =>
            {
                if ((int)message.Action == action)
                {
                    handler(message);
                }
            });

        /// <summary>The alternative API's <c>on_client_message(handler)</c>: adds a handler for every client message (added handlers run after <see cref="OnMessageFromClient"/>).</summary>
        public void OnClientMessage(Action<ProtocolMessage> handler) => AddClientMessageHandler((message, connection) => handler(message));

        /// <summary>
        /// The specs' <c>await_connection_attempt(timeout?)</c>. Returns the oldest parked attempt that is still
        /// live (unanswered and not abandoned by the client; stale ones are dropped) if there is one, else
        /// registers for the next attempt at the call: set up the await for the next attempt before
        /// answering the current one.
        /// </summary>
        public Task<PendingWebSocketConnection> AwaitConnectionAttempt(
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            TaskCompletionSource<PendingWebSocketConnection> waiter;
            lock (_lock)
            {
                while (_unansweredConnections.Count > 0)
                {
                    var parked = _unansweredConnections.Dequeue();
                    if (!parked.Answered && !parked.Abandoned)
                    {
                        return Task.FromResult(parked);
                    }
                }

                waiter = NewWaiter<PendingWebSocketConnection>();
                _connectionWaiters.Enqueue(waiter);
            }

            return WithTimeout(waiter.Task, timeout, "a connection attempt", callerFilePath, callerLineNumber);
        }

        /// <summary>The specs' <c>await_next_message_from_client(timeout?)</c>: the next message sent after this call.</summary>
        public Task<ProtocolMessage> AwaitNextMessageFromClient(
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var waiter = NewWaiter<ProtocolMessage>();
            lock (_lock)
            {
                _messageWaiters.Enqueue(waiter);
            }

            return WithTimeout(waiter.Task, timeout, "a message from the client", callerFilePath, callerLineNumber);
        }

        /// <summary>
        /// The alternative API's <c>await_client_message(action:)</c>: the oldest client message with this
        /// action that no earlier call returned, waiting for one if there is none yet. Includes messages sent
        /// before the call, so a spec that triggers the send and then awaits never races it.
        /// </summary>
        public Task<ProtocolMessage> AwaitClientMessage(
            int action,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            TaskCompletionSource<ProtocolMessage> waiter;
            lock (_lock)
            {
                var sent = _events
                    .Where(e => e.Type == MockEventType.MessageFromClient && e.Message != null && (int)e.Message.Action == action)
                    .Select(e => e.Message)
                    .ToList();
                _claimedByAction.TryGetValue(action, out var claimed);
                if (sent.Count > claimed)
                {
                    _claimedByAction[action] = claimed + 1;
                    return Task.FromResult(sent[claimed]);
                }

                waiter = NewWaiter<ProtocolMessage>();
                _actionWaiters.Add(new KeyValuePair<int, TaskCompletionSource<ProtocolMessage>>(action, waiter));
            }

            return WithTimeout(waiter.Task, timeout, $"a client message with action {action}", callerFilePath, callerLineNumber);
        }

        /// <summary>The specs' <c>await_client_close(timeout?)</c>: the next CLIENT_CLOSE after this call.</summary>
        public Task<ClientCloseEvent> AwaitClientClose(
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var waiter = NewWaiter<ClientCloseEvent>();
            lock (_lock)
            {
                _closeWaiters.Enqueue(waiter);
            }

            return WithTimeout(waiter.Task, timeout, "the client to close the WebSocket", callerFilePath, callerLineNumber);
        }

        /// <summary>
        /// Polls until the client has sent at least <paramref name="count"/> messages with this action (in
        /// total, since the last reset) and returns all of them.
        /// </summary>
        public Task<List<ProtocolMessage>> AwaitProtocolMessages(
            int action,
            int count = 1,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => UtsWait.PollUntil<List<ProtocolMessage>>(
                () =>
                {
                    var matching = MessagesFromClientOfAction(action).ToList();
                    return matching.Count >= count ? matching : null;
                },
                timeout,
                null,
                $"{count} client message(s) with action {action}",
                callerFilePath,
                callerLineNumber);

        /// <summary>
        /// The specs' <c>reset()</c>: clears the timeline, waiters, parked attempts and added handlers
        /// (<see cref="OnMessage(int, JObject)"/>, <see cref="OnClientMessage"/>). The handler properties stay.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _events.Clear();
                _transports.Clear();
                _handlerErrors.Clear();
                _clientMessageHandlers.Clear();
                _connectionWaiters.Clear();
                _unansweredConnections.Clear();
                _messageWaiters.Clear();
                _closeWaiters.Clear();
                _actionWaiters.Clear();
                _claimedByAction.Clear();
            }
        }

        internal void RaiseConnectionAttempt(MockTransport transport)
        {
            var pending = new PendingWebSocketConnection(transport, Now());
            TaskCompletionSource<PendingWebSocketConnection> waiter = null;
            lock (_lock)
            {
                _transports.Add(transport);
                _events.Add(new MockEvent(MockEventType.ConnectionAttempt, Now(), pending));
                if (_connectionWaiters.Count > 0)
                {
                    waiter = _connectionWaiters.Dequeue();
                }
            }

            if (waiter != null)
            {
                waiter.TrySetResult(pending);
                return;
            }

            var handler = OnConnectionAttempt;
            if (handler != null)
            {
                Invoke(() => handler(pending));
                if (pending.Answered)
                {
                    return;
                }
            }

            // No handler, or the handler left it unanswered: hand it to a waiter that registered meanwhile
            // (it found the parked queue empty), or park it for the next AwaitConnectionAttempt(). This is
            // the corpus' usual await style: build the client (it connects), then await the attempt.
            TaskCompletionSource<PendingWebSocketConnection> lateWaiter = null;
            lock (_lock)
            {
                if (_connectionWaiters.Count > 0)
                {
                    lateWaiter = _connectionWaiters.Dequeue();
                }
                else
                {
                    _unansweredConnections.Enqueue(pending);
                }
            }

            lateWaiter?.TrySetResult(pending);
        }

        internal void RecordMessageFromClient(MockTransport transport, RealtimeTransportData data)
        {
            var message = data.IsBinary ? null : JsonHelper.Deserialize<ProtocolMessage>(data.Text);
            TaskCompletionSource<ProtocolMessage> nextWaiter = null;
            TaskCompletionSource<ProtocolMessage> actionWaiter = null;
            List<Action<ProtocolMessage, MockConnection>> added;
            lock (_lock)
            {
                _events.Add(new MockEvent(MockEventType.MessageFromClient, Now(), message));
                if (_messageWaiters.Count > 0)
                {
                    nextWaiter = _messageWaiters.Dequeue();
                }

                if (message != null)
                {
                    var action = (int)message.Action;
                    var index = _actionWaiters.FindIndex(w => w.Key == action);
                    if (index >= 0)
                    {
                        actionWaiter = _actionWaiters[index].Value;
                        _actionWaiters.RemoveAt(index);
                        _claimedByAction.TryGetValue(action, out var claimed);
                        _claimedByAction[action] = claimed + 1;
                    }
                }

                added = new List<Action<ProtocolMessage, MockConnection>>(_clientMessageHandlers);
            }

            try
            {
                if (data.IsBinary)
                {
                    var binaryHandler = OnBinaryDataFrame;
                    if (binaryHandler != null)
                    {
                        Invoke(() => binaryHandler(data.Data));
                    }
                }
                else
                {
                    var textHandler = OnTextDataFrame;
                    if (textHandler != null)
                    {
                        Invoke(() => textHandler(data.Text));
                    }
                }

                var messageHandler = OnMessageFromClient;
                if (messageHandler != null && message != null)
                {
                    Invoke(() => messageHandler(message));
                }

                foreach (var handler in added)
                {
                    if (message != null)
                    {
                        Invoke(() => handler(message, transport.Connection));
                    }
                }
            }
            finally
            {
                nextWaiter?.TrySetResult(message);
                actionWaiter?.TrySetResult(message);
            }
        }

        internal void RecordClientClose(ClientCloseEvent closeEvent)
        {
            TaskCompletionSource<ClientCloseEvent> waiter = null;
            lock (_lock)
            {
                _events.Add(new MockEvent(MockEventType.ClientClose, Now(), closeEvent));
                if (_closeWaiters.Count > 0)
                {
                    waiter = _closeWaiters.Dequeue();
                }
            }

            waiter?.TrySetResult(closeEvent);
        }

        internal void Record(MockEventType type, object data)
        {
            lock (_lock)
            {
                _events.Add(new MockEvent(type, Now(), data));
            }
        }

        internal void RecordHandlerError(Exception error)
        {
            lock (_lock)
            {
                _handlerErrors.Add(error);
            }
        }

        private static TaskCompletionSource<T> NewWaiter<T>()
            => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        private DateTimeOffset Now() => Clock?.Now ?? DateTimeOffset.UtcNow;

        private void AddClientMessageHandler(Action<ProtocolMessage, MockConnection> handler)
        {
            lock (_lock)
            {
                _clientMessageHandlers.Add(handler);
            }
        }

        private MockConnection RequireActive(string callerFilePath, int callerLineNumber)
        {
            var connection = ActiveConnection;
            if (connection == null)
            {
                throw UtsAssert.Failure(
                    $"MockWebSocket: there is no active connection ({ConnectionAttempts.Count} attempt(s) so far; timeline: {Describe()}). " +
                    "Answer a connection attempt with RespondWithSuccess first.",
                    callerFilePath,
                    callerLineNumber,
                    null);
            }

            return connection;
        }

        private async Task<T> WithTimeout<T>(Task<T> task, TimeSpan? timeout, string what, string callerFilePath, int callerLineNumber)
        {
            var bound = timeout ?? UtsWait.DefaultTimeout;
            var completed = await Task.WhenAny(task, Task.Delay(bound)).ConfigureAwait(false);
            if (completed != task)
            {
                throw UtsAssert.Failure(
                    $"MockWebSocket: timed out after {bound.TotalMilliseconds} ms waiting for {what}; timeline: {Describe()}",
                    callerFilePath,
                    callerLineNumber,
                    null);
            }

            return await task.ConfigureAwait(false);
        }

        private string Describe()
        {
            var events = Events;
            return events.Count == 0 ? "(empty)" : string.Join(", ", events.Select(e => e.Type.ToString()));
        }

        private void Invoke(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                RecordHandlerError(ex);
                throw;
            }
        }

        private List<T> Copy<T>(List<T> source)
        {
            lock (_lock)
            {
                return new List<T>(source);
            }
        }
    }
}
