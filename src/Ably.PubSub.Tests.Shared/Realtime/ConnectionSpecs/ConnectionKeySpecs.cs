using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("spec", "RTN9")]
    public class ConnectionKeySpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN9a")]
        public void UntilConnected_ShouldBeNull()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTN9b")]
        [Trait("sandboxTest", "needed")]
        public async Task OnceConnected_ShouldUseKeyFromConnectedMessage()
        {
            var client = GetClientWithFakeTransport();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected) { ConnectionDetails = new ConnectionDetails { ConnectionKey = "key" } });
            await client.WaitForState(ConnectionState.Connected);
            client.Connection.Key.Should().Be("key");
        }

        [Fact]
        [Trait("spec", "RTN9b")]
        public async Task WhenRestoringConnection_UsesConnectionKey()
        {
            // Arrange
            const string targetKey = "1234567";
            var client = GetClientWithFakeTransport();
            client.State.Connection.Key = targetKey;

            // Act
            var transportParamsForReconnect = await client.ConnectionManager.CreateTransportParameters("https://realtime.ably.io");

            // Assert
            transportParamsForReconnect
                .ConnectionKey.Should().Be(targetKey);
        }

        public ConnectionKeySpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
