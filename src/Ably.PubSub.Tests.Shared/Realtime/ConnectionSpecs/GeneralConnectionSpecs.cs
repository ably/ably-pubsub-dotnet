using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    public class GeneralConnectionSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN1")]
        public async Task ShouldUseWebSocketTransport()
        {
            var client = await GetConnectedClient(); // The transport is created by the connecting state

            client.ConnectionManager.Transport.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "G4")]
        public async Task ShouldAddVersionToCreatedWebSocketTransport()
        {
            var client = await GetConnectedClient();

            // We care about the TransportParams GetUri() function. It doesn't matter it's a test transport
            var webSocketTransport = client.ConnectionManager.Transport as FakeTransport;
            webSocketTransport.Parameters.GetUri().ToString().Should().Contain("v=" + Defaults.ProtocolVersion);
        }

        [Fact]
        [Trait("spec", "RTN3")]
        [Trait("spec", "RTN6")]
        public async Task WithAutoConnect_CallsConnectOnTransport()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = true);
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));

            await client.WaitForState(ConnectionState.Connected);

            LastCreatedTransport.ConnectCalled.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RTN3")]
        public void WithAutoConnectFalse_LeavesStateAsInitialized()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);

            client.ConnectionManager.ConnectionState.Should().Be(ConnectionState.Initialized);
            LastCreatedTransport.Should().BeNull("Transport shouldn't be created without calling connect when AutoConnect is false");
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RTN19")]
        public async Task WhenConnectedMessageReceived_ConnectionShouldBeInConnectedStateAndConnectionDetailsAreUpdated()
        {
            var client = GetClientWithFakeTransport();

            var connectionDetailsMessage = new ConnectionDetails
            {
                ClientId = "123",
                ConnectionKey = "boo"
            };
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = connectionDetailsMessage
            });

            await client.WaitForState(ConnectionState.Connected);

            client.Connection.Key.Should().Be("boo");
        }

        [Fact]
        [Trait("spec", "RSA15a")]
        [Trait("sandboxTest", "needed")]
        public async Task WhenConnectedMessageReceivedWithClientId_AblyAuthShouldUseConnectionClientId()
        {
            var client = GetClientWithFakeTransport();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ClientId = "realtimeClient" }
            });

            await client.ProcessCommands();

            client.RestClient.AblyAuth.ClientId.Should().Be("realtimeClient");
        }

        public GeneralConnectionSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
