// Request Endpoint Tests (RSC25)
// Derived from uts/rest/unit/request_endpoint.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: DEFAULT_REST_HOST is the const DefaultRestHost (main.realtime.ably.net)

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
    public class RequestEndpointTests : UtsUnitTestBase
    {
        private const string DefaultRestHost = "main.realtime.ably.net";

        public RequestEndpointTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC25/default-primary-domain-0
        [UtsFact]
        [Trait("spec", "RSC25")]
        public async Task RSC25_DefaultPrimaryDomain()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(1);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be(DefaultRestHost);
        }

        // UTS: rest/unit/RSC25/custom-endpoint-domain-1
        [UtsFact]
        [Trait("spec", "RSC25")]
        public async Task RSC25_CustomEndpointDomain()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "test",
            });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(1);
            mockHttp.CapturedRequests[0].Url.Host.Should().Be("test.realtime.ably.net");
        }

        // UTS: rest/unit/RSC25/multiple-requests-primary-domain-2
        [UtsFact]
        [Trait("spec", "RSC25")]
        public async Task RSC25_MultipleRequestsPrimaryDomain()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                    req.RespondWith(200, new[] { 1234567890000L });
                });

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();
            await client.TimeAsync();
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(3);
            foreach (var request in mockHttp.CapturedRequests)
            {
                request.Url.Host.Should().Be(DefaultRestHost);
            }
        }

        // UTS: rest/unit/RSC25/primary-tried-before-fallback-3
        [UtsFact]
        [Trait("spec", "RSC25")]
        public async Task RSC25_PrimaryTriedBeforeFallback()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
                    }
                    else
                    {
                        // NOTE: /time stub corrected to the array form RSC16/time.md use — see deviations.md (UTS Spec Errors, /time stub)
                        req.RespondWith(200, new[] { 1234567890000L });
                    }
                });

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.TimeAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(2);

            // First request was to primary domain
            mockHttp.CapturedRequests[0].Url.Host.Should().Be(DefaultRestHost);

            // Second request was to a fallback domain (not primary)
            mockHttp.CapturedRequests[1].Url.Host.Should().NotBe(DefaultRestHost);
        }

        // UTS: rest/unit/RSC25/request-path-preserved-4
        [UtsFact]
        [Trait("spec", "RSC25")]
        public async Task RSC25_RequestPathPreserved()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray());
                });

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Channels.Get("test-channel").HistoryAsync();

            // Assertions
            mockHttp.CapturedRequests.Count.Should().Be(1);
            var request = mockHttp.CapturedRequests[0];
            request.Url.Host.Should().Be(DefaultRestHost);
            request.Url.Path.Should().Be("/channels/test-channel/messages");
            request.Method.Should().Be("GET");
        }
    }
}
