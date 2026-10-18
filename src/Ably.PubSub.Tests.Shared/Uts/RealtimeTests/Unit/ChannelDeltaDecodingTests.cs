// Channel Delta Decoding Tests (RTL18, RTL18a, RTL18b, RTL18c, RTL19, RTL19a, RTL19b, RTL19c, RTL20, RTL21, PC3, PC3a)
// Derived from uts/realtime/unit/channels/channel_delta_decoding.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: plugins: { vcdiff: decoder } is installed with InstallMock(decoder) (hook H2); delta payloads are sent as base64 with the /base64 encoding suffix, as the spec's JSON transport note says; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelDeltaDecodingTests : UtsUnitTestBase
    {
        public ChannelDeltaDecodingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static string RandomId() => Guid.NewGuid().ToString("N");

        // A non-delta message of a MESSAGE protocol message
        private static JObject PlainMessage(string id, string data, string encoding = null)
        {
            var message = new JObject { ["id"] = id, ["data"] = data };
            if (encoding != null)
            {
                message["encoding"] = encoding;
            }

            return message;
        }

        // A delta message: the spec's data/encoding/extras.delta, with the payload already in its JSON wire form
        private static JObject DeltaMessage(string id, string data, string encoding, string from)
        {
            return new JObject
            {
                ["id"] = id,
                ["data"] = data,
                ["encoding"] = encoding,
                ["extras"] = new JObject { ["delta"] = new JObject { ["from"] = from, ["format"] = "vcdiff" } },
            };
        }

        // The spec's MESSAGE ProtocolMessage(channel:, id:, channelSerial:, messages:)
        private static JObject MessageFrame(string channelName, string id, JArray messages, string channelSerial = null)
        {
            var fields = new Dictionary<string, JToken> { ["id"] = id };
            if (channelSerial != null)
            {
                fields["channelSerial"] = channelSerial;
            }

            return ProtocolMessages.MessageProtocolMessage(channelName, messages, fields);
        }

        // The spec's JSON transport note: a delta travels as base64 of its bytes, with /base64 appended to the encoding
        private static string Base64Delta(string delta) => Convert.ToBase64String(Encoding.UTF8.GetBytes(delta));

        // A mock whose onMessageFromClient answers every ATTACH with ATTACHED for the channel (optionally observing it first)
        private static MockWebSocket AttachingMock(string channelName, Action<ProtocolMessage> onAttach = null)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    onAttach?.Invoke(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
            };
            return mockWs;
        }

        private async Task ConnectAndAttach(PubSubRealtimeClient client, IRealtimeChannel channel)
        {
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
        }

        // UTS: realtime/unit/RTL21/ascending-index-order-0
        [UtsFact]
        [Trait("spec", "RTL21")]
        public async Task RTL21_AscendingIndexOrder()
        {
            // Setup
            var channelName = "test-RTL21-order-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send a ProtocolMessage with 3 messages:
            // - msg-1: non-delta (establishes base)
            // - msg-2: delta referencing msg-1
            // - msg-3: delta referencing msg-2
            // This only works if messages are decoded in order [0], [1], [2]
            var baseData = "first message";
            var secondData = "second message";
            var thirdData = "third message";

            var delta1To2 = encoder.Encode(baseData, secondData);
            var delta2To3 = encoder.Encode(secondData, thirdData);

            mockWs.SendToClient(MessageFrame(
                channelName,
                "serial:0",
                new JArray
                {
                    PlainMessage("serial:0", baseData),
                    DeltaMessage("serial:1", Base64Delta(delta1To2), "utf-8/vcdiff/base64", "serial:0"),
                    DeltaMessage("serial:2", Base64Delta(delta2To3), "utf-8/vcdiff/base64", "serial:1"),
                }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 3, description: "3 messages received");

            // Assertions
            receivedMessages[0].Data.Should().Be("first message");
            // NOTE: per the spec's transport encoding note the delta is sent as "utf-8/vcdiff/base64", so the decoded data is the string the spec asserts
            receivedMessages[1].Data.Should().Be("second message");
            receivedMessages[2].Data.Should().Be("third message");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL19b/stores-base-payload-0
        [UtsFact]
        [Trait("spec", "RTL19b")]
        public async Task RTL19b_StoresBasePayload()
        {
            // Setup
            var channelName = "test-RTL19b-base-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send non-delta message to establish base
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "base payload") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Send delta referencing the base
            var delta = encoder.Encode("base payload", "updated payload");

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(delta), "utf-8/vcdiff/base64", "msg-1:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Assertions
            receivedMessages[0].Data.Should().Be("base payload");
            // NOTE: per the spec's transport encoding note the delta is sent as "utf-8/vcdiff/base64", so the decoded data is the string the spec asserts
            receivedMessages[1].Data.Should().Be("updated payload");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL19b/json-wire-form-base-1
        [UtsFact]
        [Trait("spec", "RTL19b")]
        public async Task RTL19b_JsonWireFormBase()
        {
            // Setup
            var channelName = "test-RTL19b-json-base-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send a non-delta message with JSON encoding.
            // The wire data is a JSON string; after decoding, the subscriber sees a Map.
            // The base payload stored for delta decoding should be the JSON string,
            // not the parsed Map.
            var jsonString = "{\"foo\":\"bar\",\"count\":1}";

            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", jsonString, "json") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Send a delta referencing the JSON string base.
            // The delta is computed against the JSON string, not the parsed object.
            var newJsonString = "{\"foo\":\"baz\",\"count\":2}";
            var delta = encoder.Encode(jsonString, newJsonString);

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(delta), "utf-8/vcdiff/base64", "msg-1:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Assertions
            // First message: subscriber receives the parsed JSON object
            JToken.DeepEquals(JToken.FromObject(receivedMessages[0].Data), JObject.Parse("{\"foo\":\"bar\",\"count\":1}")).Should().BeTrue();

            // Second message: delta decoded against JSON string base, then utf-8 decoded
            // to produce the new JSON string, which is delivered as-is (no json encoding
            // step in the delta message's encoding)
            receivedMessages[1].Data.Should().Be(newJsonString);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL19a/base64-decoded-before-store-0
        [UtsFact]
        [Trait("spec", "RTL19a")]
        public async Task RTL19a_Base64DecodedBeforeStore()
        {
            // Setup
            var channelName = "test-RTL19a-base64-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // The base payload is binary data [0x48, 0x65, 0x6C, 0x6C, 0x6F] ("Hello")
            // Sent as base64-encoded string
            var baseBinary = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
            var baseAsBase64 = "SGVsbG8=";

            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", baseAsBase64, "base64") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Now send a delta that references the binary base payload
            var newBinary = new byte[] { 0x57, 0x6F, 0x72, 0x6C, 0x64 };
            var delta = encoder.Encode(baseBinary, newBinary);

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Convert.ToBase64String(delta), "vcdiff/base64", "msg-1:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Assertions
            // First message decoded from base64 to binary
            ((byte[])receivedMessages[0].Data).Should().Equal(baseBinary);

            // Second message delta-decoded using the binary base, then delivered as binary
            ((byte[])receivedMessages[1].Data).Should().Equal(newBinary);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL19c/delta-result-becomes-base-0
        [UtsFact]
        [Trait("spec", "RTL19c")]
        public async Task RTL19c_DeltaResultBecomesBase()
        {
            // Setup
            var channelName = "test-RTL19c-chain-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message 1: non-delta, establishes base
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "value-A") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Message 2: delta from msg-1 to value-B
            var deltaAToB = encoder.Encode("value-A", "value-B");

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(deltaAToB), "utf-8/vcdiff/base64", "msg-1:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Message 3: delta from msg-2 to value-C
            // This verifies the base was updated to value-B after decoding msg-2
            var deltaBToC = encoder.Encode("value-B", "value-C");

            mockWs.SendToClient(MessageFrame(channelName, "msg-3:0", new JArray { DeltaMessage("msg-3:0", Base64Delta(deltaBToC), "utf-8/vcdiff/base64", "msg-2:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 3, description: "3 messages received");

            // Assertions
            receivedMessages[0].Data.Should().Be("value-A");
            // NOTE: per the spec's transport encoding note the delta is sent as "utf-8/vcdiff/base64", so the decoded data is the string the spec asserts
            receivedMessages[1].Data.Should().Be("value-B");
            receivedMessages[2].Data.Should().Be("value-C");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL20/mismatched-id-triggers-recovery-0
        [UtsFact]
        [Trait("spec", "RTL20")]
        public async Task RTL20_MismatchedIdTriggersRecovery()
        {
            // Setup
            var channelName = "test-RTL20-mismatch-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var attachMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(channelName, msg => attachMessages.Add(msg));
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            var stateChanges = UtsRecord.ChannelStateChanges(channel);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Establish base with msg-1
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "base payload") }, "serial-1"));

            // Wait for message to be processed
            await ProcessPendingEvents();

            // Clear state tracking from initial attach
            stateChanges.Clear();
            var initialAttachCount = attachMessages.Count;

            // Send delta that references wrong message ID (msg-999 instead of msg-1)
            var delta = encoder.Encode("base payload", "new payload");

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(delta), "vcdiff/base64", "msg-999:0") }));

            // RTL18c: channel transitions to ATTACHING and sends ATTACH
            // NOTE: the mock answers the recovery ATTACH at once, so ATTACHING is transient: the recorded changes are polled instead of AWAIT_STATE
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(c => c.Current == ChannelState.Attaching),
                description: "channel entered ATTACHING for recovery");

            // Assertions
            // RTL18c: A new ATTACH message was sent for recovery
            attachMessages.Count.Should().BeGreaterThan(initialAttachCount);

            // RTL18c: The ATTACH message includes channelSerial from previous message
            var recoveryAttach = attachMessages[attachMessages.Count - 1];
            recoveryAttach.ChannelSerial.Should().Be("serial-1");

            // RTL18c: Channel state went to ATTACHING with error code 40018
            UtsAssert.ContainsInOrder(stateChanges.Snapshot().Select(c => c.Current).ToList(), new[] { ChannelState.Attaching });
            var attachingChange = stateChanges.Snapshot().First(c => c.Current == ChannelState.Attaching);
            attachingChange.Error.Code.Should().Be(40018);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL20/last-id-updated-on-decode-1
        [UtsFact]
        [Trait("spec", "RTL20")]
        public async Task RTL20_LastIdUpdatedOnDecode()
        {
            // Setup
            var channelName = "test-RTL20-id-update-" + RandomId();
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(decoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send ProtocolMessage with 2 messages in the array
            // The last message ID should be stored as "serial:1" (the last in the array)
            mockWs.SendToClient(MessageFrame(
                channelName,
                "serial:0",
                new JArray
                {
                    PlainMessage("serial:0", "first"),
                    PlainMessage("serial:1", "second"),
                }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Now send a delta that references "serial:1" (the last message ID)
            // This should succeed because the stored ID matches
            var delta = encoder.Encode("second", "third");

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(delta), "utf-8/vcdiff/base64", "serial:1") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 3, description: "3 messages received");

            // Assertions
            // The delta was decoded successfully, confirming the stored ID was "serial:1"
            receivedMessages[0].Data.Should().Be("first");
            receivedMessages[1].Data.Should().Be("second");
            // NOTE: per the spec's transport encoding note the delta is sent as "utf-8/vcdiff/base64", so the decoded data is the string the spec asserts
            receivedMessages[2].Data.Should().Be("third");
            await CloseClient(client);
        }

        // UTS: realtime/unit/PC3/vcdiff-plugin-decodes-0
        [UtsFact]
        [Trait("spec", "PC3")]
        public async Task PC3_VcdiffPluginDecodes()
        {
            // Setup
            var channelName = "test-PC3-decode-" + RandomId();
            var encoder = new MockVCDiffEncoder();

            // Use a wrapping decoder that records the arguments it receives
            var decodeCalls = new CaptureList<Tuple<byte[], byte[]>>();

            var recordingDecoder = new MockVCDiffDecoder(onDecode: (delta, @base) => decodeCalls.Add(Tuple.Create(delta, @base)));

            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName);
            InstallMock(mockWs);
            InstallMock(recordingDecoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send a string non-delta message (establishes string base payload)
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "hello world") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Send a delta message referencing the string base
            var delta = encoder.Encode("hello world", "goodbye world");

            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta(delta), "utf-8/vcdiff/base64", "msg-1:0") }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Assertions
            // PC3: The decoder was called to decode the delta
            decodeCalls.Should().HaveCount(1);

            // PC3a: The base argument was UTF-8 encoded to binary
            // "hello world" as UTF-8 bytes
            decodeCalls[0].Item2.Should().Equal(Encoding.UTF8.GetBytes("hello world"));

            // PC3a: The delta argument is the raw delta payload
            // NOTE: the delta travels as base64 on the JSON transport; the decoder receives the decoded bytes, i.e. the UTF-8 bytes of the delta string
            decodeCalls[0].Item1.Should().Equal(Encoding.UTF8.GetBytes(delta));

            // The decoded message was delivered to the subscriber
            // NOTE: per the spec's transport encoding note the delta is sent as "utf-8/vcdiff/base64", so the decoded data is the string the spec asserts
            receivedMessages[1].Data.Should().Be("goodbye world");
            await CloseClient(client);
        }

        // UTS-NA: realtime/unit/PC3/no-plugin-fails-1 — not applicable: no plugin mechanism in this SDK
        // NOTE: the decoder is built in and cannot be configured away (realtime-mapping.md section 12); the spec's lines follow as comments.
        // channel_name = "test-PC3-no-plugin-${random_id()}"
        // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))   # No vcdiff plugin registered
        // Send a delta-encoded message without a vcdiff plugin registered
        // AWAIT_STATE channel.state == ChannelState.failed
        // ASSERT channel.state == ChannelState.failed
        // ASSERT channel.errorReason.code == 40019

        // UTS: realtime/unit/RTL18/decode-failure-recovery-0
        [UtsFact]
        [Trait("spec", "RTL18")]
        public async Task RTL18_DecodeFailureRecovery()
        {
            // Setup
            var channelName = "test-RTL18-recovery-" + RandomId();

            var attachMessages = new CaptureList<ProtocolMessage>();
            var receivedMessages = new CaptureList<Message>();

            var mockWs = AttachingMock(channelName, msg => attachMessages.Add(msg));
            InstallMock(mockWs);

            // Use a decoder that always fails
            var failingDecoder = new FailingMockVCDiffDecoder();
            InstallMock(failingDecoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            var stateChanges = UtsRecord.ChannelStateChanges(channel);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Establish base with a non-delta message first
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "base payload") }, "serial-100"));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Clear state tracking from initial attach
            stateChanges.Clear();
            var initialAttachCount = attachMessages.Count;

            // Send a delta message - the failing decoder will throw during decode
            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta("fake-delta-payload"), "vcdiff/base64", "msg-1:0") }, "serial-200"));

            // RTL18c: channel transitions to ATTACHING for recovery
            // NOTE: the mock answers the recovery ATTACH at once, so ATTACHING is transient: the recorded changes are polled instead of AWAIT_STATE
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(c => c.Current == ChannelState.Attaching),
                description: "channel entered ATTACHING for recovery");

            // Assertions
            // RTL18b: The failed delta message was NOT delivered to subscribers
            receivedMessages.Should().HaveCount(1);
            receivedMessages[0].Data.Should().Be("base payload");

            // RTL18c: A new ATTACH was sent for recovery
            attachMessages.Count.Should().BeGreaterThan(initialAttachCount);
            var recoveryAttach = attachMessages[attachMessages.Count - 1];

            // RTL18c: The ATTACH includes channelSerial from the previous successful message
            recoveryAttach.ChannelSerial.Should().Be("serial-100");

            // RTL18c: Channel state went to ATTACHING with error code 40018
            UtsAssert.ContainsInOrder(stateChanges.Snapshot().Select(c => c.Current).ToList(), new[] { ChannelState.Attaching });
            var attachingChange = stateChanges.Snapshot().First(c => c.Current == ChannelState.Attaching);
            attachingChange.Error.Code.Should().Be(40018);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL18c/recovery-completes-on-attached-0
        [UtsFact]
        [Trait("spec", "RTL18c")]
        public async Task RTL18c_RecoveryCompletesOnAttached()
        {
            // Setup
            var channelName = "test-RTL18c-complete-" + RandomId();
            var encoder = new MockVCDiffEncoder();

            var receivedMessages = new CaptureList<Message>();
            var attachCount = 0;

            var mockWs = AttachingMock(channelName, msg => Interlocked.Increment(ref attachCount));
            InstallMock(mockWs);

            // Use a decoder that fails on first call, then succeeds
            var decodeAttempt = 0;
            var conditionalDecoder = new MockVCDiffDecoder(onDecode: (delta, @base) =>
            {
                if (Interlocked.Increment(ref decodeAttempt) == 1)
                {
                    throw new InvalidOperationException("Simulated decode failure");
                }
            });
            InstallMock(conditionalDecoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            var stateChanges = UtsRecord.ChannelStateChanges(channel);
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Establish base
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "original base") }, "serial-1"));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message received");

            // Send delta that will fail on first decode attempt
            // This triggers recovery -> ATTACHING -> ATTACHED
            var recovery = UtsRecord.ChannelStates(channel);
            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta("bad-delta"), "vcdiff/base64", "msg-1:0") }, "serial-2"));

            // Recovery: ATTACHING -> server auto-responds with ATTACHED
            // NOTE: AWAIT_STATE attached would return at once (the channel is still ATTACHED when the failing delta is sent); the states recorded from just before it are polled for ATTACHING then ATTACHED
            await UtsWait.PollUntil(
                () => recovery.Snapshot().SkipWhile(s => s != ChannelState.Attaching).Any(s => s == ChannelState.Attached),
                description: "recovery re-attached the channel");

            stateChanges.Clear();

            // After recovery, server resends from channelSerial with a fresh non-delta
            mockWs.SendToClient(MessageFrame(channelName, "msg-3:0", new JArray { PlainMessage("msg-3:0", "fresh after recovery") }, "serial-3"));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 messages received");

            // Assertions
            // Channel recovered and is now attached
            channel.State.Should().Be(ChannelState.Attached);

            // Messages received: the original base and the fresh message after recovery
            // (the failed delta msg-2 was discarded per RTL18b)
            receivedMessages[0].Data.Should().Be("original base");
            receivedMessages[1].Data.Should().Be("fresh after recovery");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL18/single-recovery-at-time-1
        [UtsFact]
        [Trait("spec", "RTL18")]
        public async Task RTL18_SingleRecoveryAtTime()
        {
            // Setup
            var channelName = "test-RTL18-single-recovery-" + RandomId();

            var attachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    attachMessages.Add(msg);

                    // Do NOT auto-respond with ATTACHED - leave recovery in progress
                    if (attachMessages.Count == 1)
                    {
                        // Only respond to the initial attach
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                }
            };
            InstallMock(mockWs);

            var failingDecoder = new FailingMockVCDiffDecoder();
            InstallMock(failingDecoder);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var initialAttachCount = attachMessages.Count;

            // Establish base
            mockWs.SendToClient(MessageFrame(channelName, "msg-1:0", new JArray { PlainMessage("msg-1:0", "base") }, "serial-1"));

            await ProcessPendingEvents();

            // Send first delta that will fail - triggers recovery
            mockWs.SendToClient(MessageFrame(channelName, "msg-2:0", new JArray { DeltaMessage("msg-2:0", Base64Delta("bad-delta-1"), "vcdiff/base64", "msg-1:0") }));

            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Send second delta that also fails - recovery already in progress
            mockWs.SendToClient(MessageFrame(channelName, "msg-3:0", new JArray { DeltaMessage("msg-3:0", Base64Delta("bad-delta-2"), "vcdiff/base64", "msg-2:0") }));

            await ProcessPendingEvents();

            // Assertions
            // Only one recovery ATTACH was sent (not two)
            var recoveryAttaches = attachMessages.Count - initialAttachCount;
            recoveryAttaches.Should().Be(1);
            await CloseClient(client);
        }
    }
}
