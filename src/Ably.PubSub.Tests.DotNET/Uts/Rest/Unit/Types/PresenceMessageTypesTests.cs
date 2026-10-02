using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/presence_message_types.md in ably/specification.
    ///
    /// Spec points: TP2, TP3, TP3a, TP3b, TP3c, TP3d, TP3e, TP3f, TP3g, TP3h, TP4
    ///
    /// Adaptations, each also marked at its site:
    /// - TP3i has no counterpart. PresenceMessage carries no extras member at all — not public, not
    ///   internal — so every extras assertion in the spec is dropped. The field is still fed in with
    ///   the wire JSON, where Newtonsoft discards it as an unknown member.
    /// - TP4's fromEncoded / fromEncodedArray are not exposed on PresenceMessage; they exist only on
    ///   Message, as thin wrappers over the internal generic MessageHandler.FromEncoded. The readers
    ///   below call that generic directly, so the decoding behaviour is still covered.
    /// - TP3d, TP3a and TP3g decorate a received presence message from its encapsulating
    ///   ProtocolMessage. That decoration is MessageHandler.DecodeMessages, which is exactly what
    ///   ChannelMessageProcessor calls on the PRESENCE and SYNC path, so DecodePresence below drives
    ///   the production path rather than reimplementing it.
    /// - TP3h: MemberKey is clientId:connectionId in this SDK, the reverse of the spec's
    ///   connectionId:clientId, so that test keeps the spec-correct assertion under [DeviationFact].
    /// - TP3's serialising test: PresenceAction goes onto the wire as its numeric ordinal, which is
    ///   what the Ably JSON protocol carries. The spec's "enter" holds for the deserialising
    ///   direction only, where Newtonsoft accepts either spelling.
    /// - Decoding leaves Encoding as the empty string rather than null once the last encoding is
    ///   consumed (MessageEncoder.RemoveCurrentEncodingPart returns string.Empty), so the spec's
    ///   "encoding IS null" is asserted as empty wherever an encoding was actually consumed.
    /// - TP5 is not translated: nothing in this SDK computes a TM6-style message size, and
    ///   maxMessageSize is read off ConnectionDetails but never enforced.
    /// </summary>
    public class PresenceMessageTypesTests : UtsTestBase
    {
        public PresenceMessageTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's PresenceMessage.fromEncoded. See the class summary.</summary>
        private static PresenceMessage FromEncoded(PresenceMessage encoded) =>
            MessageHandler.FromEncoded(encoded);

        /// <summary>The spec's PresenceMessage.fromEncodedArray. See the class summary.</summary>
        private static PresenceMessage[] FromEncodedArray(PresenceMessage[] encoded) =>
            MessageHandler.FromEncodedArray(encoded);

        /// <summary>
        /// The decoration the SDK applies to every presence message it receives inside a
        /// ProtocolMessage, driven through the same call ChannelMessageProcessor makes.
        /// </summary>
        private static void DecodePresence(ProtocolMessage protocolMessage)
        {
            var handler = new MessageHandler(DefaultLogger.LoggerInstance, Protocol.Json);
            var result = handler.DecodeMessages(
                protocolMessage,
                protocolMessage.Presence,
                (ChannelOptions)null);

            result.IsSuccess.Should().BeTrue("every presence payload in these tests decodes cleanly");
        }

        // UTS: rest/unit/TP2/presence-action-enum-values-0
        [Fact]
        public void TP2_PresenceActionEnumValues()
        {
            ((int)PresenceAction.Absent).Should().Be(0);
            ((int)PresenceAction.Present).Should().Be(1);
            ((int)PresenceAction.Enter).Should().Be(2);
            ((int)PresenceAction.Leave).Should().Be(3);
            ((int)PresenceAction.Update).Should().Be(4);
        }

        // UTS: rest/unit/TP3a/presence-message-attributes-0
        [Fact]
        public void TP3a_PresenceMessageAttributes()
        {
            // TP3a - id attribute
            new PresenceMessage { Id = "presence-123" }.Id.Should().Be("presence-123");

            // TP3b - action attribute
            new PresenceMessage { Action = PresenceAction.Enter }.Action.Should().Be(PresenceAction.Enter);

            // TP3c - clientId attribute
            new PresenceMessage { ClientId = "user-1" }.ClientId.Should().Be("user-1");

            // TP3d - connectionId attribute
            new PresenceMessage { ConnectionId = "conn-1" }.ConnectionId.Should().Be("conn-1");

            // TP3e - data attribute (string)
            new PresenceMessage { Data = "hello" }.Data.Should().Be("hello");

            // TP3e - data attribute (object)
            var objectData = new JObject { ["status"] = "online" };
            var withObjectData = new PresenceMessage { Data = objectData };
            withObjectData.Data.Should().BeSameAs(objectData);
            ((JObject)withObjectData.Data)["status"].Value<string>().Should().Be("online");

            // TP3f - encoding attribute
            new PresenceMessage { Encoding = "json" }.Encoding.Should().Be("json");

            // TP3g - timestamp attribute. The spec writes epoch milliseconds; the .NET surface is a
            // nullable DateTimeOffset, which is the same instant.
            var withTimestamp = new PresenceMessage { Timestamp = 1234567890000L.FromUnixTimeInMilliseconds() };
            withTimestamp.Timestamp.Should().HaveValue();
            withTimestamp.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567890000L);

            // TP3i - extras attribute: PresenceMessage has no extras member. See the class summary.
        }

        // UTS: rest/unit/TP3h/member-key-combines-ids-0
        // The spec's order is connectionId:clientId, which is what the other Ably SDKs return. This
        // SDK returns clientId:connectionId — see Types/PresenceMessage.cs, whose doc comment states
        // the reversed order outright, and PresenceSandboxSpecs.cs:749, which pins it. The uniqueness
        // property the spec is really after survives the reversal; the documented format does not.
        [DeviationFact]
        public void TP3h_MemberKeyCombinesIds()
        {
            var msg = new PresenceMessage { ConnectionId = "conn-1", ClientId = "user-1" };
            msg.MemberKey.Should().Be("conn-1:user-1");

            var msg2 = new PresenceMessage { ConnectionId = "conn-2", ClientId = "user-1" };
            msg2.MemberKey.Should().Be("conn-2:user-1");

            // Same clientId, different connectionId - different memberKey.
            msg.MemberKey.Should().NotBe(msg2.MemberKey);
        }

        // UTS: rest/unit/TP3d/connectionid-from-protocol-message-0
        [Fact]
        public void TP3d_ConnectionIdFromProtocolMessage()
        {
            var protocolMessage = new ProtocolMessage
            {
                Action = ProtocolMessage.MessageAction.Presence,
                ConnectionId = "proto-conn-1",
                Presence = new[] { new PresenceMessage(PresenceAction.Enter, "user-1") },
            };

            DecodePresence(protocolMessage);

            var presenceMessage = protocolMessage.Presence[0];
            presenceMessage.ConnectionId.Should().Be("proto-conn-1");
        }

        // UTS: rest/unit/TP3a/id-from-protocol-message-1
        [Fact]
        public void TP3a_IdFromProtocolMessage()
        {
            var protocolMessage = new ProtocolMessage
            {
                Action = ProtocolMessage.MessageAction.Presence,
                Id = "proto-msg-42",
                Presence = new[]
                {
                    new PresenceMessage(PresenceAction.Enter, "alice"),
                    new PresenceMessage(PresenceAction.Enter, "bob"),
                },
            };

            DecodePresence(protocolMessage);

            protocolMessage.Presence[0].Id.Should().Be("proto-msg-42:0");
            protocolMessage.Presence[1].Id.Should().Be("proto-msg-42:1");
        }

        // UTS: rest/unit/TP3g/timestamp-from-protocol-message-0
        [Fact]
        public void TP3g_TimestampFromProtocolMessage()
        {
            var protocolMessage = new ProtocolMessage
            {
                Action = ProtocolMessage.MessageAction.Presence,
                Timestamp = 9999999L.FromUnixTimeInMilliseconds(),
                Presence = new[] { new PresenceMessage(PresenceAction.Enter, "user-1") },
            };

            DecodePresence(protocolMessage);

            var presenceMessage = protocolMessage.Presence[0];
            presenceMessage.Timestamp.Should().Be(protocolMessage.Timestamp);
            presenceMessage.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(9999999L);
        }

        // UTS: rest/unit/TP3/presence-from-json-0
        [Fact]
        public void TP3_PresenceFromJson()
        {
            // NOTE: the spec writes the action as the string "enter". The Ably JSON protocol carries
            // the presence action as its numeric ordinal, but Newtonsoft parses either spelling into
            // the enum, so the spec's wire object is translated verbatim.
            var wire = new JObject
            {
                ["id"] = "pm-123",
                ["action"] = "enter",
                ["clientId"] = "user-1",
                ["connectionId"] = "conn-1",
                ["data"] = "hello",
                ["encoding"] = JValue.CreateNull(),
                ["timestamp"] = 1234567890000L,
                ["extras"] = new JObject { ["headers"] = new JObject { ["x-key"] = "x-value" } },
            };

            var msg = JsonHelper.Deserialize<PresenceMessage>(wire.ToString());

            msg.Id.Should().Be("pm-123");
            msg.Action.Should().Be(PresenceAction.Enter);
            msg.ClientId.Should().Be("user-1");
            msg.ConnectionId.Should().Be("conn-1");
            msg.Data.Should().Be("hello");
            msg.Timestamp.Should().HaveValue();
            msg.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567890000L);

            // The spec's extras assertion has no counterpart. See the class summary.
        }

        // UTS: rest/unit/TP3/presence-encoded-data-from-json-1
        [Fact]
        public void TP3_PresenceEncodedDataFromJson()
        {
            // Case 1 - no encoding: the payload is carried through untouched.
            var plainText = DecodeFromWire(null, "plain text");
            plainText.Data.Should().Be("plain text");
            plainText.Encoding.Should().BeNull();

            // Case 2 - json: decoded into a JObject, and the encoding consumed. The spec asks for a
            // null encoding; the SDK leaves the empty string. See the class summary.
            var jsonEncoded = DecodeFromWire("json", "{\"status\":\"online\"}");
            ((JObject)jsonEncoded.Data)["status"].Value<string>().Should().Be("online");
            jsonEncoded.Encoding.Should().BeEmpty();

            // Case 3 - base64: decoded into bytes, and the encoding consumed.
            var base64Encoded = DecodeFromWire("base64", "SGVsbG8=");
            ((byte[])base64Encoded.Data).Should().Equal(System.Text.Encoding.UTF8.GetBytes("Hello"));
            base64Encoded.Encoding.Should().BeEmpty();

            PresenceMessage DecodeFromWire(string encoding, string wireData)
            {
                var wire = new JObject
                {
                    ["action"] = "enter",
                    ["clientId"] = "user-1",
                    ["data"] = wireData,
                    ["encoding"] = encoding == null ? (JToken)JValue.CreateNull() : new JValue(encoding),
                };

                return FromEncoded(JsonHelper.Deserialize<PresenceMessage>(wire.ToString()));
            }
        }

        // UTS: rest/unit/TP3/presence-to-json-2
        [Fact]
        public void TP3_PresenceToJson()
        {
            var msg = new PresenceMessage(PresenceAction.Enter, "user-1", "hello");

            var json = JObject.Parse(JsonHelper.Serialize(msg));

            // The spec expects "enter". The Ably JSON protocol carries the presence action as its
            // numeric ordinal and that is what this SDK writes. See the class summary.
            json["action"].Value<int>().Should().Be((int)PresenceAction.Enter);
            json["clientId"].Value<string>().Should().Be("user-1");
            json["data"].Value<string>().Should().Be("hello");

            // The spec's extras assertion has no counterpart. See the class summary.
        }

        // UTS: rest/unit/TP3/null-attributes-omitted-3
        [Fact]
        public void TP3_NullAttributesOmitted()
        {
            var msg = new PresenceMessage(PresenceAction.Enter, "user-1");

            var json = JObject.Parse(JsonHelper.Serialize(msg));

            json["action"].Value<int>().Should().Be((int)PresenceAction.Enter);
            json["clientId"].Value<string>().Should().Be("user-1");

            // The spec allows either absence or an explicit null. NullValueHandling.Ignore in
            // JsonHelper means this SDK always takes the stronger option and omits the key.
            json.ContainsKey("data").Should().BeFalse();
            json.ContainsKey("encoding").Should().BeFalse();
            json.ContainsKey("id").Should().BeFalse();
            json.ContainsKey("timestamp").Should().BeFalse();

            // extras is never written because there is no such member. See the class summary.
            json.ContainsKey("extras").Should().BeFalse();
        }

        // UTS: rest/unit/TP4/from-encoded-presence-0
        [Fact]
        public void TP4_FromEncodedPresenceSingle()
        {
            var raw = new PresenceMessage
            {
                Action = PresenceAction.Enter,
                ClientId = "user-1",
                Data = "{\"status\":\"online\"}",
                Encoding = "json",
            };

            var msg = FromEncoded(raw);

            msg.Action.Should().Be(PresenceAction.Enter);
            msg.ClientId.Should().Be("user-1");
            ((JObject)msg.Data)["status"].Value<string>().Should().Be("online");

            // The spec asks for a null encoding; the SDK leaves the empty string once the last
            // encoding is consumed. See the class summary.
            msg.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/TP4/from-encoded-presence-0
        [Fact]
        public void TP4_FromEncodedPresenceArray()
        {
            var raw = new[]
            {
                new PresenceMessage(PresenceAction.Enter, "alice", "hello"),
                new PresenceMessage(PresenceAction.Enter, "bob", "world"),
            };

            var messages = FromEncodedArray(raw);

            messages.Should().HaveCount(2);
            messages[0].ClientId.Should().Be("alice");
            messages[0].Data.Should().Be("hello");
            messages[1].ClientId.Should().Be("bob");
            messages[1].Data.Should().Be("world");
        }
    }
}
