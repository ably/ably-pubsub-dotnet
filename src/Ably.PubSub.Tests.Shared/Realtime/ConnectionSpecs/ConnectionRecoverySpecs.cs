using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime.ConnectionSpecs
{
    public class ConnectionRecoverySpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN16g")]
        [Trait("spec", "RTN16g1")]
        public async Task CreateRecoveryKey_ShouldReturnSerializedConnectionKeyAndMsgSerialAndChannelSerials()
        {
            const string expectedRecoveryKey = "{\"connectionKey\":\"connectionKey\",\"msgSerial\":0,\"channelSerials\":{}}";

            var client = GetClientWithFakeTransport();
            var connectedProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                ConnectionId = "1"
            };
            client.FakeProtocolMessageReceived(connectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);

            client.Connection.CreateRecoveryKey().Should().Be(expectedRecoveryKey);
        }

        [Fact]
        [Trait("spec", "RTN16g2")]
        public async Task CreateRecoveryKey_ShouldReturnNullRecoveryKeyForNullConnectionKeyOrWhenStateIsClosed()
        {
            var client = GetClientWithFakeTransport();
            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty(); // connectionKey is empty

            client.FakeProtocolMessageReceived(ConnectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);
            client.Connection.CreateRecoveryKey().Should().NotBeNullOrEmpty();

            client.Close();
            await client.WaitForState(ConnectionState.Closed);
            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTN16m")]
        [System.Obsolete]
        public async Task DeprecatedRecoveryKeyProperty_ShouldBehaveSameAsCreateRecoveryKey()
        {
            const string expectedRecoveryKey = "{\"connectionKey\":\"connectionKey\",\"msgSerial\":0,\"channelSerials\":{}}";

            var client = GetClientWithFakeTransport();
            var connectedProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                ConnectionId = "1"
            };
            client.FakeProtocolMessageReceived(connectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);

            client.Connection.RecoveryKey.Should().Be(expectedRecoveryKey);
        }

        [Fact]
        [Trait("spec", "RTN16i")]
        [Trait("spec", "RTN16f")]
        [Trait("spec", "RTN16j")]
        [Trait("spec", "RTN16k")]
        public async Task RecoveryKey_MsgSerialShouldNotBeSentToAblyButShouldBeSetOnConnection()
        {
            var recoveryKey =
                "{\"connectionKey\":\"uniqueKey\",\"msgSerial\":45,\"channelSerials\":{\"channel1\":\"1\",\"channel2\":\"2\",\"channel3\":\"3\"}}";
            var client = GetClientWithFakeTransport(options => { options.Recover = recoveryKey; });

            var transportParams = await client.ConnectionManager.CreateTransportParameters("https://realtime.ably.io");
            var paramsDict = transportParams.GetParams();
            paramsDict.ContainsKey("recover").Should().BeTrue();
            paramsDict["recover"].Should().Be("uniqueKey");

            client.FakeProtocolMessageReceived(ConnectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);

            client.Connection.MessageSerial.Should().Be(45);
            client.Channels.Count().Should().Be(3);
            var channelCounter = 1;
            foreach (var realtimeChannel in client.Channels.OrderBy(channel => channel.Name))
            {
                realtimeChannel.Name.Should().Be($"channel{channelCounter}");
                realtimeChannel.Properties.ChannelSerial.Should().Be($"{channelCounter}");
                channelCounter++;
            }

            // Recover should be set to null once used
            client.Options.Recover.Should().BeNull();

            client.Connection.InnerState.MessageSerial = 0;
            client.Channels.ReleaseAll();

            client.ExecuteCommand(SetDisconnectedStateCommand.Create(null, true));
            await client.WaitForState(ConnectionState.Disconnected);
            await client.WaitForState(ConnectionState.Connecting);

            transportParams = await client.ConnectionManager.CreateTransportParameters("https://realtime.ably.io");
            paramsDict = transportParams.GetParams();
            paramsDict.ContainsKey("recover").Should().BeFalse(); // recover param should be empty for next attempt

            client.FakeProtocolMessageReceived(ConnectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);

            // Recover options shouldn't be used for next retry
            client.Connection.MessageSerial.Should().Be(0);
            client.Channels.Count().Should().Be(0);
        }

        public ConnectionRecoverySpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
