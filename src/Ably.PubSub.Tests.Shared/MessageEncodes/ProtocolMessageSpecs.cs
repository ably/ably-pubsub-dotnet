using Xunit;

namespace IO.Ably.Tests
{
    public class ProtocolMessageSpecs
    {
        [Fact]
        public void WithMsgPackEncoding_CanSerialiseAndDeserializeProtocolMessage()
        {
#if MSGPACK
            if (!Defaults.MsgPackEnabled)
            {
                return;
            }

            var message = new ProtocolMessage(ProtocolMessage.MessageAction.Presence, "boo");
            message.Presence = new[] { new PresenceMessage(PresenceAction.Enter, "123", "my data") };

            var data = MsgPackHelper.Serialise(message);
            var result = MsgPackHelper.Deserialise(data, typeof(ProtocolMessage)) as ProtocolMessage;

            result.Action.Should().Be(message.Action);
            result.Presence.First().Data.Should().Be(message.Presence[0].Data);
#endif
        }
    }
}
