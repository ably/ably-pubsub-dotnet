// REST Client request() Tests (RSC19, RSC19b, RSC19c, RSC19d, RSC19e, RSC19f, RSC19f1, HP1, HP3, HP4, HP5, HP6, HP7, HP8)
// Derived from uts/rest/unit/request.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: fake timers drive the spec's real-time delay/timeout (infra-driving: EnableFakeTimers + AdvanceTime in RSC19e/timeout-error-handling-1)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    /// <summary>
    /// The uts/rest/unit/request.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="RequestTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: RealtimeRequestTests (realtime/unit/RTC9/request-proxies-rest-0).
    /// </summary>
    public abstract class RequestTestsBase : UtsUnitTestBase
    {
        protected RequestTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the REST client whose <c>Request</c> is under test (for RTC9, the realtime client's own REST client).</summary>
        protected abstract PubSubHttpClient NewClient(ClientOptions options);

        // UTS: rest/unit/RSC19f/supports-http-methods-0
        [UtsFact]
        [Trait("spec", "RSC19f")]
        public async Task RSC19f_SupportsHttpMethods()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            // Test Cases
            var testCases = new[]
            {
                new { Id = 1, Method = "GET", Path = "/test" },
                new { Id = 2, Method = "POST", Path = "/test" },
                new { Id = 3, Method = "PUT", Path = "/test" },
                new { Id = 4, Method = "PATCH", Path = "/test" },
                new { Id = 5, Method = "DELETE", Path = "/test" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();

                var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

                // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
                var response = await client.Request(
                    new HttpMethod(testCase.Method),
                    testCase.Path,
                    headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

                capturedRequests.Count.Should().Be(1, "test case {0}", testCase.Id);
                var request = capturedRequests[0];
                request.Method.Should().Be(testCase.Method);
                request.Url.Path.Should().Be(testCase.Path);
            }
        }

        // UTS: rest/unit/RSC19f/query-params-passed-1
        [UtsFact]
        [Trait("spec", "RSC19f")]
        public async Task RSC19f_QueryParamsPassed()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/channels/test/messages",
                new Dictionary<string, string> { ["limit"] = "10", ["direction"] = "backwards" },
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Url.QueryParams["limit"].Should().Be("10");
            request.Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSC19f/custom-headers-passed-2
        [UtsFact]
        [Trait("spec", "RSC19f")]
        public async Task RSC19f_CustomHeadersPassed()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string>
                {
                    ["X-Ably-Version"] = "3",
                    ["X-Custom-Header"] = "custom-value",
                    ["X-Another"] = "another-value",
                });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Headers["X-Custom-Header"].Should().Be("custom-value");
            request.Headers["X-Another"].Should().Be("another-value");
        }

        // UTS: rest/unit/RSC19f/request-body-sent-3
        [UtsFact]
        [Trait("spec", "RSC19f")]
        public async Task RSC19f_RequestBodySent()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["id"] = "123" });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false, // JSON for easier inspection
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Post,
                "/channels/test/messages",
                body: new JObject { ["name"] = "event", ["data"] = "payload" },
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            var body = JObject.Parse(request.BodyText);
            ((string)body["name"]).Should().Be("event");
            ((string)body["data"]).Should().Be("payload");
        }

        // UTS: rest/unit/RSC19f1/version-param-sets-header-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RSC19f1")]
        public async Task RSC19f1_VersionParamSetsHeader()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(200, new JArray());

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Cases
            var testCases = new[]
            {
                new { Id = 1, Version = 2, ExpectedHeader = "2" },
                new { Id = 2, Version = 3, ExpectedHeader = "3" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                mockHttp.Reset();
                mockHttp.QueueResponse(200, new JArray());

                // NOTE: the version argument has no SDK equivalent: sent as a request-level X-Ably-Version header, which should take precedence over the client default (RSC7e; rest-mapping.md section 8)
                var response = await client.Request(
                    HttpMethod.Get,
                    "/test",
                    headers: new Dictionary<string, string> { ["X-Ably-Version"] = testCase.Version.ToString() });

                var request = mockHttp.CapturedRequests[0];
                request.Headers["X-Ably-Version"].Should().Be(testCase.ExpectedHeader, "test case {0}", testCase.Id);
            }
        }

        // UTS: rest/unit/RSC19b/uses-configured-auth-0
        [UtsFact]
        [Trait("spec", "RSC19b")]
        public async Task RSC19b_UsesConfiguredAuth()
        {
            // Test Case 1: Basic authentication (API key)

            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Headers.Should().ContainKey("Authorization");
            request.Headers["Authorization"].Should().StartWith("Basic ");

            // Verify the base64 encoded credentials
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers["Authorization"].Substring(6)));
            credentials.Should().Be("appId.keyId:keySecret");

            // Test Case 2: Token authentication

            // Setup
            var capturedRequestsToken = new CaptureList<PendingHttpRequest>();

            var mockHttpToken = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequestsToken.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttpToken);

            var clientToken = NewClient(new ClientOptions { Token = "my-token-string" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var responseToken = await clientToken.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            capturedRequestsToken.Count.Should().Be(1);
            var requestToken = capturedRequestsToken[0];
            requestToken.Headers.Should().ContainKey("Authorization");
            requestToken.Headers["Authorization"].Should().StartWith("Bearer ");
        }

        // UTS: rest/unit/RSC19c/protocol-headers-json-0
        [UtsFact]
        [Trait("spec", "RSC19c")]
        public async Task RSC19c_ProtocolHeadersJson()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(200, new JArray());

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false, // JSON
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Post,
                "/test",
                body: new JObject { ["data"] = "test" },
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            var request = mockHttp.CapturedRequests[0];
            request.Headers["Accept"].Should().Be("application/json");
            request.Headers["Content-Type"].Should().Be("application/json");
        }

        // UTS-NA: rest/unit/RSC19c/protocol-headers-msgpack-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSC19c/body-encoded-per-protocol-2
        [UtsFact]
        [Trait("spec", "RSC19c")]
        public async Task RSC19c_BodyEncodedPerProtocol()
        {
            // Test Case 1: JSON encoding

            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(200, new JArray());

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false,
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Post,
                "/test",
                body: new JObject { ["name"] = "event", ["data"] = new JObject { ["nested"] = "value" } },
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            var request = mockHttp.CapturedRequests[0];

            // Body should be valid JSON
            var body = JObject.Parse(request.BodyText);
            ((string)body["name"]).Should().Be("event");
            ((string)body["data"]["nested"]).Should().Be("value");

            // NOTE: Test Case 2 (MsgPack encoding) not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
            // mock_http = MockHttpClient()
            // mock_http.queue_response(200, msgpack_encode([]))
            // client = Rest(options: ClientOptions(key: "appId.keyId:keySecret", useBinaryProtocol: true))
            // response = AWAIT client.request("POST", "/test", version: 3, body: { "name": "event", "data": "value" })
            // request = mock_http.captured_requests[0]
            // # Body should be valid MsgPack
            // body = msgpack_decode(request.body)
            // ASSERT body["name"] == "event"
            // ASSERT body["data"] == "value"
        }

        // UTS: rest/unit/RSC19c/response-decoded-by-content-type-3
        [UtsFact]
        [Trait("spec", "RSC19c")]
        public async Task RSC19c_ResponseDecodedByContentType()
        {
            // Test Case 1: JSON response

            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                200,
                JsonConvert.SerializeObject(new[]
                {
                    new { id = "1", name = "item1" },
                    new { id = "2", name = "item2" },
                }),
                new Dictionary<string, string> { ["Content-Type"] = "application/json" });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });
            var items = response.Items;

            // Assertions
            items.Count.Should().Be(2);
            ((string)items[0]["id"]).Should().Be("1");
            ((string)items[1]["name"]).Should().Be("item2");

            // NOTE: Test Case 2 (MsgPack response) not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
            // mock_http = MockHttpClient()
            // mock_http.queue_response(200, body: msgpack_encode([{ "id": "1" }]), headers: { "Content-Type": "application/x-msgpack" })
            // client = Rest(options: ClientOptions(key: "appId.keyId:keySecret"))
            // response = AWAIT client.request("GET", "/test", version: 3)
            // items = response.items()
            // ASSERT items.length == 1
            // ASSERT items[0]["id"] == "1"
        }

        // UTS: rest/unit/RSC19d/response-status-code-0
        [UtsFact(Skip = "UTS spec error RSC19d: a single stubbed response is retried on fallback hosts that have no response (RSC15l3); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC19d")]
        public Task RSC19d_ResponseStatusCode()
        {
            // SPEC ERROR RSC19d: the 500 case queues one response and leaves the default fallback hosts enabled, so the 500 is retried on a fallback host (RSC15l3) that the mock never answers.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC19d — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC19d/response-success-indicator-1
        [UtsFact(Skip = "UTS spec error RSC19d: a single stubbed response is retried on fallback hosts that have no response (RSC15l3); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC19d")]
        public Task RSC19d_ResponseSuccessIndicator()
        {
            // SPEC ERROR RSC19d: the 500 case queues one response and leaves the default fallback hosts enabled, so the 500 is retried on a fallback host (RSC15l3) that the mock never answers.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC19d — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC19d/response-error-code-header-2
        [UtsFact]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_ResponseErrorCodeHeader()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                401,
                new JObject { ["error"] = new JObject { ["code"] = 40101, ["message"] = "Unauthorized" } },
                new Dictionary<string, string> { ["X-Ably-Errorcode"] = "40101" });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            response.ErrorCode.Should().Be(40101);
        }

        // UTS: rest/unit/RSC19d/response-error-message-header-3
        // DEVIATION: see deviations.md
        [DeviationFact("HP7", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_ResponseErrorMessageHeader()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                401,
                new JObject { ["error"] = new JObject { ["code"] = 40101, ["message"] = "Unauthorized" } },
                new Dictionary<string, string>
                {
                    ["X-Ably-Errorcode"] = "40101",
                    ["X-Ably-Errormessage"] = "Token expired",
                });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            response.ErrorMessage.Should().Be("Token expired");
        }

        // UTS: rest/unit/RSC19d/response-headers-accessible-4
        // DEVIATION: see deviations.md
        [DeviationFact("HP8", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_ResponseHeadersAccessible()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                200,
                new JArray(),
                new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                    ["X-Request-Id"] = "req-123",
                    ["X-Custom-Header"] = "custom-value",
                });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            var headers = response.Headers;
            headers.GetValues("Content-Type").First().Should().Be("application/json");
            headers.GetValues("X-Request-Id").First().Should().Be("req-123");
            headers.GetValues("X-Custom-Header").First().Should().Be("custom-value");
        }

        // UTS: rest/unit/RSC19d/response-items-decoded-5
        [UtsFact]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_ResponseItemsDecoded()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                200,
                new JArray
                {
                    new JObject { ["id"] = "msg1", ["name"] = "event1", ["data"] = "data1" },
                    new JObject { ["id"] = "msg2", ["name"] = "event2", ["data"] = "data2" },
                });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/channels/test/messages",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });
            var items = response.Items;

            // Assertions
            items.Count.Should().Be(2);
            ((string)items[0]["id"]).Should().Be("msg1");
            ((string)items[1]["id"]).Should().Be("msg2");
        }

        // UTS: rest/unit/RSC19d/pagination-with-link-headers-6
        [UtsFact]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_PaginationWithLinkHeaders()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // First page
            mockHttp.QueueResponse(
                200,
                new JArray { new JObject { ["id"] = "1" }, new JObject { ["id"] = "2" } },
                new Dictionary<string, string> { ["Link"] = "</channels/test/messages?page=2>; rel=\"next\"" });

            // Second page
            mockHttp.QueueResponse(
                200,
                new JArray { new JObject { ["id"] = "3" } },
                new Dictionary<string, string>()); // No "next" link - last page

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/channels/test/messages",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            // First page
            var items1 = response.Items;
            items1.Count.Should().Be(2);
            response.HasNext.Should().BeTrue();

            // Navigate to second page
            response = await response.NextAsync();
            var items2 = response.Items;
            items2.Count.Should().Be(1);
            ((string)items2[0]["id"]).Should().Be("3");
            response.HasNext.Should().BeFalse();
        }

        // UTS: rest/unit/RSC19d/non-array-response-handling-7
        [UtsFact]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_NonArrayResponseHandling()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(200, new JObject { ["time"] = 1234567890000L });

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/time",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });
            var items = response.Items;

            // Assertions
            // Non-array response should be accessible
            // NOTE: Items is a List<JToken>, so the alternative items["time"] cannot apply: only the wrapped-as-single-item branch is asserted
            // ASSERT items.length == 1 OR items["time"] == 1234567890000
            items.Count.Should().Be(1);

            // Implementation may vary - either wrap in array or return object directly
        }

        // UTS: rest/unit/RSC19e/network-error-propagated-0
        [UtsFact]
        [Trait("spec", "RSC19e")]
        public async Task RSC19e_NetworkErrorPropagated()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithRefused());
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackHosts = new string[0], // Disable fallback for this test
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            Func<Task> act = () => client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // NOTE: CONTAINS is read as case-insensitive (the SDK message says "Connection refused" / "HttpClient.Timeout")
            (error.Code == 80000 || (error.Message != null && error.Message.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0) || (error.Message != null && error.Message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0)).Should().BeTrue(
                "error was code {0}, message {1}",
                error.Code,
                error.Message);
        }

        // UTS: rest/unit/RSC19e/timeout-error-handling-1
        [UtsFact]
        [Trait("spec", "RSC19e")]
        public async Task RSC19e_TimeoutErrorHandling()
        {
            // Setup
            // NOTE: fake timers drive the spec's real-time delay/timeout: the HTTP timeout is enforced on fake time
            EnableFakeTimers();
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueDelayedResponse(
                TimeSpan.FromMilliseconds(5000), // 5 second delay
                200,
                new JArray());

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                HttpRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second timeout
                FallbackHosts = new string[0], // Disable fallback
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var requestTask = client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // NOTE: infra-driving: advance fake time past httpRequestTimeout (the delay is 5000 ms)
            await AdvanceTime(1500);

            Func<Task> act = () => requestTask;
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // NOTE: CONTAINS is read as case-insensitive (the SDK message says "Connection refused" / "HttpClient.Timeout")
            (error.Code == 50003 || (error.Message != null && error.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)).Should().BeTrue(
                "error was code {0}, message {1}",
                error.Code,
                error.Message);
        }

        // UTS: rest/unit/RSC19e/http-error-no-fallback-2
        [UtsFact]
        [Trait("spec", "RSC19e")]
        public async Task RSC19e_HttpErrorNoFallback()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                400,
                new JObject { ["error"] = new JObject { ["code"] = 40000, ["message"] = "Bad request" } },
                new Dictionary<string, string> { ["X-Ably-Errorcode"] = "40000" });

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackHosts = new[] { "a.ably-realtime.com", "b.ably-realtime.com" },
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            // Should return the error response, not retry to fallback
            ((int)response.StatusCode).Should().Be(400);
            response.Success.Should().BeFalse();
            response.ErrorCode.Should().Be(40000);

            // Only one request should have been made (no fallback)
            mockHttp.CapturedRequests.Count.Should().Be(1);
        }

        // UTS: rest/unit/RSC19e/fallback-on-server-error-3
        [UtsFact]
        [Trait("spec", "RSC19e")]
        public async Task RSC19e_FallbackOnServerError()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // Primary host fails with non-Ably 500 error
            mockHttp.QueueResponse(
                500,
                "Internal Server Error",
                new Dictionary<string, string> { ["Content-Type"] = "text/plain" });

            // Fallback succeeds
            mockHttp.QueueResponse(200, new JArray { new JObject { ["id"] = "1" } });

            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                FallbackHosts = new[] { "fallback.ably-realtime.com" },
            });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            ((int)response.StatusCode).Should().Be(200);
            response.Success.Should().BeTrue();

            // Two requests: primary failed, fallback succeeded
            mockHttp.CapturedRequests.Count.Should().Be(2);
            mockHttp.CapturedRequests[1].Url.Host.Should().Be("fallback.ably-realtime.com");
        }

        // UTS: rest/unit/RSC19b/cannot-override-auth-1
        [UtsFact]
        [Trait("spec", "RSC19b")]
        public async Task RSC19b_CannotOverrideAuth()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(200, new JArray());

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Attempt to override auth with custom header
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Get,
                "/test",
                headers: new Dictionary<string, string>
                {
                    ["X-Ably-Version"] = "3",
                    ["Authorization"] = "Bearer malicious-token",
                });

            // Assertions
            var request = mockHttp.CapturedRequests[0];

            // The configured Basic auth should be used, not the custom header
            request.Headers["Authorization"].Should().StartWith("Basic ");

            // Should NOT contain the attempted override
            request.Headers["Authorization"].Should().NotBe("Bearer malicious-token");

            // Note
            // This behavior may vary by implementation. Some libraries may allow header override while others enforce configured auth. The spec states authentication is "unconditional" per RSC19b.
        }

        // UTS: rest/unit/RSC19f/path-leading-slash-handling-4
        // DEVIATION: see deviations.md
        [DeviationFact("RSC19f", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC19f")]
        public async Task RSC19f_PathLeadingSlashHandling()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Cases
            var testCases = new[]
            {
                new { Id = 1, Path = "/channels/test", ExpectedPath = "/channels/test" },
                new { Id = 2, Path = "channels/test", ExpectedPath = "/channels/test" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                mockHttp.Reset();
                mockHttp.QueueResponse(200, new JArray());

                // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
                var response = await client.Request(
                    HttpMethod.Get,
                    testCase.Path,
                    headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

                var request = mockHttp.CapturedRequests[0];
                request.Url.Path.Should().Be(testCase.ExpectedPath, "test case {0}", testCase.Id);
            }
        }

        // UTS: rest/unit/RSC19d/empty-response-handling-8
        [UtsFact]
        [Trait("spec", "RSC19d")]
        public async Task RSC19d_EmptyResponseHandling()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(
                204,
                null, // No body
                new Dictionary<string, string>());

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // NOTE: version: 3 has no SDK argument: sent as a request-level X-Ably-Version header (rest-mapping.md section 8)
            var response = await client.Request(
                HttpMethod.Delete,
                "/channels/test/messages/123",
                headers: new Dictionary<string, string> { ["X-Ably-Version"] = "3" });

            // Assertions
            ((int)response.StatusCode).Should().Be(204);
            response.Success.Should().BeTrue();
            var items = response.Items;
            (items == null || items.Count == 0).Should().BeTrue();
        }
    }

    /// <summary>The uts/rest/unit/request.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class RequestTests : RequestTestsBase
    {
        public RequestTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override PubSubHttpClient NewClient(ClientOptions options) => Rest(options);
    }
}
