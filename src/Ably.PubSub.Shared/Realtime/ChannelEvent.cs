namespace IO.Ably.Realtime
{
    /// <summary>
    /// Events defined for a channel. ChannelEvents are equal to <see cref="ChannelState"/> with the addition of the Update event.
    /// </summary>
    public enum ChannelEvent
    {
        /// <summary>
        /// Emitted when a channel object having the corresponding state has been initialized but no attach has
        /// yet been attempted.
        /// </summary>
        Initialized = 0,

        /// <summary>
        /// Emitted when an attach has been initiated by sending a request to the service. This indicates a
        /// transient state; it will be followed either by a transition to Attached or Failed.
        /// </summary>
        Attaching = 1,

        /// <summary>
        /// Emitted when Attach has succeeded. In the attached state a client may publish, and
        /// subscribe to messages.
        /// </summary>
        Attached = 2,

        /// <summary>
        /// Emitted when a detach has been initiated by sending a request to the service. This is a
        /// transient state; it will be followed either by a transition to Detached or Failed.
        /// </summary>
        Detaching = 3,

        /// <summary>
        /// Emitted when the channel, having previously been attached, has been detached.
        /// </summary>
        Detached = 4,

        /// <summary>
        /// Emitted when the connection state enters the Suspended state and the channel is in the Attaching or Attached state.
        /// </summary>
        Suspended = 5,

        /// <summary>
        /// An indefinite failure condition. Emitted when a channel error has
        /// been received from the Ably service (such as an attempt to attach without the
        /// necessary access rights).
        /// </summary>
        Failed = 6,

        /// <summary>
        /// Emitted for changes to channel conditions for which the <see cref="ChannelState"/> does not change
        /// </summary>
        Update = 7
    }
}
