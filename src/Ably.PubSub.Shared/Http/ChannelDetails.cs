using Newtonsoft.Json;

namespace IO.Ably.Rest
{
#pragma warning disable SA1307
#pragma warning disable SA1600

    public class ChannelDetails
    {
        /// <summary>
        /// The required name of the channel including any qualifier, if any.
        /// </summary>
        [JsonProperty("channelId")]
        public string ChannelId { get; set; }

        /// <summary>
        /// The status and occupancy stats for the channel.
        /// </summary>
        [JsonProperty("status")]
        public ChannelStatus Status { get; set; }
    }

    public class ChannelStatus
    {
        /// <summary>
        /// Indicates whether the channel that is the subject of the event is active.
        /// </summary>
        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        /// <summary>
        /// Metadata relating to the occupants of the channel.
        /// </summary>
        [JsonProperty("occupancy")]
        public ChannelOccupancy Occupancy { get; set; }
    }

    /// <summary>
    /// Metadata relating to the occupants of the channel.
    /// </summary>
    public class ChannelOccupancy
    {
        [JsonProperty("metrics")]
        public ChannelMetrics Metrics { get; set; }
    }

    public class ChannelMetrics
    {
        /// <summary>
        /// The number of connections.
        /// </summary>
        [JsonProperty("connections")]
        public int Connections { get; set; }

        /// <summary>
        /// The number of connections attached to the channel that are authorised to publish.
        /// </summary>
        [JsonProperty("publishers")]
        public int Publishers { get; set; }

        /// <summary>
        /// The number of connections attached that are authorised to subscribe to messages.
        /// </summary>
        [JsonProperty("subscribers")]
        public int Subscribers { get; set; }

        /// <summary>
        /// The number of connections that are authorised to enter members into the presence channel.
        /// </summary>
        [JsonProperty("presenceConnections")]
        public int PresenceConnections { get; set; }

        /// <summary>
        /// The number of members currently entered into the presence channel.
        /// </summary>
        [JsonProperty("presenceMembers")]
        public int PresenceMembers { get; set; }

        /// <summary>
        /// The number of connections that are authorised to subscribe to presence messages.
        /// </summary>
        [JsonProperty("presenceSubscribers")]
        public int PresenceSubscribers { get; set; }
    }
}
