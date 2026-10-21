using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>PendingConnection</c> for the realtime tier (<c>mock_websocket.md</c>): one WebSocket
    /// connection attempt.
    /// </summary>
    public sealed class PendingWebSocketConnection
    {
        private readonly MockTransport _transport;
        private int _answered;

        internal PendingWebSocketConnection(MockTransport transport, DateTimeOffset timestamp)
        {
            _transport = transport;
            Url = new RecordedUrl(transport.Uri);
            Timestamp = timestamp;
        }

        /// <summary>The URL the client tried to connect to, query parameters parsed.</summary>
        public RecordedUrl Url { get; }

        /// <summary>The specs' <c>conn.queryParams</c> / <c>conn.url.query_params</c> shorthand.</summary>
        public IReadOnlyDictionary<string, string> QueryParams => Url.QueryParams;

        /// <summary>The specs' <c>protocol</c>: always <c>application/json</c> in this SDK build.</summary>
        public string Protocol => _transport.BinaryProtocol ? "application/x-msgpack" : "application/json";

        /// <summary>When the attempt was made: fake time when a fake clock is installed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>The server side of the connection this attempt yields (usable once it has succeeded).</summary>
        public MockConnection Connection => _transport.Connection;

        public bool Answered => Volatile.Read(ref _answered) != 0;

        /// <summary>True once the client closed or disposed this attempt's transport (e.g. its connect timer fired).</summary>
        public bool Abandoned => _transport.ClosedByClient;

        /// <summary>
        /// The specs' <c>respond_with_success(connected_message?)</c>: completes the connection first (state
        /// Connected, CONNECTION_SUCCESS recorded, the Connected transport event posted), then delivers <paramref
        /// name="connectedMessage"/>, if given, asynchronously after it.
        /// </summary>
        public void RespondWithSuccess(JObject connectedMessage = null)
        {
            if (!TryAnswer())
            {
                return;
            }

            _transport.CompleteConnection();
            if (connectedMessage != null)
            {
                _transport.DeliverToClient(connectedMessage);
            }
        }

        /// <summary>
        /// The corpus' <c>respond_with_connected()</c>: success with <see
        /// cref="ProtocolMessages.ConnectedMessage()"/>.
        /// </summary>
        public void RespondWithConnected() => RespondWithSuccess(ProtocolMessages.ConnectedMessage());

        /// <summary>Connection refused: the transport closes with a <see cref="SocketException"/> (WSAECONNREFUSED).</summary>
        public void RespondWithRefused() => Fail(new SocketException(10061));

        /// <summary>DNS failure: the transport closes with a <see cref="SocketException"/> (WSAHOST_NOT_FOUND).</summary>
        public void RespondWithDnsError() => Fail(new SocketException(11001));

        /// <summary>The specs' <c>respond_with_timeout()</c>: the attempt never completes.</summary>
        public void RespondWithTimeout()
        {
            if (TryAnswer())
            {
                _transport.TimeOutConnection(new TimeoutException("The mock WebSocket connection attempt timed out."));
            }
        }

        /// <summary>
        /// The specs' <c>respond_with_error(error_message, then_close = true)</c>: the WebSocket connects, the
        /// server sends <paramref name="errorMessage"/> (an ERROR), then closes normally unless told not to.
        /// </summary>
        public void RespondWithError(JObject errorMessage, bool thenClose = true)
        {
            if (!TryAnswer())
            {
                return;
            }

            _transport.CompleteConnection();
            _transport.DeliverToClient(errorMessage);
            if (thenClose)
            {
                _transport.ServerDisconnect(null, clean: true);
            }
        }

        /// <summary>Server actions on the connection this attempt yields, so a handler can respond then send.</summary>
        public void SendToClient(JObject message) => Connection.SendToClient(message);

        /// <inheritdoc cref="MockConnection.SendToClientAndClose"/>
        public void SendToClientAndClose(JObject message) => Connection.SendToClientAndClose(message);

        /// <inheritdoc cref="MockConnection.SendToClientRaw(string)"/>
        public void SendToClientRaw(string json) => Connection.SendToClientRaw(json);

        /// <inheritdoc cref="MockConnection.SimulateDisconnect"/>
        public void SimulateDisconnect(ErrorInfo error = null) => Connection.SimulateDisconnect(error);

        /// <inheritdoc cref="MockConnection.CloseFromServer"/>
        public void CloseFromServer() => Connection.CloseFromServer();

        public override string ToString() => Url.ToString();

        private bool TryAnswer() => Interlocked.Exchange(ref _answered, 1) == 0;

        private void Fail(Exception error)
        {
            if (TryAnswer())
            {
                _transport.FailConnection(error);
            }
        }
    }
}
