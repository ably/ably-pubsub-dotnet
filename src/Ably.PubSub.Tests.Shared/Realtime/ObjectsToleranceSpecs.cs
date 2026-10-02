using System.Collections.Concurrent;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Infrastructure;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    /// <summary>
    /// Tolerance of the OBJECT and OBJECT_SYNC wire surface (TR2, TR4r, TR3h, RTL15b). This library has no
    /// object support, so such traffic must be accepted and ignored without any effect on connection or channel state.
    /// </summary>
    public class ObjectsToleranceSpecs : AblyRealtimeSpecs
    {
        private const string StatePayload =
            "\"state\":[{\"operation\":{\"action\":0,\"objectId\":\"root\"},\"serial\":\"01\",\"siteCode\":\"aaa\"}]";

        public ObjectsToleranceSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private void InjectRawJson(string json)
        {
            LastCreatedTransport.Listener.OnTransportDataReceived(new RealtimeTransportData(json));
        }

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel, ConcurrentQueue<ConnectionState> ConnectionStates, ConcurrentQueue<ChannelState> ChannelStates)> GetConnectedClientAndAttachedChannel()
        {
            var client = await GetConnectedClient();
            var channel = client.Channels.Get(TestChannelName);
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);

            var connectionStates = new ConcurrentQueue<ConnectionState>();
            client.Connection.On(change => connectionStates.Enqueue(change.Current));
            var channelStates = new ConcurrentQueue<ChannelState>();
            channel.On(change => channelStates.Enqueue(change.Current));

            return (client, channel, connectionStates, channelStates);
        }

        private async Task AssertStillProcessing(PubSubRealtimeClient client)
        {
            // A heartbeat reply following the ignored frame must still be processed.
            string heartbeatId = null;
            var heartbeatSent = new TaskCompletionAwaiter();
            LastCreatedTransport.SetSendAction(data =>
            {
                if (data.Original?.Action == ProtocolMessage.MessageAction.Heartbeat)
                {
                    heartbeatId = data.Original.Id;
                    heartbeatSent.SetCompleted();
                }
            });

            var pingTask = client.Connection.PingAsync();
            (await heartbeatSent.Task).Should().BeTrue();
            InjectRawJson("{\"action\":0,\"id\":\"" + heartbeatId + "\"}");
            (await pingTask).IsSuccess.Should().BeTrue("frames after the ignored object traffic must still be processed");
        }

        [Fact]
        [Trait("spec", "RTL15b")]
        [Trait("spec", "TR4r")]
        public async Task WhenObjectMessageWithStateReceived_ItIsIgnoredAndChannelSerialIsUpdated()
        {
            var (client, channel, connectionStates, channelStates) = await GetConnectedClientAndAttachedChannel();
            channel.Properties.ChannelSerial.Should().BeNull();

            InjectRawJson("{\"action\":19,\"channel\":\"" + TestChannelName + "\",\"channelSerial\":\"object-serial-1\"," + StatePayload + "}");

            await AssertStillProcessing(client);
            channel.Properties.ChannelSerial.Should().Be("object-serial-1");
            channel.State.Should().Be(ChannelState.Attached);
            channelStates.Should().BeEmpty();
            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionStates.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTL15b")]
        public async Task WhenObjectMessageWithoutChannelSerialReceived_ChannelSerialIsLeftUnchanged()
        {
            var (client, channel, _, channelStates) = await GetConnectedClientAndAttachedChannel();
            channel.Properties.ChannelSerial.Should().BeNull();

            InjectRawJson("{\"action\":19,\"channel\":\"" + TestChannelName + "\"," + StatePayload + "}");

            await AssertStillProcessing(client);
            channel.Properties.ChannelSerial.Should().BeNull();
            channelStates.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTL15b")]
        [Trait("spec", "TR4r")]
        public async Task WhenObjectSyncMessageWithStateReceived_ItIsIgnoredWithNoChannelEffects()
        {
            var (client, channel, connectionStates, channelStates) = await GetConnectedClientAndAttachedChannel();

            // OBJECT_SYNC is not among the RTL15b actions, so the channelSerial must not be recorded.
            InjectRawJson("{\"action\":20,\"channel\":\"" + TestChannelName + "\",\"channelSerial\":\"sync-serial-1:\"," + StatePayload + "}");

            await AssertStillProcessing(client);
            channel.Properties.ChannelSerial.Should().BeNull();
            channel.State.Should().Be(ChannelState.Attached);
            channelStates.Should().BeEmpty();
            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionStates.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "TR3h")]
        [Trait("spec", "TR4r")]
        public async Task WhenAttachedWithHasObjectsFlagAndStateReceived_AttachCompletes()
        {
            var client = await GetConnectedClient();
            var channel = client.Channels.Get(TestChannelName);

            var attachTask = channel.AttachAsync();
            InjectRawJson(
                "{\"action\":11,\"channel\":\"" + TestChannelName + "\",\"channelSerial\":\"attach-serial\",\"flags\":" +
                (int)(ProtocolMessage.Flag.HasObjects | ProtocolMessage.Flag.Subscribe | ProtocolMessage.Flag.ObjectSubscribe) +
                "," + StatePayload + "}");

            var result = await attachTask;
            result.IsSuccess.Should().BeTrue();
            channel.State.Should().Be(ChannelState.Attached);
            channel.Properties.ChannelSerial.Should().Be("attach-serial");
            channel.Modes.Should().Contain(ChannelMode.ObjectSubscribe);
            channel.Modes.Should().NotContain(ChannelMode.ObjectPublish);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }
    }
}
