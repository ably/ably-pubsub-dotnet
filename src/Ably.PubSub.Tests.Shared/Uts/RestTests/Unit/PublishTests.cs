// REST Channel Publish Tests (RSL1, RSL1a, RSL1b, RSL1c, RSL1d, RSL1e, RSL1h, RSL1i, RSL1j, RSL1l, RSL1m)
// Derived from uts/rest/unit/channel/publish.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Http;
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
    public class PublishTests : UtsUnitTestBase
    {
        public PublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL1a/publish-name-and-data-0
        [UtsFact]
        [Trait("spec", "RSL1a")]
        public async Task RSL1a_PublishNameAndData()
        {
            // Setup
            var channelName = "test-RSL1a-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "serial1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("greeting", "hello");

            // Assertions
            var request = capturedRequests[0];

            // RSL1b - single message published
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages");

            var body = JToken.Parse(request.BodyText);
            body.Type.Should().Be(JTokenType.Array);

            // NOTE: Some SDKs send a single message as a plain JSON object rather than
            // wrapping it in an array. The Ably API accepts both formats. SDKs MAY send
            // a single message as either an object or a single-element array.
            body.Should().HaveCount(1);
            ((string)body[0]["name"]).Should().Be("greeting");
            ((string)body[0]["data"]).Should().Be("hello");
        }

        // UTS: rest/unit/RSL1a/publish-message-array-1
        [UtsFact]
        [Trait("spec", "RSL1a")]
        public async Task RSL1a_PublishMessageArray()
        {
            // Setup
            var channelName = "test-RSL1c-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1", "s2", "s3" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var messages = new[]
            {
                new Message("event1", "data1"),
                new Message("event2", new JObject { ["key"] = "value" }),
                new Message("event3", new byte[] { 0x01, 0x02, 0x03 }),
            };
            await channel.PublishAsync(messages);

            // Assertions
            // RSL1c - single request for array
            requestCount.Should().Be(1);

            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText);

            body.Count.Should().Be(3);
            ((string)body[0]["name"]).Should().Be("event1");
            ((string)body[0]["data"]).Should().Be("data1");
            ((string)body[1]["name"]).Should().Be("event2");

            // NOTE: RSL4d3 requires a JSON object payload to be stringified with encoding "json"; the SDK does so. The UTS spec asserts the raw object - see deviations.md (UTS Spec Errors, RSL4d3)
            JToken.DeepEquals(JToken.Parse((string)body[1]["data"]), new JObject { ["key"] = "value" }).Should().BeTrue("parse_json(body[1][\"data\"]) == {{ \"key\": \"value\" }} (found {0})", body[1]["data"]);

            // Note: binary data encoding tested separately in encoding tests
        }

        // UTS: rest/unit/RSL1e/null-name-and-data-0
        [UtsFact]
        [Trait("spec", "RSL1e")]
        public async Task RSL1e_NullNameAndData()
        {
            // Setup
            var channelName = "test-RSL1e-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Cases
            var testCases = new[]
            {
                new { Name = (string)null, Data = (object)"hello", ExpectedBody = new JObject { ["data"] = "hello" } },
                new { Name = "event", Data = (object)null, ExpectedBody = new JObject { ["name"] = "event" } },
                new { Name = (string)null, Data = (object)null, ExpectedBody = new JObject() },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();

                await channel.PublishAsync(testCase.Name, testCase.Data);

                var body = JArray.Parse(capturedRequests[0].BodyText);

                // NOTE: RSL1k1 makes the SDK add a generated id (idempotentRestPublishing defaults to true); the UTS spec's expected body has none - see deviations.md (UTS Spec Errors, RSL1k1)
                ((JObject)body[0]).Remove("id");
                JToken.DeepEquals(body, new JArray { testCase.ExpectedBody }).Should().BeTrue("body == [test_case.expected_body] (found {0})", body);
                if (testCase.Name == null)
                {
                    ((JObject)body[0]).Properties().Should().NotContain(p => p.Name == "name");
                }

                if (testCase.Data == null)
                {
                    ((JObject)body[0]).Properties().Should().NotContain(p => p.Name == "data");
                }
            }
        }

        // UTS: rest/unit/RSL1h/publish-signature-0
        [UtsFact]
        [Trait("spec", "RSL1h")]
        public async Task RSL1h_PublishSignature()
        {
            // Setup
            var channelName = "test-RSL1h-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // This is a compile-time/type-system test in strongly-typed languages
            // The API should accept exactly (name, data) with no extras
            await channel.PublishAsync("event", "payload");

            // If language allows, verify that extra positional args are rejected at compile time
            // NOTE: PublishAsync(string name, object data, string clientId = null) has an optional third parameter; the (name, data) form is the one exercised here

            // Assertions
            requestCount.Should().Be(1);
            var body = JArray.Parse(capturedRequests[0].BodyText);
            ((string)body[0]["name"]).Should().Be("event");
            ((string)body[0]["data"]).Should().Be("payload");
        }

        // UTS: rest/unit/RSL1i/message-size-limit-0
        [UtsFact(Skip = "Missing SDK public API: ClientOptions#maxMessageSize (RSL1i); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSL1i")]
        public Task RSL1i_MessageSizeLimit()
        {
            // NOTE: missing SDK API: the SDK has no maxMessageSize client option and no pre-transmission size check
            // (error 40009); ClientOptions has no such member, so the test cannot be expressed.
            // Setup
            // channel_name = "test-RSL1i-${random_id()}"
            // captured_requests = []
            // request_count = 0
            //
            // mock_http = MockHttpClient(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(),
            //   onRequest: (req) => {
            //     captured_requests.append(req)
            //     request_count = request_count + 1
            //     req.respond_with(201, { "serials": ["s1"] })
            //   }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   maxMessageSize: 1024  # 1KB limit for testing
            // ))
            // channel = client.channels.get(channel_name)
            //
            // Test Cases
            // | ID | Message size | Expected |
            // | 1 | 1000 bytes | Success (under limit) |
            // | 2 | 1024 bytes | Success (at limit) |
            // | 3 | 1025 bytes | Error 40009 |
            // | 4 | 10000 bytes | Error 40009 |
            //
            // Test Steps
            // FOR EACH test_case IN test_cases:
            //   captured_requests = []
            //   request_count = 0
            //
            //   large_data = "x" * test_case.size
            //
            //   IF test_case.expected == "Success":
            //     AWAIT channel.publish(name: "event", data: large_data)
            //     ASSERT request_count == 1
            //   ELSE:
            //     ASSERT channel.publish(name: "event", data: large_data) THROWS AblyException WITH:
            //       code == 40009
            //     ASSERT request_count == 0  # Request never sent
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSL1j/all-attributes-transmitted-0
        [UtsFact]
        [Trait("spec", "RSL1j")]
        public async Task RSL1j_AllAttributesTransmitted()
        {
            // Setup
            var channelName = "test-RSL1j-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var message = new Message(
                name: "test-event",
                data: "test-data",
                clientId: "explicit-client-id", // RSL1m tests cover whether this should be sent
                extras: new MessageExtras(new JObject
                {
                    ["push"] = new JObject
                    {
                        ["notification"] = new JObject { ["title"] = "Test" },
                    },
                }))
            {
                Id = "custom-message-id",
            };

            await channel.PublishAsync(message);

            // Assertions
            var body = JArray.Parse(capturedRequests[0].BodyText)[0];

            ((string)body["name"]).Should().Be("test-event");
            ((string)body["data"]).Should().Be("test-data");
            ((string)body["id"]).Should().Be("custom-message-id");
            ((string)body["extras"]["push"]["notification"]["title"]).Should().Be("Test");

            // clientId handling is tested separately in RSL1m tests
        }

        // UTS: rest/unit/RSL1l/params-as-querystring-0
        [UtsFact(Skip = "Missing SDK public API: RestChannel#publish(params) (RSL1l); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSL1l")]
        public Task RSL1l_ParamsAsQuerystring()
        {
            // NOTE: missing SDK API: no PublishAsync overload accepts publish params (RSL1l), so the query string
            // parameters cannot be passed.
            // Setup
            // channel_name = "test-RSL1l-${random_id()}"
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(),
            //   onRequest: (req) => {
            //     captured_requests.append(req)
            //     req.respond_with(201, { "serials": ["s1"] })
            //   }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyId:keySecret"))
            // channel = client.channels.get(channel_name)
            //
            // Test Steps
            // params = {
            //   "customParam": "customValue",
            //   "anotherParam": "123"
            // }
            //
            // AWAIT channel.publish(
            //   message: Message(name: "event", data: "data"),
            //   params: params
            // )
            //
            // Assertions
            // request = captured_requests[0]
            //
            // ASSERT request.url.query_params["customParam"] == "customValue"
            // ASSERT request.url.query_params["anotherParam"] == "123"
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSL1m/clientid-not-injected-0
        [UtsFact]
        [Trait("spec", "RSL1m")]
        public async Task RSL1m_ClientidNotInjected()
        {
            // Setup
            var channelName = "test-RSL1m-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "library-client-id",
            });
            var channel = client.Channels.Get(channelName);

            // Test Cases (RSL1m1-RSL1m3)
            // | ID | Spec | Message clientId | Library clientId | Expected in request |
            // | RSL1m1 | Message with no clientId, library has clientId | null | "lib-client" | clientId absent |
            // | RSL1m2 | Message clientId matches library clientId | "lib-client" | "lib-client" | "lib-client" |
            // | RSL1m3 | Unidentified client, message has clientId | "msg-client" | null | "msg-client" |

            // Test Steps
            var channelNameM1 = "test-RSL1m1-" + Guid.NewGuid().ToString("N");
            var channelNameM2 = "test-RSL1m2-" + Guid.NewGuid().ToString("N");
            var channelNameM3 = "test-RSL1m3-" + Guid.NewGuid().ToString("N");

            // RSL1m1 - Message with no clientId
            capturedRequests.Clear();

            var clientWithId = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "lib-client",
            });
            await clientWithId.Channels.Get(channelNameM1).PublishAsync("e", "d");

            var body = JArray.Parse(capturedRequests[0].BodyText)[0];
            ((JObject)body).Properties().Should().NotContain(p => p.Name == "clientId"); // Library should not inject its clientId

            // RSL1m2 - Message clientId matches library
            capturedRequests.Clear();

            await clientWithId.Channels.Get(channelNameM2).PublishAsync(new Message("e", "d", "lib-client"));

            body = JArray.Parse(capturedRequests[0].BodyText)[0];
            ((string)body["clientId"]).Should().Be("lib-client"); // Explicit clientId preserved

            // RSL1m3 - Unidentified client with message clientId
            capturedRequests.Clear();

            var clientNoId = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            await clientNoId.Channels.Get(channelNameM3).PublishAsync(new Message("e", "d", "msg-client"));

            body = JArray.Parse(capturedRequests[0].BodyText)[0];
            ((string)body["clientId"]).Should().Be("msg-client");

            // Note
            // RSL1m4 (clientId mismatch rejection) requires an integration test as the server performs the validation.
        }
    }
}
