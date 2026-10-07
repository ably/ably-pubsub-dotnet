using System;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The server side of one established mock WebSocket connection — what a test uses to act *as* the
    /// server. The library's own side (sending, closing) is <see cref="MockTransport"/> and a test does
    /// not call it.
    /// </summary>
    public sealed class MockConnection
    {
        private readonly MockTransport _transport;

        internal MockConnection(MockTransport transport, bool binaryProtocol)
        {
            _transport = transport;
            BinaryProtocol = binaryProtocol;
        }

        /// <summary>
        /// The protocol the client negotiated. Always JSON in this SDK build: msgpack is compiled out
        /// (<c>ClientOptions.UseBinaryProtocol</c>'s getter returns a hard-coded false without the
        /// <c>MSGPACK</c> define), so this is here for the specs that assert on it rather than as a switch.
        /// </summary>
        public bool BinaryProtocol { get; }

        public string Protocol => BinaryProtocol ? "application/x-msgpack" : "application/json";

        public bool Closed => _transport.ClosedByServer || _transport.ClosedByClient;

        /// <summary>Deliver a protocol message to the client, leaving the connection open.</summary>
        public void SendToClient(JObject message) => _transport.DeliverToClient(message);

        /// <summary>
        /// Deliver a message and then close, which is what the server does for DISCONNECTED and for a
        /// connection-level ERROR (one without a channel).
        /// </summary>
        public void SendToClientAndClose(JObject message)
        {
            _transport.DeliverToClient(message);

            // A normal closure. The server always closes after DISCONNECTED or a connection-level
            // ERROR, and that close is not itself a fault.
            _transport.ServerDisconnect(null, clean: true);
        }

        /// <summary>An unexpected transport failure: the connection drops with no message.</summary>
        public void SimulateDisconnect(ErrorInfo error = null)
            => _transport.ServerDisconnect(error, clean: false);

        /// <summary>
        /// The spec's <c>send_ping_frame()</c> (RTN23b). .NET's <c>ClientWebSocket</c> answers ping frames
        /// inside the protocol and surfaces no event to <c>ITransport</c>, so there is nothing for this to
        /// drive — it records the event so a test can see it was attempted, and the RTN23b specs are
        /// recorded as a Mock Infrastructure Limitation instead. RTN23a via a HEARTBEAT message works.
        /// </summary>
        public void SendPingFrame() => _transport.RecordPingFrame();

        public override string ToString() => $"MockConnection({_transport.Id})";
    }
}
