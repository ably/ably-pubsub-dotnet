using System;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// EventArgs class used when a Channel error gets raised.
    /// </summary>
    public class ChannelErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Error reason.
        /// </summary>
        public ErrorInfo Reason { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelErrorEventArgs"/> class.
        /// </summary>
        /// <param name="reason">Error reason.</param>
        public ChannelErrorEventArgs(ErrorInfo reason)
        {
            Reason = reason;
        }
    }
}
