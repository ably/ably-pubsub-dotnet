using System;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// A series of connection states.
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// A connection object having this state has been initialized but no connection has yet been
        /// attempted.
        /// </summary>
        Initialized = 0,

        /// <summary>
        /// A connection attempt has been initiated. The connecting state is entered as soon as the library
        /// has completed initialization, and is reentered each time connection is re-attempted following
        /// disconnection.
        /// </summary>
        Connecting = 1,

        /// <summary>A connection exists and is active.</summary>
        Connected = 2,

        /// <summary>A temporary failure condition.
        /// No current connection exists because there is no network connectivity or no host is unavailable.
        /// </summary>
        /// <remarks>The disconnected state is entered if an established connection is dropped, or if a connection
        /// attempt was unsuccessful. In the disconnected state the library will trigger a new connection
        /// attempt after a short period, anticipating that the connection will be re-established soon, and
        /// connection continuity will be possible. Clients can continue to publish messages, and these will
        /// be queued, to be sent as soon as a connection is established.</remarks>
        Disconnected = 3,

        /// <summary>A long term failure condition.
        /// No current connection exists because there is no network
        /// connectivity or no host is unavailable.
        /// </summary>
        /// <remarks>The suspended state is entered after a failed connection attempt if there has then been no
        /// connection for a period of two minutes. A new connection attempt will be triggered after a
        /// further two minutes. In the suspended state clients are unable to publish messages. A new
        /// connection attempt can also be triggered by an explicit call to connect() on the connection
        /// object.</remarks>
        Suspended = 4,

        /// <summary>
        /// A call to Close has been made. The connection will temporarily move to Closing until a CLOSED message
        /// is received from the server or a timeout has been reached.
        /// </summary>
        Closing = 5,

        /// <summary>The connection has been explicitly closed by the client.</summary>
        /// <remarks>In the closed state, no reconnection attempts are made automatically by the library, and clients
        /// may not publish messages. No connection state is preserved by the service or by the library. A
        /// new connection attempt can be triggered by an explicit call to connect() on the connection
        /// object, which will result in a new connection.
        /// </remarks>
        Closed = 6,

        /// <summary>
        /// An indefinite failure condition. This state is entered if a connection error has been received from
        /// the Ably service (such as an attempt to connect with a non-existent appId or with invalid
        /// credentials). A failed state may also be triggered by the client library directly as a result of some
        /// local permanent error.</summary>
        /// <remarks>
        /// In the failed state, no reconnection attempts are made automatically by the library, and clients
        /// may not publish messages. A new connection attempt can be triggered by an explicit call to
        /// Connect() on the connection object.
        /// </remarks>
        Failed = 7
    }

    internal static class ConnectionStateExtensions
    {
        public static ConnectionEvent ToConnectionEvent(this ConnectionState state)
        {
            if (Enum.IsDefined(typeof(ConnectionEvent), (int)state))
            {
                return (ConnectionEvent)state;
            }

            throw new ArgumentOutOfRangeException($"ConnectionState '{state}' cannot be cast to a ConnectionEvent.");
        }
    }
}
