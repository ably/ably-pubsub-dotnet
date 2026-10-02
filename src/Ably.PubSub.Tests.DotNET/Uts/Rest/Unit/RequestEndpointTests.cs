using System;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/request_endpoint.md in ably/specification.
    ///
    /// Spec points: RSC25
    ///
    /// Two notes that apply to the whole file:
    ///
    /// The spec stubs /time as <c>{"time": N}</c>. The endpoint returns an array and
    /// <c>TimeAsync()</c> indexes it, so every stub here is <c>[N]</c>.
    ///
    /// The spec's DEFAULT_REST_HOST is this SDK's <c>Defaults.RestHost</c> — see
    /// <see cref="DefaultRestHost"/>.
    /// </summary>
    public class RequestEndpointTests : UtsTestBase
    {
        /// <summary>
        /// The spec's DEFAULT_REST_HOST. This SDK predates REC1's endpoint/routing-policy model, so its
        /// primary domain is <c>Defaults.RestHost</c> rather than <c>main.realtime.ably.net</c>. Written
        /// as a literal rather than read from <c>Defaults</c> so that a change to the default host fails
        /// this test instead of silently following it.
        /// </summary>
        private const string DefaultRestHost = "rest.ably.io";

        private const long ServerTimeMs = 1234567890000;

        public RequestEndpointTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC25/default-primary-domain-0
        [Fact]
        public async Task RSC25_DefaultPrimaryDomain()
        {
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = RestClient(mockHttp);

            await client.TimeAsync();

            // This spec reads mock_http.captured_requests directly rather than keeping its own list.
            var capturedRequests = mockHttp.CapturedRequests;
            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.Host.Should().Be(DefaultRestHost);
        }

        // UTS: rest/unit/RSC25/multiple-requests-primary-domain-2
        [Fact]
        public async Task RSC25_MultipleRequestsPrimaryDomain()
        {
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = RestClient(mockHttp);

            await client.TimeAsync();
            await client.TimeAsync();
            await client.TimeAsync();

            var capturedRequests = mockHttp.CapturedRequests;
            capturedRequests.Should().HaveCount(3);
            foreach (var request in capturedRequests)
            {
                request.Url.Host.Should().Be(DefaultRestHost);
            }
        }

        // UTS: rest/unit/RSC25/primary-tried-before-fallback-3
        [Fact]
        public async Task RSC25_PrimaryTriedBeforeFallback()
        {
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onRequest: req =>
                {
                    requestCount++;
                    if (requestCount == 1)
                    {
                        req.RespondWith(500, new { error = new { code = 50000 } });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { ServerTimeMs });
                    }
                });

            // UtsClients.Options disables the fallback hosts by default, so that an injected 5xx in an
            // unrelated spec does not fan out into one request per host. This is the one test in the
            // file that is about the fallback, so the defaults are restored: null — not an empty array —
            // is what makes ClientOptions.GetFallbackHosts() return Defaults.FallbackHosts.
            var client = RestClient(mockHttp, configure: options => options.FallbackHosts = null);

            await client.TimeAsync();

            var capturedRequests = mockHttp.CapturedRequests;
            capturedRequests.Should().HaveCount(2);
            capturedRequests[0].Url.Host.Should().Be(DefaultRestHost);
            capturedRequests[1].Url.Host.Should().NotBe(DefaultRestHost);
        }

        // UTS: rest/unit/RSC25/request-path-preserved-4
        [Fact]
        public async Task RSC25_RequestPathPreserved()
        {
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, Array.Empty<object>()));

            var client = RestClient(mockHttp);

            await client.Channels.Get("test-channel").HistoryAsync();

            var capturedRequests = mockHttp.CapturedRequests;
            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];
            request.Url.Host.Should().Be(DefaultRestHost);
            request.Url.Path.Should().Be("/channels/test-channel/messages");
            request.Method.Should().Be("GET");
        }
    }
}
