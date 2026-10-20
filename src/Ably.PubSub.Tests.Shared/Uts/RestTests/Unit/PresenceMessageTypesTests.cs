// PresenceMessage Types Tests (TP1, TP2, TP3, TP3a, TP3b, TP3c, TP3d, TP3e, TP3f, TP3g, TP3h, TP3i, TP4, TP5)
// Derived from uts/rest/unit/types/presence_message_types.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: TP3d, TP3a and TP3g: the spec reads the presence message "after processing" its ProtocolMessage but defines no processing step and the SDK has no public entry point for it, so the PRESENCE ProtocolMessage is delivered to a realtime client over the mock WebSocket and the message is read from the presence subscription (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class PresenceMessageTypesTests : UtsUnitTestBase
    {
        public PresenceMessageTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TP2/presence-action-enum-values-0
        [Fact]
        [Trait("spec", "TP2")]
        public void TP2_PresenceActionEnumValues()
        {
            // Test Steps
            ((int)PresenceAction.Absent).Should().Be(0);
            ((int)PresenceAction.Present).Should().Be(1);
            ((int)PresenceAction.Enter).Should().Be(2);
            ((int)PresenceAction.Leave).Should().Be(3);
            ((int)PresenceAction.Update).Should().Be(4);
        }

        // UTS: rest/unit/TP3a/presence-message-attributes-0
        [Fact]
        [Trait("spec", "TP3a")]
        public void TP3a_PresenceMessageAttributes()
        {
            // Test Steps
            // TP3a - id attribute
            var msg = new PresenceMessage { Id = "presence-123" };
            msg.Id.Should().Be("presence-123");

            // TP3b - action attribute
            msg = new PresenceMessage { Action = PresenceAction.Enter };
            msg.Action.Should().Be(PresenceAction.Enter);

            // TP3c - clientId attribute
            msg = new PresenceMessage { ClientId = "user-1" };
            msg.ClientId.Should().Be("user-1");

            // TP3d - connectionId attribute
            msg = new PresenceMessage { ConnectionId = "conn-1" };
            msg.ConnectionId.Should().Be("conn-1");

            // TP3e - data attribute (string)
            msg = new PresenceMessage { Data = "hello" };
            msg.Data.Should().Be("hello");

            // TP3e - data attribute (object)
            msg = new PresenceMessage { Data = new JObject { ["status"] = "online" } };
            JToken.DeepEquals(msg.Data as JToken, new JObject { ["status"] = "online" }).Should().BeTrue();

            // TP3f - encoding attribute
            msg = new PresenceMessage { Encoding = "json" };
            msg.Encoding.Should().Be("json");

            // TP3g - timestamp attribute
            msg = new PresenceMessage { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000) };
            msg.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // TP3i - extras attribute
            // NOTE: no assertion: PresenceMessage has no extras attribute (TP3i) in this SDK, so the spec lines are kept as comments (missing SDK API)
            // msg = PresenceMessage(extras: { "headers": { "x-custom": "value" } })
            // ASSERT msg.extras["headers"]["x-custom"] == "value"
        }

        // UTS: rest/unit/TP3h/member-key-combines-ids-0
        [Fact]
        [Trait("spec", "TP3h")]
        public void TP3h_MemberKeyCombinesIds()
        {
            // Test Steps
            // NOTE: TP3h permits any combination of connectionId and clientId; the SDK builds "{clientId}:{connectionId}". The UTS spec asserts "{connectionId}:{clientId}" - see deviations.md (UTS Spec Errors, TP3h)
            var msg = new PresenceMessage { ConnectionId = "conn-1", ClientId = "user-1" };
            msg.MemberKey.Should().Be("user-1:conn-1");

            var msg2 = new PresenceMessage { ConnectionId = "conn-2", ClientId = "user-1" };
            msg2.MemberKey.Should().Be("user-1:conn-2");

            // Same clientId, different connectionId — different memberKey
            msg.MemberKey.Should().NotBe(msg2.MemberKey);
        }

        // UTS: rest/unit/TP3d/connectionid-from-protocol-message-0
        [UtsFact]
        [Trait("spec", "TP3d")]
        public async Task TP3d_ConnectionidFromProtocolMessage()
        {
            // Setup
            var channelName = "test-TP3d-" + Guid.NewGuid().ToString("N");
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedMessages = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(message => receivedMessages.Add(message));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Test Steps
            // protocol_msg = ProtocolMessage(action: PRESENCE, connectionId: "proto-conn-1", presence: [{ "action": "enter", "clientId": "user-1" }])
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "user-1")),
                new Dictionary<string, JToken> { ["connectionId"] = "proto-conn-1" }));
            await ProcessPendingEvents();

            // Assertions
            // After processing, the PresenceMessage should inherit connectionId
            receivedMessages.Should().HaveCount(1);
            var presenceMsg = receivedMessages[0];
            presenceMsg.ConnectionId.Should().Be("proto-conn-1");
        }

        // UTS: rest/unit/TP3a/id-from-protocol-message-1
        [UtsFact]
        [Trait("spec", "TP3a")]
        public async Task TP3a_IdFromProtocolMessage()
        {
            // Setup
            var channelName = "test-TP3a-" + Guid.NewGuid().ToString("N");
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedMessages = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(message => receivedMessages.Add(message));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Test Steps
            // protocol_msg = ProtocolMessage(action: PRESENCE, id: "proto-msg-42", presence: [{ "action": "enter", "clientId": "alice" }, { "action": "enter", "clientId": "bob" }])
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice"),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "bob")),
                new Dictionary<string, JToken> { ["id"] = "proto-msg-42" }));
            await ProcessPendingEvents();

            // Assertions
            // After processing, presence messages should have derived ids
            receivedMessages.Should().HaveCount(2);
            receivedMessages[0].Id.Should().Be("proto-msg-42:0");
            receivedMessages[1].Id.Should().Be("proto-msg-42:1");
        }

        // UTS: rest/unit/TP3g/timestamp-from-protocol-message-0
        [UtsFact]
        [Trait("spec", "TP3g")]
        public async Task TP3g_TimestampFromProtocolMessage()
        {
            // Setup
            var channelName = "test-TP3g-" + Guid.NewGuid().ToString("N");
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedMessages = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(message => receivedMessages.Add(message));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Test Steps
            // protocol_msg = ProtocolMessage(action: PRESENCE, timestamp: 9999999, presence: [{ "action": "enter", "clientId": "user-1" }])
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "user-1")),
                new Dictionary<string, JToken> { ["timestamp"] = 9999999 }));
            await ProcessPendingEvents();

            // Assertions
            receivedMessages.Should().HaveCount(1);
            var presenceMsg = receivedMessages[0];
            presenceMsg.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(9999999));
        }

        // UTS: rest/unit/TP3/presence-from-json-0
        [Fact]
        [Trait("spec", "TP3")]
        public void TP3_PresenceFromJson()
        {
            // Test Steps
            var jsonData = new JObject
            {
                ["id"] = "pm-123",
                ["action"] = "enter",
                ["clientId"] = "user-1",
                ["connectionId"] = "conn-1",
                ["data"] = "hello",
                ["encoding"] = JValue.CreateNull(),
                ["timestamp"] = 1234567890000,
                ["extras"] = new JObject
                {
                    ["headers"] = new JObject { ["x-key"] = "x-value" },
                },
            };

            var msg = JsonHelper.DeserializeObject<PresenceMessage>(jsonData);

            // Assertions
            msg.Id.Should().Be("pm-123");
            msg.Action.Should().Be(PresenceAction.Enter);
            msg.ClientId.Should().Be("user-1");
            msg.ConnectionId.Should().Be("conn-1");
            msg.Data.Should().Be("hello");
            msg.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // NOTE: no assertion: PresenceMessage has no extras attribute (TP3i) in this SDK (missing SDK API)
            // ASSERT msg.extras["headers"]["x-key"] == "x-value"
        }

        // UTS: rest/unit/TP3/presence-encoded-data-from-json-1
        // DEVIATION: see deviations.md
        [DeviationFact("TP3", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TP3")]
        public void TP3_PresenceEncodedDataFromJson()
        {
            // Test Cases
            var testCases = new[]
            {
                new { Encoding = (string)null, WireData = "plain text", ExpectedData = (object)"plain text" },
                new { Encoding = "json", WireData = "{\"status\":\"online\"}", ExpectedData = (object)new JObject { ["status"] = "online" } },
                new { Encoding = "base64", WireData = "SGVsbG8=", ExpectedData = (object)new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F } },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                var jsonData = new JObject
                {
                    ["action"] = "enter",
                    ["clientId"] = "user-1",
                    ["data"] = testCase.WireData,
                    ["encoding"] = testCase.Encoding,
                };

                var msg = JsonHelper.DeserializeObject<PresenceMessage>(jsonData);

                if (testCase.ExpectedData is byte[] expectedBytes)
                {
                    ((byte[])msg.Data).Should().Equal(expectedBytes);
                }
                else if (testCase.ExpectedData is JToken expectedToken)
                {
                    JToken.DeepEquals(msg.Data as JToken, expectedToken).Should().BeTrue();
                }
                else
                {
                    msg.Data.Should().Be(testCase.ExpectedData);
                }

                // Encoding consumed
                msg.Encoding.Should().BeNull();
            }
        }

        // UTS: rest/unit/TP3/presence-to-json-2
        [UtsFact(Skip = "UTS spec error TP3: the test expects the string enter as the presence action on the wire, protocol.md encodes it as the ordinal; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "TP3")]
        public Task TP3_PresenceToJson()
        {
            // SPEC ERROR TP3: protocol.md (Presence Message, action) says the action is "encoded as the ordinal in the PresenceMessage Action enum"; the test asserts json_data["action"] == "enter".
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error TP3 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TP3/null-attributes-omitted-3
        [UtsFact(Skip = "UTS spec error TP3: the test expects the string enter as the presence action on the wire, protocol.md encodes it as the ordinal; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "TP3")]
        public Task TP3_NullAttributesOmitted()
        {
            // SPEC ERROR TP3: protocol.md (Presence Message, action) says the action is "encoded as the ordinal in the PresenceMessage Action enum"; the test asserts json_data["action"] == "enter".
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error TP3 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TP4/from-encoded-presence-0
        [UtsFact(Skip = "Missing SDK public API: PresenceMessage#fromEncoded (TP4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TP4")]
        public Task TP4_FromEncodedPresence()
        {
            // NOTE: missing SDK API: the SDK has no PresenceMessage.FromEncoded / FromEncodedArray (rest-mapping.md section 8, Missing APIs; only the internal MessageHandler.FromEncoded<T> exists), so the whole test is a stub. Spec lines:
            // # fromEncoded — single message
            // raw = {
            //   "action": "enter",
            //   "clientId": "user-1",
            //   "data": "{\"status\":\"online\"}",
            //   "encoding": "json"
            // }
            //
            // msg = PresenceMessage.fromEncoded(raw)
            //
            // ASSERT msg.action == ENTER
            // ASSERT msg.clientId == "user-1"
            // ASSERT msg.data == { "status": "online" }
            // ASSERT msg.encoding IS null
            //
            // # fromEncodedArray — array of messages
            // raw_array = [
            //   { "action": "enter", "clientId": "alice", "data": "hello" },
            //   { "action": "enter", "clientId": "bob", "data": "world" }
            // ]
            //
            // messages = PresenceMessage.fromEncodedArray(raw_array)
            //
            // ASSERT messages.length == 2
            // ASSERT messages[0].clientId == "alice"
            // ASSERT messages[0].data == "hello"
            // ASSERT messages[1].clientId == "bob"
            // ASSERT messages[1].data == "world"
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TP5/presence-message-size-0
        [UtsFact(Skip = "Missing SDK public API: PresenceMessage#size (TP5); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TP5")]
        public Task TP5_PresenceMessageSize()
        {
            // NOTE: missing SDK API: PresenceMessage has no size attribute (TP5; rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // # Size includes clientId + data + extras (same formula as TM6)
            // msg = PresenceMessage(
            //   action: ENTER,
            //   clientId: "user-1",
            //   data: "hello"
            // )
            //
            // size = msg.size
            //
            // # Size should account for clientId (6 bytes) + data (5 bytes) = 11
            // ASSERT size == 11
            //
            // # Size with object data (JSON-encoded size)
            // msg2 = PresenceMessage(
            //   action: ENTER,
            //   clientId: "u",
            //   data: { "key": "value" }
            // )
            //
            // # clientId (1) + JSON-encoded data length
            // ASSERT msg2.size > 1
            return Task.CompletedTask;
        }
    }
}
