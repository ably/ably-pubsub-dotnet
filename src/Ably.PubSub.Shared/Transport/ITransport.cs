using System;
using IO.Ably.Realtime;

namespace IO.Ably.Transport
{
    /// <summary>
    /// Current state of the websocket transport.
    /// </summary>
    public enum TransportState
    {
        /// <summary>
        /// Never connected.
        /// </summary>
        Initialized,

        /// <summary>
        /// In the process of connecting.
        /// </summary>
        Connecting,

        /// <summary>
        /// Connection has been successfully established.
        /// </summary>
        Connected,

        /// <summary>
        /// In the process of closing the connection.
        /// </summary>
        Closing,

        /// <summary>
        /// Connection has been closed.
        /// </summary>
        Closed,
    }

    /// <summary>
    /// Represents a websocket transport.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Unique id to represent each transport instance.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Current state of the connection.
        /// </summary>
        TransportState State { get; }

        /// <summary>
        /// Current listener class. <see cref="ITransportListener"/>.
        /// </summary>
        ITransportListener Listener { get; set; }

        /// <summary>
        /// Tries to establish a connection.
        /// </summary>
        void Connect();

        /// <summary>
        /// Closes the current connection.
        /// Optionally can suppress the closed event.
        /// </summary>
        /// <param name="suppressClosedEvent">If true, it doesn't emmit the Closed Event.
        /// Default: true.
        /// </param>
        void Close(bool suppressClosedEvent = true);

        /// <summary>
        /// Sends a message.
        /// </summary>
        /// <param name="data">Transport message. <see cref="RealtimeTransportData"/>.</param>
        /// <returns>Result whether the operation succeeded or failed.</returns>
        Result Send(RealtimeTransportData data);
    }

    /// <summary>
    /// Interface representing a transport factory.
    /// </summary>
    public interface ITransportFactory
    {
        /// <summary>
        /// Creates a websocket transport.
        /// </summary>
        /// <param name="parameters">transport parameters.</param>
        /// <returns>instance of a transport object.</returns>
        ITransport CreateTransport(TransportParams parameters);
    }

    /// <summary>
    /// Interface defining the methods required for a transport listener.
    /// </summary>
    public interface ITransportListener
    {
        /// <summary>
        /// Called when data has been received on the transport websocket.
        /// </summary>
        /// <param name="data"><see cref="RealtimeTransportData"/>.</param>
        void OnTransportDataReceived(RealtimeTransportData data);

        /// <summary>
        /// Called when the TransportState changes.
        /// </summary>
        /// <param name="transportId">Unique identifier for the current transport instance.</param>
        /// <param name="state">the new state.</param>
        /// <param name="exception">optional, exception.</param>
        void OnTransportEvent(Guid transportId, TransportState state, Exception exception = null);
    }
}
