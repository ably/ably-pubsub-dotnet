using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/time.md in ably/specification.
    ///
    /// Spec points: RSC16
    /// </summary>
    public class TimeTests : UtsTestBase
    {
        private const long ServerTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

        public TimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC16/returns-server-time-0
        [Fact]
        public async Task RSC16_TimeReturnsServerTime()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            var client = RestClient(mockHttp);

            var result = await client.TimeAsync();

            // The spec's "result IS DateTime" is a static guarantee here: TimeAsync returns
            // DateTimeOffset, which is the idiomatic .NET rendering of "a DateTime or timestamp", so a
            // runtime type assertion cannot fail and would not compile against the typed assertion. The
            // value assertion is what carries the coverage.
            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Path.Should().Be("/time");
        }

        // UTS: rest/unit/RSC16/request-format-get-time-1
        [Fact]
        public async Task RSC16_TimeRequestFormat()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            var client = RestClient(mockHttp);

            await client.TimeAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Method.Should().Be("GET");
            request.Path.Should().Be("/time");

            request.Headers.Should().ContainKey("X-Ably-Version");
            request.Headers.Should().ContainKey("Ably-Agent");
        }

        // UTS: rest/unit/RSC16/no-auth-required-2
        [Fact]
        public async Task RSC16_TimeDoesNotRequireAuthentication()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            // The client has credentials; time() must not use them.
            var client = RestClient(mockHttp);

            var result = await client.TimeAsync();

            // "result IS DateTime" is static here — see RSC16_TimeReturnsServerTime. Asserting the value
            // instead keeps the "should succeed" half of the spec's assertion meaningful.
            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Headers.Should().NotContainKey("Authorization");
        }

        // UTS: rest/unit/RSC16/works-without-tls-3
        [Fact]
        public async Task RSC16_TimeWorksWithoutTls()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Tls = false;
                options.UseTokenAuth = true;
            });

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);

            capturedRequests.Should().HaveCount(
                1,
                "RSC18's basic-auth-over-plain-HTTP check must not apply to an unauthenticated " +
                "endpoint, and no token request should be made either");

            var request = capturedRequests[0];
            request.Url.Scheme.Should().Be("http");
            request.Headers.Should().NotContainKey("Authorization");
        }

        // UTS: rest/unit/RSC16/error-propagated-4
        [Fact]
        public async Task RSC16_TimeErrorHandling()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(500, new
                {
                    error = new
                    {
                        message = "Internal server error",
                        code = 50000,
                        statusCode = 500,
                    },
                }));

            var client = RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(500);
            error.Code.Should().Be(50000);
        }
    }
}
