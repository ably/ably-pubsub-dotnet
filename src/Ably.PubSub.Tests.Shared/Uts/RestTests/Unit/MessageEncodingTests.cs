// Message Encoding Tests (RSL4, RSL4a, RSL4b, RSL4c, RSL4d, RSL6, RSL6a, RSL6b)
// Derived from uts/rest/unit/encoding/message_encoding.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class MessageEncodingTests : UtsUnitTestBase
    {
        public MessageEncodingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL4a/string-data-no-encoding-0
        [UtsFact]
        [Trait("spec", "RSL4a")]
        public async Task RSL4a_StringDataNoEncoding()
        {
            // Setup
            var channelName = "test-RSL4a-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false, // Use JSON for easier inspection
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", "plain string data");

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["data"]).Should().Be("plain string data");
            (body["encoding"] == null || body["encoding"].Type == JTokenType.Null).Should().BeTrue();
        }

        // UTS: rest/unit/RSL4b/json-object-encoding-0
        [UtsFact]
        [Trait("spec", "RSL4b")]
        public async Task RSL4b_JsonObjectEncoding()
        {
            // Setup
            var channelName = "test-RSL4b-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", new JObject
            {
                ["key"] = "value",
                ["nested"] = new JObject { ["a"] = 1 },
            });

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            // Data should be JSON-serialized string
            body["data"].Type.Should().Be(JTokenType.String);
            JToken.DeepEquals(
                JToken.Parse((string)body["data"]),
                new JObject
                {
                    ["key"] = "value",
                    ["nested"] = new JObject { ["a"] = 1 },
                }).Should().BeTrue();
            ((string)body["encoding"]).Should().Be("json");
        }

        // UTS: rest/unit/RSL4c/binary-base64-json-protocol-0
        [UtsFact]
        [Trait("spec", "RSL4c")]
        public async Task RSL4c_BinaryBase64JsonProtocol()
        {
            // Setup
            var channelName = "test-RSL4c-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false, // JSON protocol requires base64 for binary
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
            await channel.PublishAsync("event", binaryData);

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["encoding"]).Should().Be("base64");
            Convert.FromBase64String((string)body["data"]).Should().Equal(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE });
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS-NA: rest/unit/RSL4c/binary-direct-msgpack-protocol-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSL4d/array-json-encoding-0
        [UtsFact]
        [Trait("spec", "RSL4d")]
        public async Task RSL4d_ArrayJsonEncoding()
        {
            // Setup
            var channelName = "test-RSL4d-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", new JArray(1, 2, "three", new JObject { ["four"] = 4 }));

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["encoding"]).Should().Be("json");
            JToken.DeepEquals(
                JToken.Parse((string)body["data"]),
                new JArray(1, 2, "three", new JObject { ["four"] = 4 })).Should().BeTrue();
        }

        // UTS: rest/unit/RSL6a/decode-base64-to-binary-0
        [UtsFact]
        [Trait("spec", "RSL6a")]
        public async Task RSL6a_DecodeBase64ToBinary()
        {
            // Setup
            var channelName = "test-RSL6a-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "AAECAwQ=", // base64 of [0, 1, 2, 3, 4]
                            encoding = "base64",
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            ((byte[])message.Data).Should().Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

            // Encoding consumed after decode
            // DEVIATION: the SDK leaves an empty string, not null, once every encoding is consumed - see deviations.md
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/RSL6a/decode-json-to-object-1
        [UtsFact]
        [Trait("spec", "RSL6a")]
        public async Task RSL6a_DecodeJsonToObject()
        {
            // Setup
            var channelName = "test-RSL6a-json-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "{\"key\":\"value\",\"number\":42}",
                            encoding = "json",
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            JToken.DeepEquals(
                message.Data as JToken,
                new JObject
                {
                    ["key"] = "value",
                    ["number"] = 42,
                }).Should().BeTrue();
            // DEVIATION: the SDK leaves an empty string, not null, once every encoding is consumed - see deviations.md
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/RSL6a/decode-chained-encodings-2
        // DEVIATION: see deviations.md
        [DeviationFact("RSL6a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSL6a")]
        public async Task RSL6a_DecodeChainedEncodings()
        {
            // Setup
            var channelName = "test-RSL6a-chained-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            // Data: {"key":"value"} -> JSON string -> base64 encoded
            var jsonString = "{\"key\":\"value\"}";
            var base64OfJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = base64OfJson,
                            encoding = "json/base64", // Decode base64 first, then JSON
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            JToken.DeepEquals(message.Data as JToken, new JObject { ["key"] = "value" }).Should().BeTrue();
            message.Encoding.Should().BeNull();
        }

        // UTS: rest/unit/RSL6b/unrecognized-encoding-preserved-0
        [UtsFact]
        [Trait("spec", "RSL6b")]
        public async Task RSL6b_UnrecognizedEncodingPreserved()
        {
            // Setup
            var channelName = "test-RSL6b-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "encrypted-data-here",
                            encoding = "custom-encryption/base64",
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            // base64 should be decoded, but custom-encryption is unrecognized
            message.Encoding.Should().Be("custom-encryption");

            // Data should be base64-decoded but not further processed
            // Result of base64 decode
            message.Data.Should().BeOfType<byte[]>();
        }

        // UTS-NA: rest/unit/RSL6/msgpack-binary-stays-binary-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS-NA: rest/unit/RSL6/msgpack-string-stays-string-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSL4/encoding-fixtures-ably-common-0
        [UtsFact(Skip = "UTS spec error RSL4: encoding.json does not exist in ably-common; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSL4")]
        public Task RSL4_EncodingFixturesAblyCommon()
        {
            // SPEC ERROR RSL4: load_fixtures("encoding.json") names a fixture file that exists nowhere in ably-common.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSL4 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSL4/null-data-no-encoding-1
        [UtsFact]
        [Trait("spec", "RSL4")]
        public async Task RSL4_NullDataNoEncoding()
        {
            // Setup
            var channelName = "test-RSL4-null-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", null);

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            (body["data"] == null || body["data"].Type == JTokenType.Null).Should().BeTrue();
            (body["encoding"] == null || body["encoding"].Type == JTokenType.Null).Should().BeTrue();
        }

        // UTS: rest/unit/RSL4a/number-type-rejected-1
        [UtsFact]
        [Trait("spec", "RSL4a")]
        public async Task RSL4a_NumberTypeRejected()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get("test");

            // Test Steps
            Func<Task> act = () => channel.PublishAsync("event", 42);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
        }

        // UTS: rest/unit/RSL4a/boolean-type-rejected-2
        [UtsFact]
        [Trait("spec", "RSL4a")]
        public async Task RSL4a_BooleanTypeRejected()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get("test");

            // Test Steps
            Func<Task> act = () => channel.PublishAsync("event", true);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
        }

        // UTS: rest/unit/RSL6/decode-utf8-base64-data-2
        [UtsFact]
        [Trait("spec", "RSL6")]
        public async Task RSL6_DecodeUtf8Base64Data()
        {
            // Setup
            var channelName = "test-RSL6-utf8-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = "SGVsbG8gV29ybGQ=", // base64 of UTF-8 "Hello World"
                            encoding = "utf-8/base64",
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            message.Data.Should().Be("Hello World");
            message.Data.Should().BeOfType<string>();
            // DEVIATION: the SDK leaves an empty string, not null, once every encoding is consumed - see deviations.md
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/RSL6/complex-chained-encoding-3
        [UtsFact]
        [Trait("spec", "RSL6")]
        public async Task RSL6_ComplexChainedEncoding()
        {
            // Setup
            var channelName = "test-RSL6-complex-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            // Create data: object -> JSON -> UTF-8 bytes -> base64
            var originalObject = new JObject { ["status"] = "active", ["count"] = 5 };
            var jsonString = JsonConvert.SerializeObject(originalObject);
            var utf8Bytes = Encoding.UTF8.GetBytes(jsonString);
            var base64Data = Convert.ToBase64String(utf8Bytes);

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new
                        {
                            id = "msg1",
                            name = "event",
                            data = base64Data,
                            encoding = "json/utf-8/base64",
                            timestamp = 1234567890000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();
            var message = history.Items[0];

            // Assertions
            // Should decode: base64 -> utf-8 -> json
            JToken.DeepEquals(
                message.Data as JToken,
                new JObject { ["status"] = "active", ["count"] = 5 }).Should().BeTrue();
            // DEVIATION: the SDK leaves an empty string, not null, once every encoding is consumed - see deviations.md
            message.Encoding.Should().BeEmpty();
        }

        // UTS: rest/unit/RSL4/json-protocol-content-type-2
        [UtsFact]
        [Trait("spec", "RSL4")]
        public async Task RSL4_JsonProtocolContentType()
        {
            // Setup
            var channelName = "test-RSL4-json-ct-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", "test");

            // Assertions
            var request = capturedRequests[0];
            request.Headers["Content-Type"].Should().Be("application/json");
            request.Headers["Accept"].Should().Be("application/json");
        }

        // UTS-NA: rest/unit/RSL4/msgpack-protocol-content-type-3 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSL4/empty-string-no-encoding-4
        [UtsFact]
        [Trait("spec", "RSL4")]
        public async Task RSL4_EmptyStringNoEncoding()
        {
            // Setup
            var channelName = "test-RSL4-empty-str-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", string.Empty);

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["data"]).Should().Be(string.Empty);
            (body["encoding"] == null || body["encoding"].Type == JTokenType.Null).Should().BeTrue();
        }

        // UTS: rest/unit/RSL4/empty-array-json-encoding-5
        [UtsFact]
        [Trait("spec", "RSL4")]
        public async Task RSL4_EmptyArrayJsonEncoding()
        {
            // Setup
            var channelName = "test-RSL4-empty-arr-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", new JArray());

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["encoding"]).Should().Be("json");
            JToken.DeepEquals(JToken.Parse((string)body["data"]), new JArray()).Should().BeTrue();
        }

        // NOTE: no Test ID in the spec: RSL4 - Empty object encoding
        [UtsFact]
        [Trait("spec", "RSL4")]
        public async Task NoId_RSL4EmptyObjectEncoding()
        {
            // Setup
            var channelName = "test-RSL4-empty-obj-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", new JObject());

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            ((string)body["encoding"]).Should().Be("json");
            JToken.DeepEquals(JToken.Parse((string)body["data"]), new JObject()).Should().BeTrue();
        }
    }
}
