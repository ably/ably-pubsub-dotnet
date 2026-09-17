namespace IO.Ably.Realtime
{
    /// <summary>A series of connection states.</summary>
    public enum ConnectionEvent
    {
        /// <summary>
        /// Connection is initialised. This is initial state when the library is initialised.
        /// </summary>
        Initialized = 0,

        /// <summary>
        /// Trying to connect to the server.
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// Connected to the server.
        /// </summary>
        Connected = 2,

        /// <summary>
        /// Disconnected from the server. Usually followed by Connecting or Suspended.
        /// </summary>
        Disconnected = 3,

        /// <summary>
        /// The connection was suspended. Usually followed by retry.
        /// </summary>
        Suspended = 4,

        /// <summary>
        /// The connection is closing. Usually followed by Closed or Failed.
        /// </summary>
        Closing = 5,

        /// <summary>
        /// The connection is Closed. This is an end state and won't change until Connect() is called.
        /// </summary>
        Closed = 6,

        /// <summary>
        /// Connection is in a failed state. This is a terminal state.
        /// </summary>
        Failed = 7,

        /// <summary>
        /// The current state was updated. Usually happens if the Auth is updated.
        /// </summary>
        Update = 8
    }
}
