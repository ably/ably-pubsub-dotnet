using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class RestProtocolTests
    {
#if MSGPACK
        [Fact]
        public void WhenProtocolIsNotDefined_AndMsgPackEnabled_DefaultsToMsgPack()
        {
            var rest = new AblyRest(new ClientOptions());
            rest.Protocol.Should().Be(Protocol.MsgPack);
            Defaults.Protocol.Should().Be(Protocol.MsgPack);
        }

        [Fact]
        public void WhenProtocolIsMsgPack_ProtocolIsSetToMsgPack()
        {
            var rest = new AblyRest(new ClientOptions() { UseBinaryProtocol = true});
            rest.Protocol.Should().Be(Defaults.Protocol);
        }
#else
        [Fact]
        public void WhenProtocolIsNotDefined_AndMsgPackDisabled_DefaultsToJson()
        {
            Defaults.Protocol.Should().Be(Protocol.Json);
            var rest = new AblyRest(new ClientOptions { Key = "best.test:key" });
            rest.Protocol.Should().Be(Protocol.Json);
        }

        [Fact]
        public void WhenMsgPackIsDisabled_AndUseBinaryIsTrue_ProtocolIsSetToJson()
        {
            var rest = new AblyRest(new ClientOptions { UseBinaryProtocol = true, Key = "best.test:key" });
            rest.Protocol.Should().Be(Protocol.Json);
        }
#endif

        [Fact]
        public void WhenProtocolIsJson_RestProtocolIsSetToJson()
        {
            var rest = new AblyRest(new ClientOptions { UseBinaryProtocol = false, Key = "best.test:key" });
            rest.Protocol.Should().Be(Protocol.Json);
        }

        [Fact]
        public void WhenUseBinaryIsFalse_ProtocolIsSetToJson()
        {
            var rest = new AblyRest(new ClientOptions { UseBinaryProtocol = false, Key = "best.test:key" });
            rest.Protocol.Should().Be(Protocol.Json);
        }
    }
}
