namespace IO.Ably.Realtime
{
    /*
     The values assigned to each enum should correspond with those assigned in ChannelEvent.
    */

    /// <summary>
    /// States defined for a channel. ChannelEvents are a logical subset of <see cref="ChannelEvent"/> (which has the additional Update event).
    /// </summary>
    public enum ChannelState
    {
        /// <summary>
        /// A channel object having this state has been initialized but no attach has
        /// yet been attempted.
        /// </summary>
        Initialized = 0,

        /// <summary>
        /// An attach has been initiated by sending a request to the service. This is a
        /// transient state; it will be followed either by a transition to Attached or Failed.
        /// </summary>
        Attaching = 1,

        /// <summary>
        /// Attach has succeeded. In the attached state a client may publish, and
        /// subscribe to messages.
        /// </summary>
        Attached = 2,

        /// <summary>
        /// An detach has been initiated by sending a request to the service. This is a
        /// transient state; it will be followed either by a transition to Detached or Failed.
        /// </summary>
        Detaching = 3,

        /// <summary>
        /// The channel, having previously been attached, has been detached.
        /// </summary>
        Detached = 4,

        /// <summary>
        /// If the connection state enters the SUSPENDED state, then an ATTACHING or ATTACHED channel state will transition to SUSPENDED (RTL3c)
        /// </summary>
        Suspended = 5,

        /// <summary>
        /// An indefinite failure condition. This state is entered if a channel error has
        /// been received from the Ably service (such as an attempt to attach without the
        /// necessary access rights).
        /// </summary>
        Failed = 6
    }
}
