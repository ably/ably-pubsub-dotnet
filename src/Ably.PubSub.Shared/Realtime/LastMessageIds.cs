using System.Linq;
using IO.Ably.Types;

namespace IO.Ably.Realtime
{
    internal class LastMessageIds
    {
        public static readonly LastMessageIds Empty = new LastMessageIds();

        // The internal set is to make things easier with testing where I need to simulate mismatching LastMessageIds
        // which should trigger the delta recovery process.
        public string LastMessageId { get; internal set; }

        public string ProtocolMessageChannelSerial { get; }

        public override string ToString()
        {
            return $"LastMessageId: {LastMessageId}. ChannelSerial: {ProtocolMessageChannelSerial}.";
        }

        private LastMessageIds()
        {
        }

        private LastMessageIds(string lastMessageId, string channelSerial)
        {
            LastMessageId = lastMessageId;
            ProtocolMessageChannelSerial = channelSerial;
        }

        public static LastMessageIds Create(ProtocolMessage message)
        {
            if (message.Action != ProtocolMessage.MessageAction.Message
                || message.Messages == null
                || message.Messages.Length == 0)
            {
                return Empty;
            }

            return new LastMessageIds(message.Messages.Last().Id, message.ChannelSerial);
        }
    }
}
