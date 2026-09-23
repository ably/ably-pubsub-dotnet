namespace IO.Ably.Realtime
{
    /// <summary>
    /// Channel properties.
    /// </summary>
    public class ChannelProperties
    {
        /// <summary>
        /// contains the channelSerial from latest ATTACHED ProtocolMessage received on the channel, see CP2a, RTL15a.
        /// </summary>
        public string AttachSerial { get; internal set; }

        /// <summary>
        /// contains the channelSerial from latest ProtocolMessage of action type Message/PresenceMessage received on the channel, see CP2b, RTL15b.
        /// </summary>
        public string ChannelSerial { get; internal set; }
    }
}
