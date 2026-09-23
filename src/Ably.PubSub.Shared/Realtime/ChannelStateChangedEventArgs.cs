using System;
using IO.Ably.Types;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// Class representing a channel state change.
    /// </summary>
    public class ChannelStateChange : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelStateChange"/> class.
        /// </summary>
        /// <param name="e">channel event.</param>
        /// <param name="state">Current state of the channel.</param>
        /// <param name="previous">Previous state of the channel.</param>
        /// <param name="error">Error if any.</param>
        /// <param name="protocolMessage">Protocol message.</param>
        public ChannelStateChange(ChannelEvent e, ChannelState state, ChannelState previous, ErrorInfo error = null, ProtocolMessage protocolMessage = null)
            : this(e, state, previous, error, false)
        {
            ProtocolMessage = protocolMessage;
            Resumed = protocolMessage != null && protocolMessage.HasFlag(ProtocolMessage.Flag.Resumed);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelStateChange"/> class.
        /// </summary>
        /// <param name="e">channel event.</param>
        /// <param name="state">Current state of the channel.</param>
        /// <param name="previous">Previous state of the channel.</param>
        /// <param name="error">Error if any.</param>
        /// <param name="resumed">whether the connection was resumed.</param>
        public ChannelStateChange(ChannelEvent e, ChannelState state, ChannelState previous, ErrorInfo error = null, bool resumed = false)
        {
            Event = e;
            Previous = previous;
            Current = state;
            Error = error;
            Resumed = resumed;
        }

        /// <summary>
        /// Previous channel state.
        /// </summary>
        public ChannelState Previous { get; }

        /// <summary>
        /// Current channel state.
        /// </summary>
        public ChannelState Current { get; }

        /// <summary>
        /// Error if any that caused the state change.
        /// TODO: Update to Reason to be inline with the other libraries.
        /// </summary>
        public ErrorInfo Error { get; }

        /// <summary>
        /// Whether the connection was resumed.
        /// </summary>
        public bool Resumed { get; }

        /// <summary>
        /// Channel event.
        /// </summary>
        public ChannelEvent Event { get; }

        internal ProtocolMessage ProtocolMessage { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Current: {Current}. Previous: {Previous}. Error: {Error}. Resumed: {Resumed}. Event: {Event}";
        }
    }
}
