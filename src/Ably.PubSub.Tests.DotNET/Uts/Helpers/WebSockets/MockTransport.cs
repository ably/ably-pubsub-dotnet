using System;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The <see cref="ITransport"/> a <see cref="MockWebSocket"/> hands the SDK. One per connection
    /// attempt — the SDK builds a fresh transport for every attempt, so a reconnect produces a second
    /// instance and the mock's <c>Connections</c> list grows.
    ///
    /// Everything the server side does — delivering a message, closing the connection — is posted to
    /// an **ordered asynchronous chain** rather than invoked inline, which is what
    /// <c>mock_websocket.md</c> prescribes ("complete the connection future, *then* deliver the
    /// CONNECTED message asynchronously").
    ///
    /// Inline delivery looks equivalent, because <c>ConnectionManager</c> turns both transport events
    /// and inbound data into commands on one workflow queue — but it is not. A connection-attempt
    /// handler runs inside <c>ITransport.Connect()</c>, which the SDK calls from inside its own
    /// command processing, so events raised there are interleaved with the command that is still
    /// executing. Measured: a handler that answered with success and then sent a connection-level
    /// ERROR and closed had the *close* processed first, which took the connection to DISCONNECTED
    /// with a generic 50000 before the ERROR arrived to FAIL it — and the SDK then retried out of
    /// FAILED in a loop. The chain below fixes the order while keeping it deterministic: each action
    /// runs after the previous one, and none runs until the current stack has unwound.
    /// </summary>
    public sealed class MockTransport : ITransport
    {
        private readonly MockWebSocket _owner;
        private readonly object _lock = new object();

        // The ordered async chain. Guarded by _lock; each enqueued action runs after the previous
        // one and none runs until the enqueuing stack has unwound.
        private Task _chain = Task.CompletedTask;

        internal MockTransport(MockWebSocket owner, TransportParams parameters)
        {
            _owner = owner;
            Parameters = parameters;
            BinaryProtocol = parameters.UseBinaryProtocol;
            Uri = parameters.GetUri();
            Connection = new MockConnection(this, BinaryProtocol);
        }

        public TransportParams Parameters { get; }

        public Uri Uri { get; }

        public bool BinaryProtocol { get; }

        public Guid Id { get; } = Guid.NewGuid();

        public TransportState State { get; set; } = TransportState.Initialized;

        public ITransportListener Listener { get; set; }

        public MockConnection Connection { get; }

        public bool ClosedByServer { get; private set; }

        public bool ClosedByClient { get; private set; }

        public void Connect() => _owner.RaiseConnectionAttempt(this);

        public void Close(bool suppressClosedEvent = true)
        {
            if (!MarkClosedByClient(suppressClosedEvent))
            {
                return;
            }

            if (!suppressClosedEvent)
            {
                Listener?.OnTransportEvent(Id, TransportState.Closed);
            }
        }

        public Result Send(RealtimeTransportData data)
        {
            if (State != TransportState.Connected)
            {
                return Result.Fail($"Cannot send message. Mock transport state is {State}.");
            }

            _owner.RecordMessageFromClient(this, data);
            return Result.Ok();
        }

        public void Dispose()
        {
            // This — not Close() — is how the SDK actually tears a socket down:
            // ConnectionManager.DestroyTransport() nulls the listener and calls Dispose(), and the real
            // MsWebSocketTransport closes its ClientWebSocket from there. A mock that only recorded
            // Close() would never see a client-initiated close, so AwaitClientClose() would always
            // time out.
            MarkClosedByClient(suppressClosedEvent: true);
        }

        internal void CompleteConnection()
        {
            State = TransportState.Connected;
            _owner.RecordConnectionSuccess(this);
            Listener?.OnTransportEvent(Id, TransportState.Connected);
        }

        internal void FailConnection(Exception error)
        {
            State = TransportState.Closed;
            _owner.RecordConnectionFailure(this, error);
            Listener?.OnTransportEvent(Id, TransportState.Closed, error);
        }

        internal void DeliverToClient(JObject message)
        {
            if (ClosedByServer || ClosedByClient)
            {
                throw new InvalidOperationException(
                    "MockWebSocket: the connection is closed, so this message would be silently dropped. " +
                    "Respond to the next connection attempt before injecting again.");
            }

            // Recorded eagerly so a test that inspects the timeline immediately still sees it; only
            // the hand-off to the SDK is deferred.
            _owner.RecordMessageToClient(this, message);
            var text = message.ToString(Formatting.None);
            Enqueue(() => Listener?.OnTransportDataReceived(new RealtimeTransportData(text)));
        }

        /// <summary>
        /// The server closing the connection.
        ///
        /// <paramref name="clean"/> distinguishes the two cases <c>mock_websocket.md</c> separates, and
        /// the distinction matters: a real transport reports a normal closure with a **null**
        /// exception, and only a genuine transport failure carries one. Reporting every server close
        /// as an error made the SDK treat the routine close that follows a connection-level ERROR as a
        /// retryable transport failure, which pulled the connection back out of FAILED into
        /// CONNECTING — so the ERROR's own fatal handling looked broken when it was not.
        /// </summary>
        internal void ServerDisconnect(ErrorInfo error, bool clean)
        {
            lock (_lock)
            {
                if (ClosedByServer)
                {
                    return;
                }

                ClosedByServer = true;
            }

            State = TransportState.Closed;
            _owner.RecordServerDisconnect(this, error);

            Exception exception = null;
            if (error != null)
            {
                exception = new AblyException(error);
            }
            else if (!clean)
            {
                exception = new Exception("Mock transport failure.");
            }

            Enqueue(() => Listener?.OnTransportEvent(Id, TransportState.Closed, exception));
        }

        /// <summary>Posts an action onto the ordered chain.</summary>
        private void Enqueue(Action action)
        {
            lock (_lock)
            {
                _chain = _chain.ContinueWith(
                    _ => action(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        internal void RecordPingFrame() => _owner.RecordPingFrame(this);

        private bool MarkClosedByClient(bool suppressClosedEvent)
        {
            lock (_lock)
            {
                if (ClosedByClient)
                {
                    return false;
                }

                ClosedByClient = true;
            }

            State = TransportState.Closed;
            _owner.RecordClientClose(this, suppressClosedEvent);
            return true;
        }
    }
}
