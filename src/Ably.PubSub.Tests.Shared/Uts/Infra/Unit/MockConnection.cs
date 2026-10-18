using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The server side of one mock WebSocket connection: what a test uses to act as the server
    /// (<c>mock_ws.active_connection.send_to_client(...)</c> and the rest).
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
        /// </summary>
        public void SendToClientAndClose(JObject message)
        {
            _transport.DeliverToClient(message);
            _transport.ServerDisconnect(null, clean: true);
        }

        /// <summary>
        /// The specs' <c>send_to_client_raw(json)</c>: deliver the text exactly as given, so fields and action
        /// numbers the SDK's types don't know survive to the wire (forwards-compatibility specs).
        /// </summary>
        public void SendToClientRaw(string json) => _transport.DeliverRawToClient(json);

        /// <summary><see cref="SendToClientRaw(string)"/> for a JSON object literal.</summary>
        public void SendToClientRaw(JObject json) => _transport.DeliverRawToClient(json.ToString(Newtonsoft.Json.Formatting.None));

        /// <summary>The specs' <c>simulate_disconnect(error?)</c>: an unexpected transport failure, no message.</summary>
        public void SimulateDisconnect(ErrorInfo error = null) => _transport.ServerDisconnect(error, clean: false);

        /// <summary>The specs' <c>close_from_server()</c>: the server closes the socket normally, with no message.</summary>
        public void CloseFromServer() => _transport.ServerDisconnect(null, clean: true);

        /// <summary>The specs' <c>send_ping_frame()</c> (RTN23b).</summary>
        public void SendPingFrame() => _transport.SendPingFrame();

        public override string ToString() => $"MockConnection({_transport.Id})";
    }
}
