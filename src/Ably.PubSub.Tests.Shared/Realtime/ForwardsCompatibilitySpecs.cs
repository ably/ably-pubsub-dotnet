using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Infrastructure;
using Ably.PubSub.Types;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    /// <summary>
    /// Forwards-compatibility tolerance specs (RTF1/RSF1) plus the PING/PONG reply (RTN23c1/RTN23c2).
    /// Raw JSON frames are injected through the fake transport's listener so the real deserialization
    /// path is exercised, exactly as for a frame arriving from the wire.
    /// </summary>
    public class ForwardsCompatibilitySpecs : AblyRealtimeSpecs
    {
        public ForwardsCompatibilitySpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private void InjectRawJson(string json)
        {
            LastCreatedTransport.Listener.OnTransportDataReceived(new RealtimeTransportData(json));
        }

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel, ConcurrentQueue<ConnectionState> States)> GetConnectedClientAndAttachedChannel()
        {
            var client = await GetConnectedClient();
            var channel = client.Channels.Get(TestChannelName);
            ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);

            var observedConnectionStates = new ConcurrentQueue<ConnectionState>();
            client.Connection.On(change => observedConnectionStates.Enqueue(change.Current));

            return (client, channel, observedConnectionStates);
        }

        [Fact]
        [Trait("spec", "RTF1")]
        public async Task WhenProtocolMessageHasUnrecognisedAttributes_MessageIsStillDeliveredAndConnectionUnaffected()
        {
            var (client, channel, observedConnectionStates) = await GetConnectedClientAndAttachedChannel();

            Message received = null;
            var delivered = new TaskCompletionAwaiter();
            channel.Subscribe(message =>
            {
                received = message;
                delivered.SetCompleted();
            });

            // A MESSAGE ProtocolMessage carrying top-level attributes this library does not know about.
            InjectRawJson(
                "{\"action\":15,\"channel\":\"" + TestChannelName + "\"," +
                "\"unknownTopLevelString\":\"future\",\"unknownTopLevelObject\":{\"nested\":true},\"unknownTopLevelNumber\":42," +
                "\"messages\":[{\"name\":\"event\",\"data\":\"payload\"}]}");

            (await delivered.Task).Should().BeTrue("the message should be delivered despite unrecognised ProtocolMessage attributes");
            received.Name.Should().Be("event");
            received.Data.Should().Be("payload");

            client.Connection.State.Should().Be(ConnectionState.Connected);
            observedConnectionStates.Should().BeEmpty("unrecognised attributes must not affect the connection");
        }

        [Theory]
        [Trait("spec", "RTF1")]
        [InlineData(254)] // entirely unknown action
        [InlineData(19)] // OBJECT: defined but unhandled
        [InlineData(20)] // OBJECT_SYNC: defined but unhandled
        [InlineData(21)] // ANNOTATION: defined but unhandled
        public async Task WhenProtocolMessageHasUnknownOrUnhandledAction_ItIsIgnoredAndSubsequentMessagesAreProcessed(int action)
        {
            var (client, _, observedConnectionStates) = await GetConnectedClientAndAttachedChannel();

            // Capture the heartbeat this client sends for PingAsync, so the follow-up HEARTBEAT
            // reply can be correlated and its processing proven.
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

            // Inject the unknown/unhandled action both connection-scoped and channel-scoped.
            InjectRawJson("{\"action\":" + action + "}");
            InjectRawJson("{\"action\":" + action + ",\"channel\":\"" + TestChannelName + "\"}");

            var pingTask = client.Connection.PingAsync();
            (await heartbeatSent.Task).Should().BeTrue("the outgoing heartbeat should have been sent");

            // Follow-up HEARTBEAT must still be processed after the ignored actions.
            InjectRawJson("{\"action\":0,\"id\":\"" + heartbeatId + "\"}");

            var pingResult = await pingTask;
            pingResult.IsSuccess.Should().BeTrue("a HEARTBEAT following an ignored action must still be processed");

            client.Connection.State.Should().Be(ConnectionState.Connected);
            observedConnectionStates.Should().NotContain(ConnectionState.Disconnected);
            observedConnectionStates.Should().NotContain(ConnectionState.Failed);
        }

        [Fact]
        [Trait("spec", "RSF1")]
        public async Task WhenMessagesHaveUnrecognisedAttributes_AllAreDeliveredWithKnownFieldsParsed()
        {
            var (client, channel, observedConnectionStates) = await GetConnectedClientAndAttachedChannel();

            var received = new ConcurrentQueue<Message>();
            var delivered = new TaskCompletionAwaiter(taskCount: 2);
            channel.Subscribe(message =>
            {
                received.Enqueue(message);
                delivered.Tick();
            });

            // Two Messages carrying attributes and enum-like values this library does not know about.
            InjectRawJson(
                "{\"action\":15,\"channel\":\"" + TestChannelName + "\",\"messages\":[" +
                "{\"name\":\"first\",\"data\":\"payload1\",\"unknownField\":\"x\",\"futureAction\":99}," +
                "{\"name\":\"second\",\"data\":\"payload2\",\"futureObject\":{\"nested\":[1,2,3]}}]}");

            (await delivered.Task).Should().BeTrue("both messages should be delivered despite unrecognised attributes");

            received.Should().HaveCount(2);
            received.TryDequeue(out var first).Should().BeTrue();
            first.Name.Should().Be("first");
            first.Data.Should().Be("payload1");
            received.TryDequeue(out var second).Should().BeTrue();
            second.Name.Should().Be("second");
            second.Data.Should().Be("payload2");

            client.Connection.State.Should().Be(ConnectionState.Connected);
            observedConnectionStates.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTN23c1")]
        [Trait("spec", "RTN23c2")]
        public async Task WhenPingWithIdReceived_ShouldSendPongWithSameIdOnSameTransport()
        {
            var client = await GetConnectedClient();

            RealtimeTransportData pongData = null;
            var pongSent = new TaskCompletionAwaiter();
            LastCreatedTransport.SetSendAction(data =>
            {
                if (data.Original?.Action == ProtocolMessage.MessageAction.Pong)
                {
                    pongData = data;
                    pongSent.SetCompleted();
                }
            });

            InjectRawJson("{\"action\":22,\"id\":\"unique-ping-id\"}");

            (await pongSent.Task).Should().BeTrue("a PONG should be sent in reply to the PING");
            LastCreatedTransport.SentMessages.Should().Contain(pongData, "the PONG must go out on the same transport the PING arrived on");
            pongData.Original.Id.Should().Be("unique-ping-id", "the PONG must carry the PING's id");

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Fact]
        [Trait("spec", "RTN23c1")]
        [Trait("spec", "RTN23c2")]
        public async Task WhenPingWithoutIdReceived_ShouldSendPongWithoutId()
        {
            var client = await GetConnectedClient();

            RealtimeTransportData pongData = null;
            var pongSent = new TaskCompletionAwaiter();
            LastCreatedTransport.SetSendAction(data =>
            {
                if (data.Original?.Action == ProtocolMessage.MessageAction.Pong)
                {
                    pongData = data;
                    pongSent.SetCompleted();
                }
            });

            InjectRawJson("{\"action\":22}");

            (await pongSent.Task).Should().BeTrue("a PONG should be sent in reply to the PING");
            LastCreatedTransport.SentMessages.Should().Contain(pongData);
            pongData.Original.Id.Should().BeNull("a PONG replying to an id-less PING must not contain an id");
            pongData.Text.Should().NotContain("\"id\"", "the id field must be absent from the serialized PONG");

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }
    }
}
