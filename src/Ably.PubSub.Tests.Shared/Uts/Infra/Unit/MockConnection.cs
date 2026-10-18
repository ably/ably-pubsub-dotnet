using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The server side of one mock WebSocket connection: what a test uses to act as the server
    /// (<c>mock_ws.active_connection.send_to_client(...)</c> and the rest). Every delivery to the client is
    /// asynchronous and ordered (see <see cref="MockTransport"/>); every action is recorded on the mock's
    /// timeline at once. Acting on a closed connection throws, rather than dropping the message silently.
    /// </summary>
    public sealed class MockConnection
    {
        private readonly MockTransport _transport;

        internal MockConnection(MockTransport transport)
        {
            _transport = transport;
        }

        /// <summary>The URL the client connected to.</summary>
        public RecordedUrl Url => new RecordedUrl(_transport.Uri);

        /// <summary>Always <c>application/json</c> in this SDK build (msgpack is compiled out).</summary>
        public string Protocol => _transport.BinaryProtocol ? "application/x-msgpack" : "application/json";

        /// <summary>True once either side has closed it.</summary>
        public bool Closed => _transport.ClosedByServer || _transport.ClosedByClient;

        /// <summary>The specs' <c>send_to_client(message)</c>: deliver a message and leave the connection open.</summary>
        public void SendToClient(JObject message) => _transport.DeliverToClient(message);

        /// <summary>
        /// The specs' <c>send_to_client_and_close(message)</c>: deliver, then close normally (SERVER_DISCONNECT).
        /// What the server does after DISCONNECTED or a connection-level ERROR (one without a channel).
        /// </summary>
        public void SendToClientAndClose(JObject message)
        {
            _transport.DeliverToClient(message);
            _transport.ServerDisconnect(null, clean: true);
        }

        /// <summary>
        /// The specs' <c>send_to_client_raw(json)</c>: deliver the text exactly as given, so fields and
        /// action numbers the SDK's types don't know survive to the wire (forwards-compatibility specs).
        /// </summary>
        public void SendToClientRaw(string json) => _transport.DeliverRawToClient(json);

        /// <summary><see cref="SendToClientRaw(string)"/> for a JSON object literal.</summary>
        public void SendToClientRaw(JObject json) => _transport.DeliverRawToClient(json.ToString(Newtonsoft.Json.Formatting.None));

        /// <summary>The specs' <c>simulate_disconnect(error?)</c>: an unexpected transport failure, no message.</summary>
        public void SimulateDisconnect(ErrorInfo error = null) => _transport.ServerDisconnect(error, clean: false);

        /// <summary>The specs' <c>close_from_server()</c>: the server closes the socket normally, with no message.</summary>
        public void CloseFromServer() => _transport.ServerDisconnect(null, clean: true);

        /// <summary>
        /// The specs' <c>send_ping_frame()</c> (RTN23b). Records a PING_FRAME and nothing else: .NET's
        /// <c>ClientWebSocket</c> answers ping frames inside the protocol and surfaces no event, so the SDK
        /// can't observe them and there is nothing to drive. RTN23a via HEARTBEAT messages works.
        /// </summary>
        public void SendPingFrame() => _transport.SendPingFrame();

        public override string ToString() => $"MockConnection({_transport.Id})";
    }
}
