namespace IO.Ably
{
    /// <summary>
    /// Protocol used.
    /// </summary>
    public enum Protocol
    {
#if MSGPACK
        /// <summary>
        /// Msg packg binary protocol.
        /// </summary>
        MsgPack = 0,
#endif

        /// <summary>
        /// Json text protocol.
        /// </summary>
        Json = 1
    }
}
