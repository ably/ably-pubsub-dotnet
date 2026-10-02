using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The specs' <c>PendingConnection</c> for the realtime tier (<c>mock_websocket.md</c>): a connection
    /// attempt the test has not yet answered.
    /// </summary>
    public sealed class PendingWebSocketConnection
    {
        private readonly MockTransport _transport;
        private readonly TaskCompletionSource<bool> _answered =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal PendingWebSocketConnection(MockTransport transport, Uri url, bool binaryProtocol, DateTimeOffset timestamp)
        {
            _transport = transport;
            Url = new RecordedUrl(url);
            BinaryProtocol = binaryProtocol;
            Timestamp = timestamp;
        }

        public RecordedUrl Url { get; }

        /// <summary>The specs' <c>protocol</c>, derived from the connection's <c>format</c>.</summary>
        public string Protocol => BinaryProtocol ? "application/x-msgpack" : "application/json";

        public bool BinaryProtocol { get; }

        public DateTimeOffset Timestamp { get; }

        /// <summary>The specs' <c>conn.queryParams</c> shorthand.</summary>
        public IReadOnlyDictionary<string, string> QueryParams => Url.QueryParams;

        /// <summary>The connection this attempt yields, once it has succeeded.</summary>
        public MockConnection Connection => _transport.Connection;

        public bool Answered => _answered.Task.IsCompleted;

        internal Task Answer => _answered.Task;

        /// <summary>
        /// Completes the connection and then, asynchronously, delivers the CONNECTED message. The order
        /// matters and the spec spells it out: the library must have stored the connection before it
        /// processes a message that may start timers referring to it.
        /// </summary>
        public void RespondWithSuccess(JObject connectedMessage = null)
        {
            if (!_answered.TrySetResult(true))
            {
                return;
            }

            _transport.CompleteConnection();
            if (connectedMessage != null)
            {
                _transport.DeliverToClient(connectedMessage);
            }
        }

        public void RespondWithRefused()
            => Fail(new System.Net.Sockets.SocketException(10061)); // WSAECONNREFUSED

        public void RespondWithTimeout()
            => Fail(new TimeoutException("The mock WebSocket connection attempt timed out."));

        public void RespondWithDnsError()
            => Fail(new System.Net.Sockets.SocketException(11001)); // WSAHOST_NOT_FOUND

        /// <summary>
        /// The WebSocket connects and the server then sends an ERROR — the token-error shape. Defaults to
        /// closing afterwards, because a connection-level ERROR is always accompanied by the close.
        /// </summary>
        public void RespondWithError(JObject errorMessage, bool thenClose = true)
        {
            RespondWithSuccess();
            _transport.DeliverToClient(errorMessage);
            if (thenClose)
            {
                _transport.ServerDisconnect(null, clean: true);
            }
        }

        /// <summary>
        /// The server side of the connection this attempt produces, so a handler can respond and then
        /// immediately inject — <c>conn.RespondWithSuccess(); conn.SendToClient(...)</c>.
        /// </summary>
        public void SendToClient(JObject message) => _transport.DeliverToClient(message);

        public void SendToClientAndClose(JObject message)
        {
            _transport.DeliverToClient(message);
            _transport.ServerDisconnect(null, clean: true);
        }

        public void SimulateDisconnect(ErrorInfo error = null)
            => _transport.ServerDisconnect(error, clean: false);

        public override string ToString() => Url.ToString();

        private void Fail(Exception error)
        {
            if (!_answered.TrySetResult(true))
            {
                return;
            }

            _transport.FailConnection(error);
        }
    }
}
