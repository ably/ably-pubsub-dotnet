using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>MockWebSocket</c> (<c>uts/realtime/unit/helpers/mock_websocket.md</c>).
    ///
    /// Installed through the public <c>ClientOptions.TransportFactory</c> seam. One mock serves a whole
    /// client: each connection attempt produces a fresh <see cref="MockTransport"/>, and the mock keeps
    /// the unified timeline across all of them, so a reconnection is observable as a second attempt
    /// rather than as a new mock.
    /// </summary>
    public sealed class MockWebSocket
    {
        /// <summary>
        /// The deadline every wait here uses unless a test asks for another.
        ///
        /// Fifteen seconds, not five. Nothing in the unit tier legitimately takes anywhere near
        /// that - these are mocks - so the figure is not an expectation, it is headroom. The tier
        /// is ~900 tests running in parallel in Release, and a five-second deadline was measured
        /// losing to scheduling under that load about once every three full runs, on a test with
        /// nothing wrong with it. A genuinely stuck test still fails, ten seconds later.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        private readonly object _lock = new object();
        private readonly List<MockEvent> _events = new List<MockEvent>();
        private readonly List<MockTransport> _transports = new List<MockTransport>();
        private readonly List<Exception> _handlerErrors = new List<Exception>();

        private readonly Queue<TaskCompletionSource<PendingWebSocketConnection>> _connectionWaiters =
            new Queue<TaskCompletionSource<PendingWebSocketConnection>>();

        private readonly Queue<TaskCompletionSource<ProtocolMessage>> _messageWaiters =
            new Queue<TaskCompletionSource<ProtocolMessage>>();

        private readonly Queue<TaskCompletionSource<ClientCloseEvent>> _closeWaiters =
            new Queue<TaskCompletionSource<ClientCloseEvent>>();

        // Attempts a handler deliberately left unanswered. The SDK opens its transport eagerly, so a test
        // that calls AwaitConnectionAttempt() after Connect() would otherwise register its waiter behind
        // an attempt already sitting here.
        private readonly Queue<PendingWebSocketConnection> _unansweredConnections =
            new Queue<PendingWebSocketConnection>();

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
            TransportFactory = new Factory(this);
        }

        /// <summary>
        /// Reassignable. Leave it null to have every attempt answered with a bare success and no CONNECTED
        /// message, which parks the client in CONNECTING. To withhold a response entirely — so the attempt
        /// stays unanswered — pass a handler that does nothing.
        /// </summary>
        public Action<PendingWebSocketConnection> OnConnectionAttempt { get; set; }

        public Action<ProtocolMessage> OnMessageFromClient { get; set; }

        public Action<string> OnTextDataFrame { get; set; }

        public Action<byte[]> OnBinaryDataFrame { get; set; }

        public Func<DateTimeOffset> NowFunc { get; set; } = () => DateTimeOffset.UtcNow;

        /// <summary>What goes into <c>ClientOptions.TransportFactory</c>.</summary>
        public ITransportFactory TransportFactory { get; }

        public IReadOnlyList<MockEvent> Events => Copy(_events);

        public IReadOnlyList<Exception> HandlerErrors => Copy(_handlerErrors);

        public IReadOnlyList<PendingWebSocketConnection> ConnectionAttempts =>
            EventsOfType(MockEventType.ConnectionAttempt)
                .Select(e => (PendingWebSocketConnection)e.Data)
                .ToList();

        public IReadOnlyList<ProtocolMessage> MessagesFromClient =>
            EventsOfType(MockEventType.MessageFromClient)
                .Select(e => (ProtocolMessage)e.Data)
                .ToList();

        /// <summary>Every connection established, in order.</summary>
        public IReadOnlyList<MockConnection> Connections
        {
            get
            {
                lock (_lock)
                {
                    return _transports
                        .Where(t => t.State == TransportState.Connected || t.ClosedByServer || t.ClosedByClient)
                        .Select(t => t.Connection)
                        .ToList();
                }
            }
        }

        /// <summary>
        /// The live connection, or null before the first one and after it closes. A spec's
        /// <c>mock_ws.active_connection.close()</c> means the *server* closing — translate it to
        /// <see cref="SimulateDisconnect"/>, not to anything on the transport.
        /// </summary>
        public MockConnection ActiveConnection
        {
            get
            {
                lock (_lock)
                {
                    var transport = _transports.LastOrDefault(
                        t => t.State == TransportState.Connected && !t.ClosedByServer && !t.ClosedByClient);
                    return transport?.Connection;
                }
            }
        }

        public IReadOnlyList<MockEvent> EventsOfType(MockEventType type)
            => Copy(_events).Where(e => e.Type == type).ToList();

        public void SendToClient(JObject message) => RequireActive().SendToClient(message);

        public void SendToClientAndClose(JObject message) => RequireActive().SendToClientAndClose(message);

        public void SimulateDisconnect(ErrorInfo error = null) => RequireActive().SimulateDisconnect(error);

        public void SendPingFrame() => RequireActive().SendPingFrame();

        public Task<PendingWebSocketConnection> AwaitConnectionAttempt(TimeSpan? timeout = null)
        {
            lock (_lock)
            {
                if (_unansweredConnections.Count > 0)
                {
                    return Task.FromResult(_unansweredConnections.Dequeue());
                }
            }

            return AwaitNext(_connectionWaiters, timeout, "a connection attempt");
        }

        public Task<ProtocolMessage> AwaitNextMessageFromClient(TimeSpan? timeout = null)
            => AwaitNext(_messageWaiters, timeout, "a message from the client");

        public Task<ClientCloseEvent> AwaitClientClose(TimeSpan? timeout = null)
            => AwaitNext(_closeWaiters, timeout, "the client to close the connection");

        /// <summary>
        /// Waits until <paramref name="count"/> messages of an action have left the client, and returns
        /// them. The primitive behind <see cref="AwaitPublished"/> and <see cref="AwaitPresenceSent"/>.
        /// </summary>
        public async Task<List<ProtocolMessage>> AwaitProtocolMessages(
            ProtocolMessage.MessageAction action,
            int count = 1,
            TimeSpan? timeout = null)
        {
            List<ProtocolMessage> Matching() => MessagesFromClient.Where(m => m.Action == action).ToList();

            await UtsClients.PollUntil(
                () => Matching().Count >= count,
                $"{count} {action} message(s) from the client",
                timeout ?? DefaultTimeout).ConfigureAwait(false);

            return Matching();
        }

        public Task<List<ProtocolMessage>> AwaitPublished(int count = 1, TimeSpan? timeout = null)
            => AwaitProtocolMessages(ProtocolMessage.MessageAction.Message, count, timeout);

        public Task<List<ProtocolMessage>> AwaitPresenceSent(int count = 1, TimeSpan? timeout = null)
            => AwaitProtocolMessages(ProtocolMessage.MessageAction.Presence, count, timeout);

        /// <summary>Clears the timeline, the waiters and the transports. Handlers are left alone.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _events.Clear();
                _transports.Clear();
                _handlerErrors.Clear();
                _connectionWaiters.Clear();
                _messageWaiters.Clear();
                _closeWaiters.Clear();
                _unansweredConnections.Clear();
            }
        }

        internal void RaiseConnectionAttempt(MockTransport transport)
        {
            var pending = new PendingWebSocketConnection(
                transport,
                transport.Uri,
                transport.BinaryProtocol,
                NowFunc());

            TaskCompletionSource<PendingWebSocketConnection> waiter = null;
            lock (_lock)
            {
                _transports.Add(transport);
                _events.Add(new MockEvent(MockEventType.ConnectionAttempt, NowFunc(), pending));
                if (_connectionWaiters.Count > 0)
                {
                    waiter = _connectionWaiters.Dequeue();
                }
            }

            if (waiter != null)
            {
                // A registered waiter takes precedence over the handler: the test said it wanted to answer
                // this one itself.
                waiter.TrySetResult(pending);
                return;
            }

            var handler = OnConnectionAttempt;
            if (handler == null)
            {
                pending.RespondWithSuccess();
                return;
            }

            Invoke(() => handler(pending));

            if (!pending.Answered)
            {
                TaskCompletionSource<PendingWebSocketConnection> lateWaiter = null;
                lock (_lock)
                {
                    // A waiter can have registered while the handler above was still running: it would
                // have found this queue empty and parked itself, so parking the attempt here too
                // would leave the two never meeting. Hand it over if anyone is waiting.
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
        }

        internal void RecordConnectionSuccess(MockTransport transport)
            => Record(MockEventType.ConnectionSuccess, transport.Connection);

        internal void RecordConnectionFailure(MockTransport transport, Exception error)
            => Record(MockEventType.ConnectionFailure, error);

        internal void RecordServerDisconnect(MockTransport transport, ErrorInfo error)
            => Record(MockEventType.ServerDisconnect, error);

        internal void RecordPingFrame(MockTransport transport)
            => Record(MockEventType.PingFrame, transport.Connection);

        internal void RecordMessageToClient(MockTransport transport, JObject message)
            => Record(MockEventType.MessageToClient, message);

        internal void RecordClientClose(MockTransport transport, bool suppressClosedEvent)
        {
            var closeEvent = new ClientCloseEvent(suppressClosedEvent);
            TaskCompletionSource<ClientCloseEvent> waiter = null;
            lock (_lock)
            {
                _events.Add(new MockEvent(MockEventType.ClientClose, NowFunc(), closeEvent));
                if (_closeWaiters.Count > 0)
                {
                    waiter = _closeWaiters.Dequeue();
                }
            }

            waiter?.TrySetResult(closeEvent);
        }

        internal void RecordMessageFromClient(MockTransport transport, RealtimeTransportData data)
        {
            var message = data.IsBinary
                ? null
                : JsonHelper.Deserialize<ProtocolMessage>(data.Text);

            TaskCompletionSource<ProtocolMessage> waiter = null;
            lock (_lock)
            {
                _events.Add(new MockEvent(MockEventType.MessageFromClient, NowFunc(), message));
                if (_messageWaiters.Count > 0)
                {
                    waiter = _messageWaiters.Dequeue();
                }
            }

            waiter?.TrySetResult(message);

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
            if (messageHandler != null)
            {
                Invoke(() => messageHandler(message));
            }
        }

        private MockConnection RequireActive()
        {
            var connection = ActiveConnection;
            if (connection == null)
            {
                throw new InvalidOperationException(
                    "MockWebSocket: there is no active connection. Respond to a connection attempt first " +
                    $"({ConnectionAttempts.Count} attempt(s) so far).");
            }

            return connection;
        }

        private void Record(MockEventType type, object data)
        {
            lock (_lock)
            {
                _events.Add(new MockEvent(type, NowFunc(), data));
            }
        }

        private List<T> Copy<T>(List<T> source)
        {
            lock (_lock)
            {
                return new List<T>(source);
            }
        }

        private Task<T> AwaitNext<T>(Queue<TaskCompletionSource<T>> waiters, TimeSpan? timeout, string what)
            where T : class
        {
            var waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                // Registered synchronously, so a test can queue the wait for the *next* event before
                // answering the current one — the sequencing mock_websocket.md calls out explicitly.
                waiters.Enqueue(waiter);
            }

            return WithDeadline(waiter.Task, timeout ?? DefaultTimeout, what);
        }

        private async Task<T> WithDeadline<T>(Task<T> task, TimeSpan timeout, string what)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {what}. Timeline so far: " +
                    string.Join(", ", Events.Select(e => e.Type.ToString())));
            }

            return await task.ConfigureAwait(false);
        }

        private void Invoke(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _handlerErrors.Add(ex);
                }

                throw;
            }
        }

        private sealed class Factory : ITransportFactory
        {
            private readonly MockWebSocket _owner;

            internal Factory(MockWebSocket owner)
            {
                _owner = owner;
            }

            public ITransport CreateTransport(TransportParams parameters)
                => new MockTransport(_owner, parameters);
        }
    }
}
