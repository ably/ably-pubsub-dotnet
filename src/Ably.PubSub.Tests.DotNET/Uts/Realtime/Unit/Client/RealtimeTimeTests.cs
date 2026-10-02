using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Client
{
    /// <summary>
    /// Derived from uts/realtime/unit/client/realtime_time.md in ably/specification.
    ///
    /// Spec points: RTC6, RTC6a
    ///
    /// The spec file carries a single Test ID — <c>realtime/unit/RTC6/time-proxies-rest-0</c> — and no
    /// test body of its own. It delegates: "The tests in <c>uts/rest/unit/time.md</c> (covering RSC16)
    /// should be used to test a <c>RealtimeClient</c> instance in place of a <c>RestClient</c>
    /// instance. All the same behaviour, parameters, and return types apply." So each of RSC16's five
    /// scenarios is re-derived here against <c>PubSubRealtimeClient.TimeAsync()</c>, and every test
    /// names both its realtime Test ID and the REST test it was taken from.
    ///
    /// The REST derivation these mirror is Uts/Rest/Unit/TimeTests.cs; the assertions are deliberately
    /// identical, because the whole subject of RTC6 is that nothing changes when the call is made on a
    /// realtime client.
    ///
    /// Every client here is built on both seams — <c>RealtimeClient(mockWs, mockHttp)</c> — because
    /// <c>TimeAsync()</c> is a REST call made from a realtime client. The WebSocket mock is installed
    /// but never exercised: the harness defaults <c>AutoConnect</c> to false, so no connection attempt
    /// is made and <c>/time</c> is the only traffic in the test.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimeTimeTests : UtsTestBase
    {
        private const long ServerTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

        public RealtimeTimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0 (rest/unit/RSC16/returns-server-time-0)
        [Fact]
        public async Task RTC6_TimeReturnsServerTime()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, new object[] { ServerTimeMs });
            var mockWs = new MockWebSocket();

            var client = RealtimeClient(mockWs, mockHttp);

            var result = await client.TimeAsync();

            // "result IS DateTime" is a static guarantee here: TimeAsync returns DateTimeOffset, which
            // is the idiomatic .NET rendering of "a DateTime or timestamp", so a runtime type assertion
            // cannot fail. The value assertion is what carries the coverage.
            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Path.Should().Be("/time");
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0 (rest/unit/RSC16/request-format-get-time-1)
        [Fact]
        public async Task RTC6_TimeRequestFormat()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, new object[] { ServerTimeMs });
            var mockWs = new MockWebSocket();

            var client = RealtimeClient(mockWs, mockHttp);

            await client.TimeAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Method.Should().Be("GET");
            request.Path.Should().Be("/time");

            request.Headers.Should().ContainKey("X-Ably-Version");
            request.Headers.Should().ContainKey("Ably-Agent");
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0 (rest/unit/RSC16/no-auth-required-2)
        [Fact]
        public async Task RTC6_TimeDoesNotRequireAuthentication()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, new object[] { ServerTimeMs });
            var mockWs = new MockWebSocket();

            // The client has credentials; time() must not use them.
            var client = RealtimeClient(mockWs, mockHttp);

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Headers.Should().NotContainKey("Authorization");
        }

        // UTS: realtime/unit/RTC6/time-proxies-rest-0 (rest/unit/RSC16/works-without-tls-3)
        [Fact]
        public async Task RTC6_TimeWorksWithoutTls()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = CapturingMock(capturedRequests, new object[] { ServerTimeMs });
            var mockWs = new MockWebSocket();

            var client = RealtimeClient(mockWs, mockHttp, configure: options =>
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

        // UTS: realtime/unit/RTC6/time-proxies-rest-0 (rest/unit/RSC16/error-propagated-4)
        [Fact]
        public async Task RTC6_TimeErrorHandling()
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

            var mockWs = new MockWebSocket();

            var client = RealtimeClient(mockWs, mockHttp);

            Func<Task> act = () => client.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(500);
            error.Code.Should().Be(50000);
        }

        /// <summary>
        /// The specs' <c>MockHttpClient(onConnectionAttempt: ..., onRequest: ...)</c>, which every test in
        /// time.md sets up identically: succeed the connection, record the request, answer it 200 with
        /// <paramref name="body"/>.
        /// </summary>
        private static MockHttpClient CapturingMock(List<PendingHttpRequest> capturedRequests, object body)
        {
            return new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, body);
                });
        }
    }
}
