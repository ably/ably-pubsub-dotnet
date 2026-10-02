using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Encoding
{
    /// <summary>
    /// Derived from uts/rest/unit/encoding/message_encoding.md in ably/specification.
    ///
    /// Spec points: RSL4, RSL4a, RSL4b, RSL4c, RSL4d, RSL6, RSL6a, RSL6b
    ///
    /// Three file-wide adaptations, each restated at the site that depends on it:
    ///
    /// 1. The spec asserts that a decoded message's encoding "IS null". The SDK consumes an encoding
    ///    by dropping its last segment (MessageEncoder.RemoveCurrentEncodingPart), which leaves
    ///    string.Empty once the last segment goes rather than null, so every decode test asserts
    ///    BeNullOrEmpty().
    /// 2. The spec asserts that an absent data / encoding on the wire "IS null". JsonHelper
    ///    serialises with NullValueHandling.Ignore, so the property is omitted from the request body
    ///    instead of being written as JSON null. That is the spec's own "NOT IN body OR IS null"
    ///    alternative, and HasNoValue accepts either.
    /// 3. msgpack is compiled out of this build, so the spec's four msgpack tests (RSL4c
    ///    binary-direct, RSL6 msgpack-binary, RSL6 msgpack-string, RSL4 msgpack content type) have
    ///    no runnable variant here and are not translated.
    ///
    /// The spec's channel names carry random_id(). That only matters in the integration tier, where
    /// one app is shared, but it is kept so the derived test reads like the spec.
    /// </summary>
    public class MessageEncodingTests : UtsTestBase
    {
        public MessageEncodingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL4a/string-data-no-encoding-0
        [Fact]
        public async Task RSL4a_StringDataNoEncoding()
        {
            var channelName = $"test-RSL4a-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            // The spec's useBinaryProtocol: false is the only protocol this build has.
            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", "plain string data");

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["data"].Value<string>().Should().Be("plain string data");
            HasNoValue(body, "encoding").Should().BeTrue("a string is transmitted untransformed");
        }

        // UTS: rest/unit/RSL4b/json-object-encoding-0
        [Fact]
        public async Task RSL4b_JsonObjectEncoding()
        {
            var channelName = $"test-RSL4b-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", new { key = "value", nested = new { a = 1 } });

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["data"].Type.Should().Be(JTokenType.String);
            JToken.DeepEquals(
                    JToken.Parse(body["data"].Value<string>()),
                    JObject.Parse("{\"key\":\"value\",\"nested\":{\"a\":1}}"))
                .Should().BeTrue();
            body["encoding"].Value<string>().Should().Be("json");
        }

        // UTS: rest/unit/RSL4c/binary-base64-json-protocol-0
        [Fact]
        public async Task RSL4c_BinaryBase64JsonProtocol()
        {
            var channelName = $"test-RSL4c-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
            await channel.PublishAsync("event", binaryData);

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["encoding"].Value<string>().Should().Be("base64");
            Convert.FromBase64String(body["data"].Value<string>()).Should().Equal(binaryData);
        }

        // UTS: rest/unit/RSL4d/array-json-encoding-0
        [Fact]
        public async Task RSL4d_ArrayJsonEncoding()
        {
            var channelName = $"test-RSL4d-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", new object[] { 1, 2, "three", new { four = 4 } });

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["encoding"].Value<string>().Should().Be("json");
            JToken.DeepEquals(
                    JToken.Parse(body["data"].Value<string>()),
                    JArray.Parse("[1,2,\"three\",{\"four\":4}]"))
                .Should().BeTrue();
        }

        // UTS: rest/unit/RSL6a/decode-base64-to-binary-0
        [Fact]
        public async Task RSL6a_DecodeBase64ToBinary()
        {
            var channelName = $"test-RSL6a-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "AAECAwQ=", // base64 of [0, 1, 2, 3, 4]
                            encoding = "base64",
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            message.Data.Should().BeOfType<byte[]>();
            ((byte[])message.Data).Should().Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

            // Spec: "message.encoding IS null". Consuming the last encoding leaves string.Empty here,
            // because MessageEncoder.RemoveCurrentEncodingPart joins zero remaining segments.
            message.Encoding.Should().BeNullOrEmpty();
        }

        // UTS: rest/unit/RSL6a/decode-json-to-object-1
        [Fact]
        public async Task RSL6a_DecodeJsonToObject()
        {
            var channelName = $"test-RSL6a-json-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "{\"key\":\"value\",\"number\":42}",
                            encoding = "json",
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // The spec's "native object" is a JObject in .NET: JsonHelper.Deserialize with no target
            // type is Newtonsoft's untyped read, which yields the Linq-to-JSON tree.
            message.Data.Should().BeOfType<JObject>();
            JToken.DeepEquals(
                    (JToken)message.Data,
                    JObject.Parse("{\"key\":\"value\",\"number\":42}"))
                .Should().BeTrue();
            message.Encoding.Should().BeNullOrEmpty();
        }

        // UTS: rest/unit/RSL6a/decode-chained-encodings-2
        //
        // UTS SPEC ERROR in the expectation — but the SDK's behaviour on this path is compliant, so
        // this is a normal passing test asserting the RSL6b outcome. Recorded under "UTS Spec
        // Errors" in Uts/deviations.md.
        //
        // The spec asserts that `json/base64` fully decodes to a JObject, on the strength of its own
        // added requirement that "a subsequent json decoder MUST convert those bytes to a UTF-8
        // string before JSON parsing". That requirement is not in the features spec, and the chain
        // is not one the RSL4 encoding rules can produce: RSL4d1 defines base64 for *binary* and
        // RSL4d3 defines json as producing a *string*, so the canonical chain is `json/utf-8/base64`
        // — which is the shape of the features spec's own worked examples, and which this SDK
        // handles correctly (see RSL6_ComplexChainedEncoding). No reference SDK implements the extra
        // requirement either: ably-java throws an uncaught ClassCastException on the same input and
        // ably-js only survives it on Node, by accident of Buffer-to-string coercion.
        //
        // What the SDK actually does here is exactly RSL6b: JsonEncoder.Decode reads
        // `payload.Data as string`, which is null for a byte[], so the step fails and logs; the
        // payload is left as of the last successful decoding (the base64 bytes) and the undecoded
        // transform stays in `encoding`. That is "an error message will be sent to the logger, but
        // the message will still be delivered with last successful decoding and the encoding field".
        //
        // So the assertions below are the spec-compliant ones, not adapted ones.
        [Fact]
        public async Task RSL6a_DecodeChainedEncodings()
        {
            var channelName = $"test-RSL6a-chained-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();

            // Data: {"key":"value"} -> JSON string -> base64 encoded.
            var jsonString = "{\"key\":\"value\"}";
            var base64OfJson = Base64Of(jsonString);

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = base64OfJson,
                            encoding = "json/base64", // Decode base64 first, then JSON.
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // RSL6b: delivered with the last successful decoding — the base64 step — and the
            // transform it could not apply left in `encoding`.
            message.Data.Should().BeOfType<byte[]>();
            System.Text.Encoding.UTF8.GetString((byte[])message.Data).Should().Be(jsonString);
            message.Encoding.Should().Be("json");
        }

        // UTS: rest/unit/RSL6b/unrecognized-encoding-preserved-0
        [Fact]
        public async Task RSL6b_UnrecognizedEncodingPreserved()
        {
            var channelName = $"test-RSL6b-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "encrypted-data-here",
                            encoding = "custom-encryption/base64",
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // base64 is decoded; custom-encryption has no decoder, so it is left in place and the
            // data is whatever the base64 step produced.
            message.Encoding.Should().Be("custom-encryption");
            message.Data.Should().BeOfType<byte[]>();
        }

        // UTS: rest/unit/RSL4/encoding-fixtures-ably-common-0
        [Fact]
        public async Task RSL4_EncodingFixturesAblyCommon()
        {
            // NOTE: the spec loads a fixture file "encoding.json" whose entries carry input_data,
            // expected_wire_data, expected_encoding and use_binary_protocol. No such file exists in
            // ably-common. The real fixture is messages-encoding.json, vendored at
            // common/test-resources and embedded in this assembly, whose entries carry the wire form
            // (data + encoding) alongside the decoded expectedType / expectedValue. It is read here
            // in the encode direction, which is what its own header comment asks for: "send that
            // again via realtime and check that it's encoded the same as here". There is no
            // use_binary_protocol counterpart because msgpack is compiled out.
            var fixtures = (JArray)JObject.Parse(ResourceHelper.GetResource("messages-encoding.json"))["messages"];

            for (var i = 0; i < fixtures.Count; i++)
            {
                var fixture = fixtures[i];
                var expectedWireData = fixture["data"].Value<string>();
                var expectedEncoding = fixture["encoding"].Type == JTokenType.Null
                    ? null
                    : fixture["encoding"].Value<string>();
                var expectedType = fixture["expectedType"].Value<string>();

                // Named rather than quoted in the failure messages: a fixture's wire form contains
                // braces, and a FluentAssertions `because` string is a format template.
                var describe = $"fixture {i} of type {expectedType}";

                object inputData;
                switch (expectedType)
                {
                    case "string":
                        inputData = fixture["expectedValue"].Value<string>();
                        break;
                    case "jsonObject":
                    case "jsonArray":
                        inputData = fixture["expectedValue"];
                        break;
                    case "binary":
                        inputData = FromHex(fixture["expectedHexValue"].Value<string>());
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unhandled fixture expectedType '{expectedType}'.");
                }

                var channelName = $"test-RSL4-fixture-{UtsSandbox.RandomId()}";
                var capturedRequests = new List<PendingHttpRequest>();
                var mockHttp = new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        capturedRequests.Add(req);
                        req.RespondWith(201, new { serials = new[] { "s1" } });
                    });

                var client = RestClient(mockHttp);
                var channel = client.Channels.Get(channelName);

                await channel.PublishAsync("event", inputData);

                capturedRequests.Should().HaveCount(1, describe);
                var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

                if (expectedEncoding == null)
                {
                    HasNoValue(body, "encoding").Should().BeTrue(describe + " carries no encoding");
                }
                else
                {
                    body["encoding"].Value<string>().Should().Be(expectedEncoding, describe);
                }

                if (expectedEncoding == "json")
                {
                    // Compared as JSON rather than as text: the fixture fixes the encoding, not the
                    // serialiser's whitespace or key order.
                    JToken.DeepEquals(
                            JToken.Parse(body["data"].Value<string>()),
                            JToken.Parse(expectedWireData))
                        .Should().BeTrue(describe);
                }
                else
                {
                    body["data"].Value<string>().Should().Be(expectedWireData, describe);
                }
            }
        }

        // UTS: rest/unit/RSL4/null-data-no-encoding-1
        [Fact]
        public async Task RSL4_NullDataNoEncoding()
        {
            var channelName = $"test-RSL4-null-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", null);

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            // Omitted rather than written as JSON null — see adaptation 2 in the class summary.
            HasNoValue(body, "data").Should().BeTrue("null is transmitted untransformed");
            HasNoValue(body, "encoding").Should().BeTrue();
        }

        // UTS: rest/unit/RSL4a/number-type-rejected-1
        [Fact]
        public async Task RSL4a_NumberTypeRejected()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(201, new { serials = new[] { "s1" } }));

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            Func<Task> act = () => channel.PublishAsync("event", 42);

            await act.Should().ThrowAsync<AblyException>();
            mockHttp.CapturedRequests.Should().BeEmpty(
                "the payload type is rejected before the request is built");
        }

        // UTS: rest/unit/RSL4a/boolean-type-rejected-2
        [Fact]
        public async Task RSL4a_BooleanTypeRejected()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(201, new { serials = new[] { "s1" } }));

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            Func<Task> act = () => channel.PublishAsync("event", true);

            await act.Should().ThrowAsync<AblyException>();
            mockHttp.CapturedRequests.Should().BeEmpty(
                "the payload type is rejected before the request is built");
        }

        // UTS: rest/unit/RSL6/decode-utf8-base64-data-2
        [Fact]
        public async Task RSL6_DecodeUtf8Base64Data()
        {
            var channelName = $"test-RSL6-utf8-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "SGVsbG8gV29ybGQ=", // base64 of UTF-8 "Hello World"
                            encoding = "utf-8/base64",
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            message.Data.Should().BeOfType<string>();
            message.Data.Should().Be("Hello World");
            message.Encoding.Should().BeNullOrEmpty();
        }

        // UTS: rest/unit/RSL6/complex-chained-encoding-3
        [Fact]
        public async Task RSL6_ComplexChainedEncoding()
        {
            var channelName = $"test-RSL6-complex-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();

            // Create data: object -> JSON -> UTF-8 bytes -> base64.
            var jsonString = "{\"status\":\"active\",\"count\":5}";
            var base64Data = Base64Of(jsonString);

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = base64Data,
                            encoding = "json/utf-8/base64",
                            timestamp = 1234567890000L,
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Decodes base64 -> utf-8 -> json.
            message.Data.Should().BeOfType<JObject>();
            JToken.DeepEquals((JToken)message.Data, JObject.Parse(jsonString)).Should().BeTrue();
            message.Encoding.Should().BeNullOrEmpty();
        }

        // UTS: rest/unit/RSL4/json-protocol-content-type-2
        [Fact]
        public async Task RSL4_JsonProtocolContentType()
        {
            var channelName = $"test-RSL4-json-ct-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            // useBinaryProtocol: false is this build's only setting; see adaptation 3.
            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", "test");

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Headers["Content-Type"].Should().Be("application/json");
            request.Headers["Accept"].Should().Be("application/json");
        }

        // UTS: rest/unit/RSL4/empty-string-no-encoding-4
        [Fact]
        public async Task RSL4_EmptyStringNoEncoding()
        {
            var channelName = $"test-RSL4-empty-str-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", string.Empty);

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["data"].Value<string>().Should().Be(string.Empty);
            HasNoValue(body, "encoding").Should().BeTrue();
        }

        // UTS: rest/unit/RSL4/empty-array-json-encoding-5
        [Fact]
        public async Task RSL4_EmptyArrayJsonEncoding()
        {
            var channelName = $"test-RSL4-empty-arr-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", Array.Empty<object>());

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["encoding"].Value<string>().Should().Be("json");
            JToken.DeepEquals(JToken.Parse(body["data"].Value<string>()), new JArray())
                .Should().BeTrue();
        }

        // UTS: rest/unit/RSL4 - Empty object encoding
        //
        // NOTE: this is the one scenario in the spec carrying no **Test ID** line, so the comment
        // above names its heading instead of an id.
        [Fact]
        public async Task RSL4_EmptyObjectJsonEncoding()
        {
            var channelName = $"test-RSL4-empty-obj-{UtsSandbox.RandomId()}";
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", new JObject());

            capturedRequests.Should().HaveCount(1);
            var body = (JObject)JArray.Parse(capturedRequests[0].BodyText)[0];

            body["encoding"].Value<string>().Should().Be("json");
            JToken.DeepEquals(JToken.Parse(body["data"].Value<string>()), new JObject())
                .Should().BeTrue();
        }

        /// <summary>
        /// The specs' "x" NOT IN body OR body["x"] IS null. JsonHelper serialises with
        /// NullValueHandling.Ignore, so a null payload field is omitted rather than written as JSON
        /// null, and both shapes have to count as "no value".
        /// </summary>
        /// <param name="body">the parsed wire message.</param>
        /// <param name="property">the property the spec expects to carry nothing.</param>
        /// <returns>true when the property is absent or JSON null.</returns>
        private static bool HasNoValue(JObject body, string property)
        {
            var token = body[property];
            return token == null || token.Type == JTokenType.Null;
        }

        /// <summary>The specs' base64_encode(encode_utf8(text)).</summary>
        /// <param name="text">the text to encode.</param>
        /// <returns>the base64 of the text's UTF-8 bytes.</returns>
        private static string Base64Of(string text) => Convert.ToBase64String(text.GetBytes());

        /// <summary>Reads the fixtures' expectedHexValue back into the bytes it describes.</summary>
        /// <param name="hex">an even-length run of hex digits.</param>
        /// <returns>the bytes the hex describes.</returns>
        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
