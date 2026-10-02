using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/message_types.md in ably/specification.
    ///
    /// Spec points: TM1, TM2, TM3, TM4, TM2a, TM2b, TM2c, TM2d, TM2e, TM2f, TM2g, TM2h, TM2i
    ///
    /// Pure type validation: no client is built and no mock is installed, matching the spec's
    /// "No mocks required - these verify type structure, constructors, and encoding".
    ///
    /// Four translation notes apply across the file.
    ///
    /// 1. <c>Message.fromEncoded</c> is <c>Message.FromEncoded</c>. The overload used here is the one
    ///    taking the wire JSON as a string, which deserializes and then decodes - the spec's
    ///    "already-deserialized Message-like object" is a dictionary in languages that have one, and a
    ///    JSON document is the closest faithful input in .NET. (The <c>FromEncoded(Message, ...)</c>
    ///    overload exists too, but constructing the pre-decode Message by hand would bypass exactly
    ///    the wire-name mapping TM3 is about.)
    ///
    /// 2. <c>Message.timestamp</c> is milliseconds on the wire and a <c>DateTimeOffset?</c> here, so
    ///    the spec's integer assertions go through <c>ToUnixTimeInMilliseconds()</c>.
    ///
    /// 3. <c>Message.extras</c> is a <c>MessageExtras</c> wrapping a <c>JToken</c> rather than a raw
    ///    map, so the spec's <c>extras["push"]["notification"]["title"]</c> reads through
    ///    <c>Extras.ToJson()</c>.
    ///
    /// 4. TM2i (serial) has no member on .NET's <c>Message</c>. The spec asserts nothing for it -
    ///    "Serial is typically read-only from server responses" - so there is nothing to translate,
    ///    but the absent property is worth knowing about.
    /// </summary>
    public class MessageTypesTests : UtsTestBase
    {
        private const long TimestampMs = 1234567890000;

        public MessageTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2a_MessageIdAttribute()
        {
            // There is no id constructor parameter; id is a settable property.
            var message = new Message { Id = "unique-id" };

            message.Id.Should().Be("unique-id");
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2b_MessageNameAttribute()
        {
            var message = new Message(name: "event-name");

            message.Name.Should().Be("event-name");
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2c_MessageDataAttribute()
        {
            // String payload.
            var stringMessage = new Message(data: "string-data");
            stringMessage.Data.Should().Be("string-data");

            // Object payload. Data is declared as object, so the instance the caller passed is the
            // instance the property returns - the spec's structural comparison is identity here.
            var objectPayload = JObject.Parse("{\"key\":\"value\"}");
            var objectMessage = new Message(data: objectPayload);
            objectMessage.Data.Should().BeSameAs(objectPayload);
            ((JObject)objectMessage.Data)["key"].Value<string>().Should().Be("value");

            // Binary payload.
            var binaryPayload = new byte[] { 0x01, 0x02 };
            var binaryMessage = new Message(data: binaryPayload);
            binaryMessage.Data.Should().BeSameAs(binaryPayload);
            ((byte[])binaryMessage.Data).Should().Equal(new byte[] { 0x01, 0x02 });
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2d_MessageClientIdAttribute()
        {
            var message = new Message(clientId: "message-client");

            message.ClientId.Should().Be("message-client");
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2e_MessageConnectionIdAttribute()
        {
            var message = new Message { ConnectionId = "conn-id" };

            message.ConnectionId.Should().Be("conn-id");
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2f_MessageTimestampAttribute()
        {
            var message = new Message { Timestamp = TimestampMs.FromUnixTimeInMilliseconds() };

            message.Timestamp.Should().NotBeNull();
            message.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(TimestampMs);
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2g_MessageEncodingAttribute()
        {
            var message = new Message { Encoding = "json/base64" };

            message.Encoding.Should().Be("json/base64");
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        public void TM2h_MessageExtrasAttribute()
        {
            var extras = new MessageExtras(
                JObject.Parse("{\"push\":{\"notification\":{\"title\":\"Hello\"}}}"));

            var message = new Message(extras: extras);

            message.Extras.Should().NotBeNull();
            message.Extras.ToJson()["push"]["notification"]["title"].Value<string>().Should().Be("Hello");
        }

        // UTS: rest/unit/TM3/from-encoded-deserialization-0
        [Fact]
        public void TM3_FromEncodedDeserialization()
        {
            const string wireJson =
                "{\"id\":\"msg-123\",\"name\":\"test-event\",\"data\":\"hello world\"," +
                "\"clientId\":\"sender-client\",\"connectionId\":\"conn-456\"," +
                "\"timestamp\":1234567890000,\"encoding\":null," +
                "\"extras\":{\"headers\":{\"x-custom\":\"value\"}}}";

            var message = Message.FromEncoded(wireJson);

            message.Id.Should().Be("msg-123");
            message.Name.Should().Be("test-event");
            message.Data.Should().Be("hello world");
            message.ClientId.Should().Be("sender-client");
            message.ConnectionId.Should().Be("conn-456");
            message.Timestamp.Should().NotBeNull();
            message.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(TimestampMs);
            message.Extras.Should().NotBeNull();
            message.Extras.ToJson()["headers"]["x-custom"].Value<string>().Should().Be("value");
        }

        // UTS: rest/unit/TM3/from-encoded-decodes-encoding-1
        // Case 1 of the spec's table: no encoding, so the payload passes through untouched.
        [Fact]
        public void TM3_FromEncodedDecodesEncodingNone()
        {
            const string wireJson =
                "{\"id\":\"msg\",\"name\":\"event\",\"data\":\"plain text\",\"encoding\":null}";

            var message = Message.FromEncoded(wireJson);

            message.Data.Should().Be("plain text");
            message.Encoding.Should().BeNull();
        }

        // UTS: rest/unit/TM3/from-encoded-decodes-encoding-1
        // Case 2 of the spec's table.
        [Fact]
        public void TM3_FromEncodedDecodesEncodingJson()
        {
            const string wireJson =
                "{\"id\":\"msg\",\"name\":\"event\",\"data\":\"{\\\"key\\\":\\\"value\\\"}\"," +
                "\"encoding\":\"json\"}";

            var message = Message.FromEncoded(wireJson);

            // The spec's expected value is the deserialized object. .NET's JsonEncoder parses with no
            // target type, which yields a JObject - the idiomatic .NET rendering of "an object".
            message.Data.Should().BeOfType<JObject>();
            ((JObject)message.Data)["key"].Value<string>().Should().Be("value");

            // SPEC: ASSERT message.encoding IS null (the encoding has been consumed).
            // ADAPTED: the SDK removes the consumed part by rejoining the remaining parts, which leaves
            // string.Empty rather than null once the last part goes. Everything in the SDK reads the
            // field through IsEmpty()/IsNotEmpty(), which treat null and "" alike, so this is a
            // spelling difference rather than residual encoding. BeEmpty() fails on null, so the
            // assertion still pins which of the two the SDK produces.
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/TM3/from-encoded-decodes-encoding-1
        // Case 3 of the spec's table.
        [Fact]
        public void TM3_FromEncodedDecodesEncodingBase64()
        {
            const string wireJson =
                "{\"id\":\"msg\",\"name\":\"event\",\"data\":\"SGVsbG8=\",\"encoding\":\"base64\"}";

            var message = Message.FromEncoded(wireJson);

            message.Data.Should().BeOfType<byte[]>();
            ((byte[])message.Data).Should().Equal("Hello".GetBytes());

            // See the note on TM3_FromEncodedDecodesEncodingJson for null vs string.Empty.
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/TM3/from-encoded-decodes-encoding-1
        //
        // Case 4 of the spec's table. Two separate things are wrong here and the test asserts only
        // the second, so read the comment before changing it.
        //
        // The spec's own expectation — that `json/base64` fully decodes — is a UTS spec error, not
        // an SDK gap: RSL4d1 defines base64 for binary and RSL4d3 defines json as producing a
        // string, so the canonical chain is `json/utf-8/base64`, which this SDK decodes correctly.
        // Do NOT "fix" JsonEncoder to accept a byte[] on the strength of this test. That half is
        // recorded under "UTS Spec Errors".
        //
        // What *is* a deviation is the failure mode. TM3 requires the result "decoded and decrypted
        // as specified in RSL6 with any residual transforms left in the encoding property per
        // RSL6b", and RSL6b requires the message "still be delivered with last successful decoding
        // and the encoding field". FromEncoded instead converts the failed decode step into a thrown
        // AblyException (MessageHandler.cs:541). Its neighbour FromEncodedArray, twelve lines below,
        // discards the same Result and degrades correctly — so the SDK contradicts itself, and
        // ably-js's fromEncoded logs and delivers rather than throwing.
        //
        // The assertions below are therefore the RSL6b outcome, which is what the spec actually
        // requires. Env-gated because the SDK throws before reaching any of them.
        [DeviationFact]
        public void TM3_FromEncodedDecodesEncodingJsonBase64()
        {
            const string wireJson =
                "{\"id\":\"msg\",\"name\":\"event\",\"data\":\"eyJrIjoidiJ9\"," +
                "\"encoding\":\"json/base64\"}";

            var message = Message.FromEncoded(wireJson);

            message.Data.Should().BeOfType<byte[]>();
            System.Text.Encoding.UTF8.GetString((byte[])message.Data).Should().Be("{\"k\":\"v\"}");
            message.Encoding.Should().Be("json");
        }

        // UTS: rest/unit/TM4/message-constructors-0
        [Fact]
        public void TM4_MessageConstructors()
        {
            // constructor(name, data)
            var nameAndData = new Message("event-name", "payload");
            nameAndData.Name.Should().Be("event-name");
            nameAndData.Data.Should().Be("payload");
            nameAndData.ClientId.Should().BeNull();

            // constructor(name, data, clientId)
            var withClientId = new Message("event-name", "payload", "client-1");
            withClientId.Name.Should().Be("event-name");
            withClientId.Data.Should().Be("payload");
            withClientId.ClientId.Should().Be("client-1");

            // Both name and data are nullable.
            var empty = new Message(name: null, data: null);
            empty.Name.Should().BeNull();
            empty.Data.Should().BeNull();
        }

        // UTS: rest/unit/TM/null-missing-attributes-0
        [Fact]
        public void TM_NullMissingAttributes()
        {
            var message = new Message();

            message.Id.Should().BeNull();
            message.Name.Should().BeNull();
            message.Data.Should().BeNull();
            message.ClientId.Should().BeNull();
            message.Timestamp.Should().BeNull();
        }

        // UTS: rest/unit/TM/message-with-extras-1
        [Fact]
        public void TM_MessageWithExtras()
        {
            const string extrasJson =
                "{\"push\":{\"notification\":{\"title\":\"New Message\"," +
                "\"body\":\"You have a new notification\"}," +
                "\"data\":{\"customKey\":\"customValue\"}}}";

            var message = new Message(
                "push-event",
                "payload",
                extras: new MessageExtras(JObject.Parse(extrasJson)));

            var extras = message.Extras.ToJson();

            extras["push"]["notification"]["title"].Value<string>().Should().Be("New Message");
            extras["push"]["notification"]["body"].Value<string>().Should().Be("You have a new notification");
            extras["push"]["data"]["customKey"].Value<string>().Should().Be("customValue");
        }
    }
}
