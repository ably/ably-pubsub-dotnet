using System;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The <see cref="ITransport"/> a <see cref="MockWebSocket"/> hands the SDK, one per connection attempt
    /// (the SDK builds a fresh transport for every attempt). Tests act on its server side,
    /// <see cref="MockConnection"/>, never on this.
    ///
    /// <para><b>Ordered asynchronous chain.</b> Every call into the SDK's <see cref="ITransportListener"/>
    /// (the Connected and Closed transport events, every message to the client) is posted to one ordered
    /// chain per transport and never made inline: each runs after the previous one, on the thread pool,
    /// and the listener is read when it runs (the SDK nulls it before disposing a transport, so a delivery
    /// after teardown is dropped, as with a real socket). This gives the ordering
    /// <c>mock_websocket.md</c> prescribes: <c>RespondWithSuccess</c> completes the connection (state
    /// Connected, CONNECTION_SUCCESS recorded) and only then delivers CONNECTED asynchronously, and
    /// <c>Close(false)</c> calls the listener's Closed asynchronously. Events are recorded on the timeline
    /// synchronously, when the test acts.</para>
    ///
    /// <para><see cref="IsIdle"/> is true when everything posted has been handed to the SDK; the unit base
    /// drains it in <c>process_pending_events()</c> and between fake-clock timers.</para>
    /// </summary>
    public sealed class MockTransport : ITransport
    {
        private readonly MockWebSocket _owner;
        private readonly object _lock = new object();
        private Task _chain = Task.CompletedTask;

        internal MockTransport(MockWebSocket owner, TransportParams parameters)
        {
            _owner = owner;
            Parameters = parameters;
            BinaryProtocol = parameters.UseBinaryProtocol;
            Uri = parameters.GetUri();
            Connection = new MockConnection(this);
        }

        public TransportParams Parameters { get; }

        public Uri Uri { get; }

        /// <summary>Always false in this SDK build: msgpack is compiled out.</summary>
        public bool BinaryProtocol { get; }

        public Guid Id { get; } = Guid.NewGuid();

        public TransportState State { get; private set; } = TransportState.Initialized;

        public ITransportListener Listener { get; set; }

        /// <summary>The server side of this transport.</summary>
        public MockConnection Connection { get; }

        public bool ClosedByServer { get; private set; }

        public bool ClosedByClient { get; private set; }

        /// <summary>True when every posted delivery has run.</summary>
        public bool IsIdle
        {
            get
            {
                lock (_lock)
                {
                    return _chain.IsCompleted;
                }
            }
        }

        /// <summary>Called by the SDK: raises a CONNECTION_ATTEMPT on the mock.</summary>
        public void Connect()
        {
            State = TransportState.Connecting;
            _owner.RaiseConnectionAttempt(this);
        }

        /// <summary>
        /// Called by the SDK (it passes the default, <c>true</c>). Records CLIENT_CLOSE; with
        /// <paramref name="suppressClosedEvent"/> false, the listener's Closed event follows asynchronously.
        /// </summary>
        public void Close(bool suppressClosedEvent = true)
        {
            if (!MarkClosedByClient(new ClientCloseEvent(suppressClosedEvent, viaDispose: false)))
            {
                return;
            }

            if (!suppressClosedEvent)
            {
                Post(() => Listener?.OnTransportEvent(Id, TransportState.Closed));
            }
        }

        /// <summary>Called by the SDK for every outgoing message: records MESSAGE_FROM_CLIENT and runs the handlers.</summary>
        public Result Send(RealtimeTransportData data)
        {
            if (State != TransportState.Connected)
            {
                return Result.Fail($"Cannot send message. Mock transport state is {State}.");
            }

            _owner.RecordMessageFromClient(this, data);
            return Result.Ok();
        }

        /// <summary>
        /// The SDK's usual teardown (<c>ConnectionManager.DestroyTransport</c> nulls the listener, then
        /// disposes): recorded as a CLIENT_CLOSE if the transport wasn't closed already.
        /// </summary>
        public void Dispose() => MarkClosedByClient(new ClientCloseEvent(suppressClosedEvent: true, viaDispose: true));

        public override string ToString() => $"MockTransport({Id}, {State})";

        internal void CompleteConnection()
        {
            lock (_lock)
            {
                if (ClosedByClient)
                {
                    // The SDK gave up on this attempt (its connect timer fired) before the test answered.
                    throw new InvalidOperationException(
                        "MockWebSocket: the client already abandoned this connection attempt; answer the next one.");
                }

                State = TransportState.Connected;
            }

            _owner.Record(MockEventType.ConnectionSuccess, Connection);
            Post(() => Listener?.OnTransportEvent(Id, TransportState.Connected));
        }

        internal void FailConnection(Exception error)
        {
            State = TransportState.Closed;
            _owner.Record(MockEventType.ConnectionFailure, error);
            Post(() => Listener?.OnTransportEvent(Id, TransportState.Closed, error));
        }

        /// <summary>A connection timeout: recorded, but the SDK hears nothing; its own connect timer fires.</summary>
        internal void TimeOutConnection(Exception error) => _owner.Record(MockEventType.ConnectionFailure, error);

        internal void DeliverToClient(JObject message)
        {
            EnsureOpen();
            _owner.Record(MockEventType.MessageToClient, message);
            var text = message.ToString(Formatting.None);
            Post(() => Listener?.OnTransportDataReceived(new RealtimeTransportData(text)));
        }

        internal void DeliverRawToClient(string json)
        {
            EnsureOpen();
            object recorded = json;
            try
            {
                recorded = JObject.Parse(json);
            }
            catch (JsonException)
            {
                // Not an object: record the text as is.
            }

            _owner.Record(MockEventType.MessageToClient, recorded);
            Post(() => Listener?.OnTransportDataReceived(new RealtimeTransportData(json)));
        }

        /// <summary>
        /// The server closing the connection. <paramref name="clean"/> separates the two cases of
        /// <c>mock_websocket.md</c>: a normal closure (after DISCONNECTED, a connection-level ERROR, or
        /// <c>close_from_server()</c>) reaches the SDK with a null exception, as from the real transport;
        /// a transport failure (<c>simulate_disconnect</c>) carries one, built from the error if given.
        /// </summary>
        internal void ServerDisconnect(ErrorInfo error, bool clean)
        {
            lock (_lock)
            {
                if (ClosedByServer || ClosedByClient)
                {
                    return;
                }

                ClosedByServer = true;
            }

            State = TransportState.Closed;
            _owner.Record(MockEventType.ServerDisconnect, error);

            Exception exception = null;
            if (error != null)
            {
                exception = new AblyException(error);
            }
            else if (!clean)
            {
                exception = new Exception("Mock transport failure.");
            }

            Post(() => Listener?.OnTransportEvent(Id, TransportState.Closed, exception));
        }

        internal void SendPingFrame()
        {
            EnsureOpen();
            _owner.Record(MockEventType.PingFrame, Connection);
        }

        private void EnsureOpen()
        {
            if (ClosedByServer || ClosedByClient)
            {
                throw new InvalidOperationException(
                    "MockWebSocket: this connection is closed, so the message would be silently dropped. " +
                    "Answer the next connection attempt and use its connection (or ActiveConnection).");
            }

            if (State != TransportState.Connected)
            {
                throw new InvalidOperationException(
                    "MockWebSocket: this connection attempt hasn't succeeded yet. Call RespondWithSuccess() first.");
            }
        }

        private void Post(Action action)
        {
            lock (_lock)
            {
                _chain = _chain.ContinueWith(
                    _ => Run(action),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _owner.RecordHandlerError(ex);
            }
        }

        /// <summary>
        /// Marks the client side closed. Records CLIENT_CLOSE only for a socket that was open or opening:
        /// disposing a transport the server already closed, or whose attempt failed, closes nothing.
        /// </summary>
        private bool MarkClosedByClient(ClientCloseEvent closeEvent)
        {
            bool wasOpen;
            lock (_lock)
            {
                if (ClosedByClient)
                {
                    return false;
                }

                ClosedByClient = true;
                wasOpen = State == TransportState.Connected || State == TransportState.Connecting;
                State = TransportState.Closed;
            }

            if (wasOpen)
            {
                _owner.RecordClientClose(closeEvent);
            }

            return wasOpen;
        }
    }
}
