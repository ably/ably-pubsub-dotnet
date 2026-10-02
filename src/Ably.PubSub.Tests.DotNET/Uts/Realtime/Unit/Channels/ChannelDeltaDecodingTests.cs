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
    /// Derived from uts/realtime/unit/channels/channel_delta_decoding.md in ably/specification.
    ///
    /// Spec points: RTL18, RTL18a, RTL18b, RTL18c, RTL19a, RTL19b, RTL19c, RTL20, RTL21
    ///
    /// <para>
    /// Delta decoding is a conversation with state on both sides: the server sends a patch against
    /// what it believes the client last received, so the client has to keep the right base payload
    /// and the right message id, and when either turns out to be wrong it has to stop guessing and
    /// re-attach from a known serial. RTL19 is the bookkeeping, RTL20 the check, RTL18 the
    /// recovery.
    /// </para>
    ///
    /// <para>
    /// Two of the file's twelve tests are not translated. <c>PC3/vcdiff-plugin-decodes-0</c> and
    /// <c>PC3/no-plugin-fails-1</c> both turn on vcdiff being a plugin supplied through client
    /// options; here it is <c>IO.Ably.DeltaCodec</c>, compiled in and not replaceable, so there is
    /// no "with plugin" and "without plugin" to compare. See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// A5: the specs install a mock encoder and a matching mock decoder, which this SDK has no seam
    /// for. The deltas here are real ones, built by <c>VcdiffDeltas</c> for the compiled-in decoder
    /// to apply, and the decode failures RTL18 needs are provoked with bytes that are not a valid
    /// delta rather than with a decoder rigged to throw. Both are closer to the real thing than the
    /// mock. See Uts/deviations.md.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelDeltaDecodingTests : UtsTestBase
    {
        /// <summary>
        /// A string payload arrives base64-encoded (JSON transport cannot carry the delta's bytes
        /// raw), is delta-decoded, and is then utf-8 decoded back into a string. The SDK applies
        /// encoding steps right to left, so this reads in the order they happen, backwards.
        /// </summary>
        private const string TextDeltaEncoding = "utf-8/vcdiff/base64";

        /// <summary>
        /// The same for a binary payload, which stops after the delta is applied.
        /// </summary>
        private const string BinaryDeltaEncoding = "vcdiff/base64";

        /// <summary>RTL18a's error code for a delta that could not be decoded.</summary>
        private const int DeltaDecodeFailureCode = 40018;

        public ChannelDeltaDecodingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL21/ascending-index-order-0
        [Fact]
        public async Task RTL21_MessagesInAnArrayAreDecodedInAscendingIndexOrder()
        {
            const string ChannelName = "test-RTL21-order";
            const string First = "first message";
            const string Second = "second message";
            const string Third = "third message";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            // Each delta is computed against the one before it, so the three only decode if they
            // are processed in array order.
            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray
                {
                    PlainMessage("serial:0", First),
                    DeltaMessage("serial:1", "serial:0", VcdiffDeltas.EncodeBase64(First, Second)),
                    DeltaMessage("serial:2", "serial:1", VcdiffDeltas.EncodeBase64(Second, Third)),
                },
                new Dictionary<string, JToken> { ["id"] = "serial:0" }));

            await UtsClients.PollUntil(() => Count(received) == 3, "all three messages");

            var messages = UtsClients.Snapshot(received);
            messages[0].Data.Should().Be(First);
            messages[1].Data.Should().Be(Second);
            messages[2].Data.Should().Be(Third);
        }

        // UTS: realtime/unit/RTL19b/stores-base-payload-0
        [Fact]
        public async Task RTL19b_NonDeltaMessageStoresItsPayloadAsTheBase()
        {
            const string ChannelName = "test-RTL19b-base";
            const string Base = "base payload";
            const string Updated = "updated payload";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            SendOne(mockWs, ChannelName, "msg-1:0", PlainMessage("msg-1:0", Base));
            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-1:0", VcdiffDeltas.EncodeBase64(Base, Updated)));

            await UtsClients.PollUntil(() => Count(received) == 2, "the delta message");

            var messages = UtsClients.Snapshot(received);
            messages[0].Data.Should().Be(Base);
            messages[1].Data.Should().Be(
                Updated,
                "RTL19b - the non-delta message's payload was kept as the base");
        }

        // UTS: realtime/unit/RTL19b/json-wire-form-base-1
        [Fact]
        public async Task RTL19b_JsonMessageStoresTheWireFormAsTheBase()
        {
            const string ChannelName = "test-RTL19b-json-base";
            const string JsonString = "{\"foo\":\"bar\",\"count\":1}";
            const string NewJsonString = "{\"foo\":\"baz\",\"count\":2}";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            var baseMessage = new JObject
            {
                ["id"] = "msg-1:0",
                ["data"] = JsonString,
                ["encoding"] = "json",
            };

            SendOne(mockWs, ChannelName, "msg-1:0", baseMessage);
            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            // The server computes the delta against the wire form, so storing the parsed object
            // instead of the JSON string would leave nothing a delta could be applied to.
            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage(
                    "msg-2:0",
                    "msg-1:0",
                    VcdiffDeltas.EncodeBase64(JsonString, NewJsonString)));

            await UtsClients.PollUntil(() => Count(received) == 2, "the delta message");

            var messages = UtsClients.Snapshot(received);

            messages[0].Data.Should().BeOfType<JObject>("the subscriber gets the parsed object");
            ((JObject)messages[0].Data)["foo"].Value<string>().Should().Be("bar");

            // The delta message's own encoding has no json step, so its result is the string.
            messages[1].Data.Should().Be(
                NewJsonString,
                "RTL19b - the base was the JSON string, not the object parsed from it");
        }

        // UTS: realtime/unit/RTL19a/base64-decoded-before-store-0
        [Fact]
        public async Task RTL19a_Base64IsDecodedBeforeTheBaseIsStored()
        {
            const string ChannelName = "test-RTL19a-base64";

            // The spec's "Hello"/"World" share no run long enough to copy, which would leave a
            // delta that ignored its source and so could not tell a right base from a wrong one.
            // These differ in the same way and still exercise the base.
            var baseBinary = System.Text.Encoding.UTF8.GetBytes("Hello world");
            var newBinary = System.Text.Encoding.UTF8.GetBytes("Hello there, world");

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            var baseMessage = new JObject
            {
                ["id"] = "msg-1:0",
                ["data"] = Convert.ToBase64String(baseBinary),
                ["encoding"] = "base64",
            };

            SendOne(mockWs, ChannelName, "msg-1:0", baseMessage);
            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            var delta = DeltaMessage(
                "msg-2:0",
                "msg-1:0",
                VcdiffDeltas.EncodeBase64(baseBinary, newBinary));
            delta["encoding"] = BinaryDeltaEncoding;

            SendOne(mockWs, ChannelName, "msg-2:0", delta);
            await UtsClients.PollUntil(() => Count(received) == 2, "the delta message");

            var messages = UtsClients.Snapshot(received);
            messages[0].Data.Should().BeEquivalentTo(baseBinary);
            messages[1].Data.Should().BeEquivalentTo(
                newBinary,
                "RTL19a - the base was stored decoded, so the delta had bytes to patch");
        }

        // UTS: realtime/unit/RTL19c/delta-result-becomes-base-0
        [Fact]
        public async Task RTL19c_DeltaResultBecomesTheNewBase()
        {
            const string ChannelName = "test-RTL19c-chain";
            const string ValueA = "value-A";
            const string ValueB = "value-B";
            const string ValueC = "value-C";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            SendOne(mockWs, ChannelName, "msg-1:0", PlainMessage("msg-1:0", ValueA));
            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-1:0", VcdiffDeltas.EncodeBase64(ValueA, ValueB)));

            await UtsClients.PollUntil(() => Count(received) == 2, "the first delta");

            // Computed against value-B, which only exists as the result of the previous delta.
            SendOne(
                mockWs,
                ChannelName,
                "msg-3:0",
                DeltaMessage("msg-3:0", "msg-2:0", VcdiffDeltas.EncodeBase64(ValueB, ValueC)));

            await UtsClients.PollUntil(() => Count(received) == 3, "the chained delta");

            var messages = UtsClients.Snapshot(received);
            messages[0].Data.Should().Be(ValueA);
            messages[1].Data.Should().Be(ValueB);
            messages[2].Data.Should().Be(
                ValueC,
                "RTL19c - decoding a delta replaces the base with its result");
        }

        // UTS: realtime/unit/RTL20/mismatched-id-triggers-recovery-0
        [Fact]
        public async Task RTL20_DeltaWithAMismatchedBaseIdTriggersRecovery()
        {
            const string ChannelName = "test-RTL20-mismatch";
            const string Base = "base payload";

            var attachMessages = new List<ProtocolMessage>();
            var (mockWs, received, channel) = await SubscribedChannelRecordingAttaches(
                ChannelName, attachMessages);

            SendOne(
                mockWs,
                ChannelName,
                "msg-1:0",
                PlainMessage("msg-1:0", Base),
                channelSerial: "serial-1");

            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            var changes = RecordChanges(channel);
            var attachCountBefore = AttachCount(attachMessages);
            var attaching = UtsClients.NextChannelState(channel, ChannelState.Attaching);

            // The stored last message id is msg-1:0, so this delta's reference is wrong and the
            // SDK has no way to know what it is a patch against.
            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-999:0", VcdiffDeltas.EncodeBase64(Base, "new payload")));

            await attaching;

            // ATTACHING is set before the frame is handed to the transport, so the count has to be
            // waited for rather than read.
            await UtsClients.PollUntil(
                () => AttachCount(attachMessages) > attachCountBefore,
                "RTL18c - the recovery ATTACH");

            LastAttach(attachMessages).ChannelSerial.Should().Be(
                "serial-1",
                "RTL18c - the recovery ATTACH resumes from the last good serial");

            var attachingChange = UtsClients.Snapshot(changes)
                .Find(c => c.Current == ChannelState.Attaching);

            attachingChange.Should().NotBeNull();
            attachingChange.Error.Should().NotBeNull();
            attachingChange.Error.Code.Should().Be(DeltaDecodeFailureCode);
        }

        // UTS: realtime/unit/RTL20/last-id-updated-on-decode-1
        [Fact]
        public async Task RTL20_LastMessageIdIsTheLastInTheArray()
        {
            const string ChannelName = "test-RTL20-id-update";
            const string Second = "second message";
            const string Third = "third message";

            var (mockWs, received) = await SubscribedChannel(ChannelName);

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray
                {
                    PlainMessage("serial:0", "first message"),
                    PlainMessage("serial:1", Second),
                },
                new Dictionary<string, JToken> { ["id"] = "serial:0" }));

            await UtsClients.PollUntil(() => Count(received) == 2, "both plain messages");

            // Referencing serial:1 - the last of the two, not the first - so this only decodes if
            // the stored id was taken from the end of the array.
            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "serial:1", VcdiffDeltas.EncodeBase64(Second, Third)));

            await UtsClients.PollUntil(() => Count(received) == 3, "the delta message");

            var messages = UtsClients.Snapshot(received);
            messages[0].Data.Should().Be("first message");
            messages[1].Data.Should().Be(Second);
            messages[2].Data.Should().Be(Third);
        }

        // UTS: realtime/unit/RTL18/decode-failure-recovery-0
        [Fact]
        public async Task RTL18_DecodeFailureIsDiscardedAndTriggersRecovery()
        {
            const string ChannelName = "test-RTL18-recovery";
            const string Base = "base payload";

            var attachMessages = new List<ProtocolMessage>();
            var (mockWs, received, channel) = await SubscribedChannelRecordingAttaches(
                ChannelName, attachMessages);

            SendOne(
                mockWs,
                ChannelName,
                "msg-1:0",
                PlainMessage("msg-1:0", Base),
                channelSerial: "serial-100");

            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            var changes = RecordChanges(channel);
            var attachCountBefore = AttachCount(attachMessages);
            var attaching = UtsClients.NextChannelState(channel, ChannelState.Attaching);

            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-1:0", NotADelta()),
                channelSerial: "serial-200");

            await attaching;

            Count(received).Should().Be(1, "RTL18b - the message that would not decode is discarded");
            UtsClients.Snapshot(received)[0].Data.Should().Be(Base);

            await UtsClients.PollUntil(
                () => AttachCount(attachMessages) > attachCountBefore,
                "RTL18c - the recovery ATTACH");

            LastAttach(attachMessages).ChannelSerial.Should().Be(
                "serial-100",
                "RTL18c - the serial of the last message that did decode");

            var attachingChange = UtsClients.Snapshot(changes)
                .Find(c => c.Current == ChannelState.Attaching);

            attachingChange.Should().NotBeNull();
            attachingChange.Error.Should().NotBeNull();
            attachingChange.Error.Code.Should().Be(DeltaDecodeFailureCode, "RTL18a");
        }

        // UTS: realtime/unit/RTL18c/recovery-completes-on-attached-0
        [Fact]
        public async Task RTL18c_RecoveryCompletesWhenTheServerSendsAttached()
        {
            const string ChannelName = "test-RTL18c-complete";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var received = new List<Message>();
            var channel = client.Channels.Get(ChannelName);
            channel.Subscribe(message => Append(received, message));
            await channel.AttachAsync();

            SendOne(
                mockWs,
                ChannelName,
                "msg-1:0",
                PlainMessage("msg-1:0", "original base"),
                channelSerial: "serial-1");

            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            var attaching = UtsClients.NextChannelState(channel, ChannelState.Attaching);

            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-1:0", NotADelta()),
                channelSerial: "serial-2");

            await attaching;
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            channel.State.Should().Be(
                ChannelState.Attached,
                "RTL18c - the ATTACHED confirms the recovery");

            // The server would resend from the serial; what matters is that delivery resumed.
            SendOne(
                mockWs,
                ChannelName,
                "msg-3:0",
                PlainMessage("msg-3:0", "fresh after recovery"),
                channelSerial: "serial-3");

            await UtsClients.PollUntil(() => Count(received) == 2, "the message after recovery");

            UtsClients.Snapshot(received)[1].Data.Should().Be("fresh after recovery");
        }

        // UTS: realtime/unit/RTL18/single-recovery-at-time-1
        [Fact]
        public async Task RTL18_OnlyOneRecoveryRunsAtATime()
        {
            const string ChannelName = "test-RTL18-single-recovery";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            var attachMessages = new List<ProtocolMessage>();
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                lock (attachMessages)
                {
                    attachMessages.Add(msg);
                }

                // Only the first attach is answered. The recovery ATTACH is left outstanding, so
                // the second failure arrives with a recovery still in flight.
                if (AttachCount(attachMessages) == 1)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var received = new List<Message>();
            var channel = client.Channels.Get(ChannelName);
            channel.Subscribe(message => Append(received, message));
            await channel.AttachAsync();

            var attachesBefore = AttachCount(attachMessages);

            SendOne(
                mockWs,
                ChannelName,
                "msg-1:0",
                PlainMessage("msg-1:0", "base"),
                channelSerial: "serial-1");

            await UtsClients.PollUntil(() => Count(received) == 1, "the base message");

            var attaching = UtsClients.NextChannelState(channel, ChannelState.Attaching);

            SendOne(
                mockWs,
                ChannelName,
                "msg-2:0",
                DeltaMessage("msg-2:0", "msg-1:0", NotADelta()));

            await attaching;

            SendOne(
                mockWs,
                ChannelName,
                "msg-3:0",
                DeltaMessage("msg-3:0", "msg-2:0", NotADelta()));

            // The second failure must not add an ATTACH, so there is nothing to wait for.
            await channel.Presence.GetAsync(waitForSync: false);

            (AttachCount(attachMessages) - attachesBefore).Should().Be(
                1,
                "RTL18 - a recovery already in progress absorbs the second failure");
        }

        /// <summary>
        /// Bytes that are not a VCDIFF stream, so the compiled-in decoder throws. This is the
        /// specs' <c>FailingMockVCDiffDecoder</c> arrived at from the other end.
        /// </summary>
        private static string NotADelta()
            => Convert.ToBase64String(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });

        private static JObject PlainMessage(string id, string data)
            => new JObject { ["id"] = id, ["data"] = data };

        private static JObject DeltaMessage(string id, string from, string base64Delta)
            => new JObject
            {
                ["id"] = id,
                ["data"] = base64Delta,
                ["encoding"] = TextDeltaEncoding,
                ["extras"] = new JObject
                {
                    ["delta"] = new JObject { ["from"] = from, ["format"] = "vcdiff" },
                },
            };

        private static void SendOne(
            MockWebSocket mockWs,
            string channelName,
            string protocolMessageId,
            JObject message,
            string channelSerial = null)
        {
            var fields = new Dictionary<string, JToken> { ["id"] = protocolMessageId };
            if (channelSerial != null)
            {
                fields["channelSerial"] = channelSerial;
            }

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName, new JArray { message }, fields));
        }

        private static void Append(List<Message> received, Message message)
        {
            lock (received)
            {
                received.Add(message);
            }
        }

        private static int Count(List<Message> received)
        {
            lock (received)
            {
                return received.Count;
            }
        }

        private static ProtocolMessage LastAttach(List<ProtocolMessage> attachMessages)
        {
            lock (attachMessages)
            {
                return attachMessages[attachMessages.Count - 1];
            }
        }

        private static int AttachCount(List<ProtocolMessage> attachMessages)
        {
            lock (attachMessages)
            {
                return attachMessages.Count;
            }
        }

        private static List<ChannelStateChange> RecordChanges(IRealtimeChannel channel)
        {
            var changes = new List<ChannelStateChange>();
            channel.On(change =>
            {
                lock (changes)
                {
                    changes.Add(change);
                }
            });

            return changes;
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

        private async Task<(MockWebSocket MockWs, List<Message> Received)> SubscribedChannel(
            string channelName)
        {
            var attaches = new List<ProtocolMessage>();
            var (mockWs, received, _) = await SubscribedChannelRecordingAttaches(
                channelName, attaches);

            return (mockWs, received);
        }

        private async Task<(MockWebSocket MockWs, List<Message> Received, IRealtimeChannel Channel)>
            SubscribedChannelRecordingAttaches(
                string channelName,
                List<ProtocolMessage> attachMessages)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                lock (attachMessages)
                {
                    attachMessages.Add(msg);
                }

                mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var received = new List<Message>();
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(message => Append(received, message));
            await channel.AttachAsync();

            return (mockWs, received, channel);
        }
    }
}
