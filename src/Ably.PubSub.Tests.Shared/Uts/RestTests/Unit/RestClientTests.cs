// REST Client Tests (RSC5, RSC7, RSC7b, RSC7c, RSC7d, RSC7e, RSC8, RSC8a, RSC8b, RSC8c, RSC8d, RSC8e, RSC13, RSC17, RSC18)
// Derived from uts/rest/unit/rest_client.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    public class RestClientTests : UtsUnitTestBase
    {
        public RestClientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC5/auth-attribute-accessible-0
        [Fact]
        [Trait("spec", "RSC5")]
        public void RSC5_AuthAttributeAccessible()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            // Assertions
            client.Auth.Should().NotBeNull();
            client.Auth.Should().BeAssignableTo<IAblyAuth>();
        }

        // UTS: rest/unit/RSC7e/ably-version-header-0
        [UtsFact]
        [Trait("spec", "RSC7e")]
        public async Task RSC7e_AblyVersionHeader()
        {
            // Setup
            PendingHttpRequest capturedRequest = null;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            capturedRequest.Should().NotBeNull();
            capturedRequest.Headers.Should().ContainKey("X-Ably-Version");
            capturedRequest.Headers["X-Ably-Version"].Should().MatchRegex("[0-9.]+");
        }

        // UTS: rest/unit/RSC7d/ably-agent-header-format-0
        [UtsFact]
        [Trait("spec", "RSC7d")]
        public async Task RSC7d_AblyAgentHeaderFormat()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            var request = mockHttp.CapturedRequests[0];
            request.Headers.Should().ContainKey("Ably-Agent");

            var agent = request.Headers["Ably-Agent"];

            // Format: key[/value] entries joined by spaces
            // Must include at least library name/version
            // NOTE: RSC7d1 permits any library name; the SDK's is ably-pubsub-dotnet. The UTS spec asserts ably-[a-z]+ — see deviations.md (UTS Spec Errors, RSC7d1)
            agent.Should().MatchRegex(@"ably-[a-z-]+/[0-9]+\.[0-9]+\.[0-9]+");

            // May include additional entries like platform info
        }

        // UTS: rest/unit/RSC7c/request-id-included-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC7c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC7c")]
        public async Task RSC7c_RequestIdIncluded()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AddRequestIds = true,
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            var request = mockHttp.CapturedRequests[0];
            request.Url.QueryParams.Should().ContainKey("request_id");

            var requestId = request.Url.QueryParams["request_id"];

            // Should be url-safe base64 encoded, at least 12 characters (9 bytes base64)
            requestId.Length.Should().BeGreaterThanOrEqualTo(12);
            requestId.Should().MatchRegex("[A-Za-z0-9_-]+");
        }

        // UTS: rest/unit/RSC7c/request-id-preserved-fallback-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSC7c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC7c")]
        public async Task RSC7c_RequestIdPreservedFallback()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // First request fails with 500 (triggers fallback retry)
            mockHttp.QueueResponse(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });

            // Retry succeeds
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AddRequestIds = true,
                FallbackHosts = new[] { "a.example.com", "b.example.com" },
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);

            var requestId1 = mockHttp.CapturedRequests[0].Url.QueryParams["request_id"];
            var requestId2 = mockHttp.CapturedRequests[1].Url.QueryParams["request_id"];

            requestId1.Should().Be(requestId2); // Same ID for retry
        }

        // UTS: rest/unit/RSC8a/protocol-selection-0
        [UtsFact]
        [Trait("spec", "RSC8a")]
        public async Task RSC8a_ProtocolSelection()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // Test Cases
            // NOTE: row 1 (useBinaryProtocol true (default), application/x-msgpack) not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
            var testCases = new[]
            {
                new { Id = 2, UseBinaryProtocol = false, ExpectedContentType = "application/json" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                mockHttp.Reset();
                mockHttp.QueueResponse(201, new JObject { ["serials"] = new JArray { "s1" } });

                var client = Rest(new ClientOptions
                {
                    Key = "appId.keyId:keySecret",
                    UseBinaryProtocol = testCase.UseBinaryProtocol,
                });

                await client.Channels.Get("test").PublishAsync("e", "d");

                var request = mockHttp.CapturedRequests[0];
                request.Headers["Content-Type"].Should().Be(testCase.ExpectedContentType, "test case {0}", testCase.Id);
                request.Headers["Accept"].Should().Be(testCase.ExpectedContentType, "test case {0}", testCase.Id);
            }
        }

        // UTS: rest/unit/RSC8c/accept-content-type-headers-0
        [UtsFact]
        [Trait("spec", "RSC8c")]
        public async Task RSC8c_AcceptContentTypeHeaders()
        {
            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            mockHttp.QueueResponse(201, new JObject { ["serials"] = new JArray { "s1" } });

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                UseBinaryProtocol = false, // JSON for easier inspection
            });

            // Test Steps
            await client.Channels.Get("test").PublishAsync("e", "d");

            // Assertions
            var request = mockHttp.CapturedRequests[0];
            request.Headers["Accept"].Should().Be("application/json");
            request.Headers["Content-Type"].Should().Be("application/json");
        }

        // UTS-NA: rest/unit/RSC8d/mismatched-response-content-type-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSC8e/unsupported-content-type-0
        [UtsFact(Skip = "UTS spec error RSC8e: a single stubbed response is retried on fallback hosts that have no response (RSC15l3); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC8e")]
        public Task RSC8e_UnsupportedContentType()
        {
            // SPEC ERROR RSC8e: Case 1 queues one 500 response and leaves the default fallback hosts enabled, so the 500 is retried on a fallback host (RSC15l3) that the mock never answers.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC8e — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS-NA: rest/unit/RSC8/error-decoded-from-msgpack-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: rest/unit/RSC13/request-timeout-enforced-0
        [UtsFact(Skip = "UTS spec error RSC13: a single stubbed response is retried on fallback hosts that have no response (RSC15l2); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSC13")]
        public Task RSC13_RequestTimeoutEnforced()
        {
            // SPEC ERROR RSC13: the test leaves the default fallback hosts enabled, so the request timeout triggers a retry on a fallback host (RSC15l2) that the mock never answers, and the error is the mock's unanswered-request failure, not the timeout.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSC13 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC17/client-id-from-options-0
        [UtsFact]
        [Trait("spec", "RSC17")]
        public void RSC17_ClientIdFromOptions()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "explicit-client-id",
            });

            // Assertions
            // NOTE: client.clientId is asserted as client.Auth.ClientId: RSC17 requires Auth#clientId, and the IDL defines no RestClient#clientId (only RealtimeClient#clientId, RTC17); see deviations.md (UTS Spec Errors, RSC17)
            client.Auth.ClientId.Should().Be("explicit-client-id");

            // NOTE: no assertion: with no RestClient#clientId in the IDL, client.clientId and client.auth.clientId are the same member, so this line has nothing to compare; see deviations.md (UTS Spec Errors, RSC17)
            // ASSERT client.clientId == client.auth.clientId
        }

        // UTS: rest/unit/RSC17/client-id-matches-auth-1
        [UtsFact]
        [Trait("spec", "RSC17")]
        public void RSC17_ClientIdMatchesAuth()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "explicit-client-id",
            });

            // Assertions
            // NOTE: client.clientId is asserted as client.Auth.ClientId: RSC17 requires Auth#clientId, and the IDL defines no RestClient#clientId (only RealtimeClient#clientId, RTC17); see deviations.md (UTS Spec Errors, RSC17)
            client.Auth.ClientId.Should().Be("explicit-client-id");

            // NOTE: no assertion: with no RestClient#clientId in the IDL, client.clientId and client.auth.clientId are the same member, so this line has nothing to compare; see deviations.md (UTS Spec Errors, RSC17)
            // ASSERT client.clientId == client.auth.clientId
        }

        // UTS: rest/unit/RSC18/tls-controls-protocol-scheme-0
        [UtsFact]
        [Trait("spec", "RSC18")]
        public async Task RSC18_TlsControlsProtocolScheme()
        {
            // Test Cases
            var testCases = new[]
            {
                new { Id = 1, Tls = true, ExpectedScheme = "https" },
                new { Id = 2, Tls = false, ExpectedScheme = "http" },
            };

            // Setup
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            // Test Steps
            foreach (var testCase in testCases)
            {
                mockHttp.Reset();
                mockHttp.QueueResponse(200, new[] { 1234567890000L });

                var client = Rest(new ClientOptions
                {
                    Key = "appId.keyId:keySecret",
                    Tls = testCase.Tls,
                });

                await client.TimeAsync();

                var request = mockHttp.CapturedRequests[0];
                request.Url.Scheme.Should().Be(testCase.ExpectedScheme, "test case {0}", testCase.Id);
            }
        }

        // UTS: rest/unit/RSC18/basic-auth-over-http-rejected-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSC18", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC18")]
        public async Task RSC18_BasicAuthOverHttpRejected()
        {
            // Setup
            // No mock needed - should fail before making request

            // Test Steps
            // NOTE: RSC18 doesn't say when the error is raised; the SDK raises it on the first authenticated request, not at construction. The UTS spec expects Rest(...) itself to throw — see deviations.md (UTS Spec Errors, RSC18)
            var clientBasic = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Tls = false,
            });
            Func<Task> create = () => clientBasic.StatsAsync();
            var error = (await create.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            (error.Code == 40103 || (error.Message != null && error.Message.Contains("insecure")) || (error.Message != null && error.Message.Contains("TLS"))).Should().BeTrue(
                "error was code {0}, message {1}",
                error.Code,
                error.Message);

            // Note
            // Token auth over HTTP should be allowed. Only Basic auth (API key) should be rejected.

            // Additional Test - Token auth over HTTP allowed
            var mockHttp = new MockHttpClient();

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);
            // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
            mockHttp.QueueResponse(200, new[] { 1234567890000L });

            var client = Rest(new ClientOptions
            {
                Token = "some-token-string",
                Tls = false,
            });

            var result = await client.TimeAsync();

            // Should succeed - token auth over HTTP is permitted
            // NOTE: ASSERT result IS valid is read as: the call returned a real (non-default) time
            result.Should().NotBe(default(DateTimeOffset));
        }
    }
}
