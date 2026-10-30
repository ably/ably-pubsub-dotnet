using System;
using Ably.PubSub.Types;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>The event types of <c>mock_websocket.md</c>'s unified timeline (<c>MockEventType</c>).</summary>
    public enum MockEventType
    {
        /// <summary>The client attempted to connect. Data: the <see cref="PendingWebSocketConnection"/>.</summary>
        ConnectionAttempt,

        /// <summary>A connection was established. Data: the <see cref="MockConnection"/>.</summary>
        ConnectionSuccess,

        /// <summary>A connection attempt failed (refused, timeout, DNS). Data: the <see cref="Exception"/>.</summary>
        ConnectionFailure,

        /// <summary>The client sent a protocol message. Data: the decoded <see cref="ProtocolMessage"/>.</summary>
        MessageFromClient,

        /// <summary>The test sent a message to the client. Data: the <see cref="JObject"/> (or the raw string).</summary>
        MessageToClient,

        /// <summary>The test sent a WebSocket ping frame. Data: the <see cref="MockConnection"/>.</summary>
        PingFrame,

        /// <summary>
        /// The server closed the connection or the transport failed. Data: the <see cref="ErrorInfo"/>, or null.
        /// </summary>
        ServerDisconnect,

        /// <summary>The client closed the WebSocket. Data: the <see cref="ClientCloseEvent"/>.</summary>
        ClientClose,
    }

    /// <summary>One entry of <c>MockWebSocket.Events</c>: the spec's <c>MockEvent{type, timestamp, data}</c>.</summary>
    public sealed class MockEvent
    {
        internal MockEvent(MockEventType type, DateTimeOffset timestamp, object data)
        {
            Type = type;
            Timestamp = timestamp;
            Data = data;
        }

        public MockEventType Type { get; }

        /// <summary>When it happened: fake time when a fake clock is installed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Event-specific data; see <see cref="MockEventType"/>.</summary>
        public object Data { get; }

        /// <summary><see cref="Data"/> as a client message (MESSAGE_FROM_CLIENT), else null.</summary>
        public ProtocolMessage Message => Data as ProtocolMessage;

        /// <summary><see cref="Data"/> as a server message (MESSAGE_TO_CLIENT), else null.</summary>
        public JObject Json => Data as JObject;

        public override string ToString() => Data == null ? Type.ToString() : $"{Type} ({Data})";
    }

    /// <summary>The spec's <c>ClientCloseEvent{code, reason}</c>.</summary>
    public sealed class ClientCloseEvent
    {
        internal ClientCloseEvent(bool suppressClosedEvent, bool viaDispose)
        {
            SuppressClosedEvent = suppressClosedEvent;
            ViaDispose = viaDispose;
        }

        public int? Code => 1000;

        public string Reason => null;

        /// <summary>The argument of <c>Close(...)</c>; true for a <c>Dispose()</c>.</summary>
        public bool SuppressClosedEvent { get; }

        /// <summary>
        /// True when the SDK tore the transport down with <c>Dispose()</c> (its usual path) rather than
        /// <c>Close()</c>.
        /// </summary>
        public bool ViaDispose { get; }

        public override string ToString() => ViaDispose ? "dispose()" : $"close(suppressClosedEvent: {SuppressClosedEvent})";
    }
}
