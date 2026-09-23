using System;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// Whenever the connection state changes, a ConnectionStateChange Event is emitted on the Connection object
    /// The ConnectionStateChange object contains the current state in attribute current, the previous state in attribute previous, and when the client is not connected and a connection attempt will be made automatically by the library, the amount of time in milliseconds until the next retry in the attribute retryIn
    /// If the connection state change includes error information, then the reason attribute will contain an ErrorInfo object describing the reason for the error
    /// <see href="Http://docs.ably.com/client-lib-development-guide/features/#TA1"/>.
    /// </summary>
    public class ConnectionStateChange : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStateChange"/> class.
        /// </summary>
        /// <param name="connectionEvent">Connection event.</param>
        /// <param name="previous">previous connection state.</param>
        /// <param name="current">current connection state.</param>
        /// <param name="retryIn">if necessary, how long ably will wait until retry.</param>
        /// <param name="reason">if present, the error reason for this state change.</param>
        public ConnectionStateChange(ConnectionEvent connectionEvent, ConnectionState previous, ConnectionState current, TimeSpan? retryIn = null, ErrorInfo reason = null)
        {
            Event = connectionEvent;
            Previous = previous;
            Current = current;
            RetryIn = retryIn;
            Reason = reason;
        }

        /// <summary>
        /// Previous connection state. <see cref="ConnectionState"/>.
        /// </summary>
        public ConnectionState Previous { get; }

        /// <summary>
        /// Current connection Event. <see cref="ConnectionEvent"/>.
        /// </summary>
        public ConnectionEvent Event { get; }

        /// <summary>
        /// Current Connection State <see cref="ConnectionState"/>.
        /// </summary>
        public ConnectionState Current { get; }

        /// <summary>
        /// Optional RetryIn.
        /// </summary>
        public TimeSpan? RetryIn { get; }

        /// <summary>
        /// The <see cref="ErrorInfo">error reason</see> why the connection transitioned in the current state.
        /// </summary>
        public ErrorInfo Reason { get; }

        /// <summary>
        /// Has error.
        /// </summary>
        public bool HasError => Reason != null;
    }
}
