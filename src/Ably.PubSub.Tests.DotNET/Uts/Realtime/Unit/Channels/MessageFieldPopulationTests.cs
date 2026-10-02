using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/message_field_population.md in ably/specification.
    ///
    /// Spec points: TM2a, TM2c, TM2f
    ///
    /// <para>
    /// The server leaves three fields off each message in a MESSAGE and expects the client to fill
    /// them in from the envelope: the id from the ProtocolMessage's id and the message's index, and
    /// the connectionId and timestamp from the ProtocolMessage's own. Each is filled in only when
    /// absent, so a message that carries its own keeps it. The id matters beyond metadata - RTL20's
    /// delta continuity check reads it.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class MessageFieldPopulationTests : UtsTestBase
    {
        /// <summary>
        /// The spec's timestamp, in milliseconds since the epoch, as it appears on the wire.
        /// </summary>
        private const long ProtocolTimestampMs = 1700000000000L;

        /// <summary>
        /// The spec's second timestamp, used where a message carries one of its own.
        /// </summary>
        private const long MessageTimestampMs = 1600000000000L;

        public MessageFieldPopulationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/TM2a/id-from-protocol-message-0
        [Fact]
        public async Task TM2a_IdIsPopulatedFromProtocolMessageIdAndIndex()
        {
            const string ChannelName = "test-TM2a-id";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray
                {
                    PlainMessage("first", "a"),
                    PlainMessage("second", "b"),
                    PlainMessage("third", "c"),
                },
                new Dictionary<string, JToken>
                {
                    ["id"] = "abc123:5",
                    ["connectionId"] = "abc123",
                    ["timestamp"] = ProtocolTimestampMs,
                }));

            await UtsClients.PollUntil(() => Count(received) == 3, "three messages");

            var messages = UtsClients.Snapshot(received);
            messages[0].Id.Should().Be("abc123:5:0");
            messages[1].Id.Should().Be("abc123:5:1");
            messages[2].Id.Should().Be("abc123:5:2");
        }

        // UTS: realtime/unit/TM2a/existing-id-not-overwritten-1
        [Fact]
        public async Task TM2a_ExistingIdIsNotOverwritten()
        {
            const string ChannelName = "test-TM2a-existing-id";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            var message = PlainMessage("msg", "hello");
            message["id"] = "my-custom-id";

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { message },
                new Dictionary<string, JToken> { ["id"] = "proto-id:0" }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Id.Should().Be("my-custom-id");
        }

        // UTS: realtime/unit/TM2a/no-id-without-protocol-id-2
        //
        // DEVIATION, D44. With no id on the ProtocolMessage there is nothing to derive one from, so
        // TM2a leaves the message's id unset. This SDK formats the absent id into the string
        // anyway and hands subscribers ":0". See Uts/deviations.md.
        [DeviationFact]
        public async Task TM2a_NoIdWhenProtocolMessageHasNoId()
        {
            const string ChannelName = "test-TM2a-no-id";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { PlainMessage("msg", "hello") },
                new Dictionary<string, JToken> { ["connectionId"] = "abc123" }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Id.Should().BeNull(
                "TM2a - there was no ProtocolMessage id to derive one from");
        }

        // UTS: realtime/unit/TM2c/connectionid-from-protocol-0
        [Fact]
        public async Task TM2c_ConnectionIdIsPopulatedFromProtocolMessage()
        {
            const string ChannelName = "test-TM2c-connid";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { PlainMessage("msg", "hello") },
                new Dictionary<string, JToken>
                {
                    ["id"] = "msg:0",
                    ["connectionId"] = "server-conn-xyz",
                }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].ConnectionId.Should().Be("server-conn-xyz");
        }

        // UTS: realtime/unit/TM2c/existing-connectionid-kept-1
        [Fact]
        public async Task TM2c_ExistingConnectionIdIsNotOverwritten()
        {
            const string ChannelName = "test-TM2c-existing-connid";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            var message = PlainMessage("msg", "hello");
            message["connectionId"] = "msg-conn";

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { message },
                new Dictionary<string, JToken>
                {
                    ["id"] = "msg:0",
                    ["connectionId"] = "proto-conn",
                }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].ConnectionId.Should().Be("msg-conn");
        }

        // UTS: realtime/unit/TM2f/timestamp-from-protocol-0
        [Fact]
        public async Task TM2f_TimestampIsPopulatedFromProtocolMessage()
        {
            const string ChannelName = "test-TM2f-timestamp";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { PlainMessage("msg", "hello") },
                new Dictionary<string, JToken>
                {
                    ["id"] = "msg:0",
                    ["timestamp"] = ProtocolTimestampMs,
                }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Timestamp.Should().Be(FromUnixMs(ProtocolTimestampMs));
        }

        // UTS: realtime/unit/TM2f/existing-timestamp-kept-1
        //
        // DEVIATION, D45. TM2f sets the timestamp only when the message has none. This SDK
        // overwrites every message's timestamp with the envelope's the moment the frame is parsed,
        // before the guarded assignment it also has ever runs - so a message's own timestamp is
        // always lost, and is wiped to null when the envelope carries none. See Uts/deviations.md.
        [DeviationFact]
        public async Task TM2f_ExistingTimestampIsNotOverwritten()
        {
            const string ChannelName = "test-TM2f-existing-timestamp";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            var message = PlainMessage("msg", "hello");
            message["timestamp"] = MessageTimestampMs;

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { message },
                new Dictionary<string, JToken>
                {
                    ["id"] = "msg:0",
                    ["timestamp"] = ProtocolTimestampMs,
                }));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Timestamp.Should().Be(FromUnixMs(MessageTimestampMs));

            // The same sentence, with the envelope carrying no timestamp at all. There is nothing
            // to inherit here, so the message's own is all there is.
            var second = PlainMessage("msg2", "hello again");
            second["timestamp"] = MessageTimestampMs;

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { second },
                new Dictionary<string, JToken> { ["id"] = "msg:1" }));

            await UtsClients.PollUntil(() => Count(received) == 2, "the second message");

            UtsClients.Snapshot(received)[1].Timestamp.Should().Be(
                FromUnixMs(MessageTimestampMs),
                "TM2f - an absent envelope timestamp is not a reason to discard the message's");
        }

        // UTS: realtime/unit/TM2a/all-fields-populated-together-3
        [Fact]
        public async Task TM2_AllFieldsPopulatedTogether()
        {
            const string ChannelName = "test-TM2-all-fields";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { PlainMessage("first", "a"), PlainMessage("second", "b") },
                new Dictionary<string, JToken>
                {
                    ["id"] = "connId:7",
                    ["connectionId"] = "connId",
                    ["timestamp"] = ProtocolTimestampMs,
                }));

            await UtsClients.PollUntil(() => Count(received) == 2, "both messages");

            var messages = UtsClients.Snapshot(received);

            messages[0].Id.Should().Be("connId:7:0");
            messages[0].ConnectionId.Should().Be("connId");
            messages[0].Timestamp.Should().Be(FromUnixMs(ProtocolTimestampMs));
            messages[0].Name.Should().Be("first");
            messages[0].Data.Should().Be("a");

            // Same envelope, so the same connectionId and timestamp - only the index moves on.
            messages[1].Id.Should().Be("connId:7:1");
            messages[1].ConnectionId.Should().Be("connId");
            messages[1].Timestamp.Should().Be(FromUnixMs(ProtocolTimestampMs));
            messages[1].Name.Should().Be("second");
            messages[1].Data.Should().Be("b");
        }

        private static JObject PlainMessage(string name, string data)
            => new JObject { ["name"] = name, ["data"] = data };

        private static DateTimeOffset FromUnixMs(long milliseconds)
            => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

        private static int Count(List<Message> received)
        {
            lock (received)
            {
                return received.Count;
            }
        }

        private async Task<(MockWebSocket MockWs, List<Message> Received)> SubscribedChannel(
            string channelName)
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
            };

            var received = new List<Message>();
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(message =>
            {
                lock (received)
                {
                    received.Add(message);
                }
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            await channel.AttachAsync();

            return (mockWs, received);
        }
    }
}
