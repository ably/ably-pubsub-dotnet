namespace IO.Ably
{
    internal static class PresenceExtensions
    {
        public static bool IsServerSynthesized(this PresenceMessage msg)
        {
            return msg.Id == null || !msg.Id.StartsWith(msg.ConnectionId);
        }

        // RTP2b, RTP2c
        public static bool IsNewerThan(this PresenceMessage existingMsg, PresenceMessage incomingMsg)
        {
            // RTP2b1
            if (existingMsg.IsServerSynthesized() || incomingMsg.IsServerSynthesized())
            {
                return existingMsg.Timestamp > incomingMsg.Timestamp;
            }

            // RTP2b2
            var thisValues = existingMsg.Id.Split(':');
            var thatValues = incomingMsg.Id.Split(':');

            // if any part of the message serial fails to parse then throw an exception
            if (thisValues.Length != 3 ||
                !(int.TryParse(thisValues[1], out int existingMsgSerial) | int.TryParse(thisValues[2], out int existingMsgIndex)))
            {
                throw new AblyException($"Parsing error. The Presence Message has an invalid Id '{existingMsg.Id}'.");
            }

            if (thatValues.Length != 3 ||
                !(int.TryParse(thatValues[1], out int incomingMsgSerial) | int.TryParse(thatValues[2], out int incomingMsgIndex)))
            {
                throw new AblyException($"Parsing error. The Presence Message has an invalid Id '{incomingMsg.Id}'.");
            }

            if (existingMsgSerial == incomingMsgSerial)
            {
                return existingMsgIndex > incomingMsgIndex;
            }

            return existingMsgSerial > incomingMsgSerial;
        }
    }
}
