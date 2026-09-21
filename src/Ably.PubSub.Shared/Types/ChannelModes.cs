using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace IO.Ably
{
    /// <summary>
    /// Realtime channel modes.
    /// </summary>
    public enum ChannelMode
    {
        /// <summary>
        /// Presence mode. Allows the attached channel to enter Presence.
        /// </summary>
        Presence,

        /// <summary>
        /// Publish mode. Allows the messages to be published to the attached channel.
        /// </summary>
        Publish,

        /// <summary>
        /// Subscribe mode. Allows the attached channel to subscribe to messages.
        /// </summary>
        Subscribe,

        /// <summary>
        /// PresenceSubscribe. Allows the attached channel to subscribe to Presence updates.
        /// </summary>
        PresenceSubscribe,
    }

    /// <summary>
    /// Set of Channel modes. It's used inside ChannelOptions.
    /// </summary>
    public class ChannelModes : HashSet<ChannelMode>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelModes"/> class.
        /// </summary>
        public ChannelModes()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelModes"/> class.
        /// </summary>
        /// <param name="modes">A list of modes to be populated.</param>
        public ChannelModes(IEnumerable<ChannelMode> modes)
            : base(modes)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelModes"/> class.
        /// </summary>
        /// <param name="modes">A list of modes to be populated.</param>
        public ChannelModes(params ChannelMode[] modes)
            : base(modes)
        {
        }
    }

    /// <summary>
    /// Read only version of <see cref="ChannelModes"/>.
    /// </summary>
    public class ReadOnlyChannelModes : ReadOnlyCollection<ChannelMode>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyChannelModes"/> class.
        /// </summary>
        /// <param name="list">list of channelModes.</param>
        public ReadOnlyChannelModes(IList<ChannelMode> list)
            : base(list)
        {
        }
    }
}
