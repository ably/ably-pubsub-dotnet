using System;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>The event types of <c>mock_websocket.md</c>'s unified timeline.</summary>
    public enum MockEventType
    {
        ConnectionAttempt,
        ConnectionSuccess,
        ConnectionFailure,
        MessageFromClient,
        MessageToClient,
        PingFrame,
        ServerDisconnect,
        ClientClose,
    }

    /// <summary>One entry in <c>MockWebSocket.Events</c>.</summary>
    public sealed class MockEvent
    {
        internal MockEvent(MockEventType type, DateTimeOffset timestamp, object data)
        {
            Type = type;
            Timestamp = timestamp;
            Data = data;
        }

        public MockEventType Type { get; }

        public DateTimeOffset Timestamp { get; }

        /// <summary>Event-specific: a pending connection, a protocol message, an error, a close event.</summary>
        public object Data { get; }

        public override string ToString() => $"{Type}" + (Data == null ? string.Empty : $" ({Data})");
    }

    /// <summary>
    /// The spec's <c>ClientCloseEvent</c>. .NET's <c>ITransport.Close(bool suppressClosedEvent)</c> carries
    /// no WebSocket close code, so <see cref="Code"/> is the normal-closure code the real transport would
    /// send and <see cref="SuppressClosedEvent"/> carries the one thing the SDK actually varies.
    /// </summary>
    public sealed class ClientCloseEvent
    {
        internal ClientCloseEvent(bool suppressClosedEvent)
        {
            SuppressClosedEvent = suppressClosedEvent;
        }

        public int Code => 1000;

        public string Reason => null;

        public bool SuppressClosedEvent { get; }

        public override string ToString() => $"close(suppressClosedEvent: {SuppressClosedEvent})";
    }
}
