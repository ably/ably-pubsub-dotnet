using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    [Trait("spec", "RTN8")]
    public class ConnectionIdSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN8a")]
        public void ConnectionIdIsNull_WhenClientIsNotConnected()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
            client.Connection.Id.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTN8b")]
        [Trait("sandboxTest", "needed")]
        public async Task ConnectionIdSetBasedOnValueProvidedByAblyService()
        {
            var client = GetClientWithFakeTransport();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected) { ConnectionId = "123" });

            await client.WaitForState(ConnectionState.Connected);

            client.Connection.Id.Should().Be("123");
        }

        public ConnectionIdSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
