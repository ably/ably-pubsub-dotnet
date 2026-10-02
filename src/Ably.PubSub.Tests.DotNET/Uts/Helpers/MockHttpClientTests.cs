using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// Tests for the mock itself, not for the SDK.
    ///
    /// Every REST unit test stands on this mock, so a mock that quietly mis-models the SDK's retry
    /// predicate — or drops a header, or answers the wrong attempt — makes a whole tier pass while
    /// proving nothing, and the failure is invisible from the tests above it.
    /// </summary>
    public class MockHttpClientTests
    {
        private const long ServerTimeMs = 1609459200000;

        public MockHttpClientTests(ITestOutputHelper output)
        {
            Output = output;
        }

        private ITestOutputHelper Output { get; }

        [Fact]
        public async Task HandlerPattern_CapturesTheRequestAndAnswersIt()
        {
            var captured = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    captured.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            var client = UtsClients.RestClient(mockHttp);

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            captured.Should().HaveCount(1);
            captured[0].Path.Should().Be("/time");
            captured[0].Method.Should().Be("GET");
        }

        [Fact]
        public async Task AwaitPattern_HandsThePendingRequestToTheTest()
        {
            var mockHttp = new MockHttpClient();
            var client = UtsClients.RestClient(mockHttp);

            // The request is started eagerly in .NET — unlike a Python coroutine — but an attempt that
            // nothing answers is parked for the next AwaitRequest(), so either ordering works here.
            var pendingCall = client.TimeAsync();

            var request = await mockHttp.AwaitRequest();
            request.Headers.Should().ContainKey("X-Ably-Version");
            request.RespondWith(200, new object[] { ServerTimeMs });

            (await pendingCall).ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
        }

        [Fact]
        public async Task AwaitPattern_WaiterTakesPrecedenceOverTheHandler()
        {
            // The waiter has to be registered *before* the call that provokes the request. An SDK call in
            // .NET starts its request immediately, so registering afterwards races the handler and the
            // handler usually wins. This is the main ordering difference from the Python harness.
            var handlerCalls = 0;
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                handlerCalls++;
                req.RespondWith(500);
            });

            var client = UtsClients.RestClient(mockHttp);
            var pendingRequest = mockHttp.AwaitRequest();

            var pendingCall = client.TimeAsync();

            var request = await pendingRequest;
            request.RespondWith(200, new object[] { ServerTimeMs });

            await pendingCall;
            handlerCalls.Should().Be(0, "a registered waiter answers the attempt instead of the handler");
        }

        [Fact]
        public async Task TheSdkSeesTheHostOfEachAttempt()
        {
            // The whole point of seaming at ClientOptions.HttpClient rather than above
            // AblyHttpRequester.Execute: host selection has already happened by the time the mock is
            // reached, so a fallback is observable.
            var hosts = new List<string>();
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                hosts.Add(req.Url.Host);
                req.RespondWith(hosts.Count == 1 ? 500 : 200, new object[] { ServerTimeMs });
            });

            var client = UtsClients.RestClient(mockHttp, configure: options =>
                options.FallbackHosts = new[] { "fallback.example.com" });

            await client.TimeAsync();

            hosts.Should().HaveCount(2);
            hosts[0].Should().Be("rest.ably.io");
            hosts[1].Should().Be("fallback.example.com");
        }

        [Fact]
        public async Task ConnectionRefused_IsRetryableSoTheSdkFallsBack()
        {
            // AblyHttpRequester.IsRetryableError only recognises an HttpRequestException whose inner
            // exception is a WebException with one of a known set of statuses. A bare HttpRequestException
            // would make the SDK give up instead, and every connection-failure spec would be wrong.
            var attempts = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        conn.RespondWithRefused();
                    }
                    else
                    {
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = UtsClients.RestClient(mockHttp, configure: options =>
                options.FallbackHosts = new[] { "fallback.example.com" });

            await client.TimeAsync();

            attempts.Should().Be(2);
            mockHttp.CapturedRequests.Should().HaveCount(1, "a refused connection records no request");
        }

        [Fact]
        public async Task DnsError_IsAlsoRetryable()
        {
            var attempts = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        conn.RespondWithDnsError();
                    }
                    else
                    {
                        conn.RespondWithSuccess();
                    }
                },
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = UtsClients.RestClient(mockHttp, configure: options =>
                options.FallbackHosts = new[] { "fallback.example.com" });

            await client.TimeAsync();

            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ConnectionFailureWithNoFallback_SurfacesAsAnAblyException()
        {
            var mockHttp = new MockHttpClient(onConnectionAttempt: conn => conn.RespondWithRefused());
            var client = UtsClients.RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();
        }

        [Fact]
        public async Task QueuedResponses_AreConsumedInOrder()
        {
            var mockHttp = new MockHttpClient();
            mockHttp.QueueResponse(500);
            mockHttp.QueueResponse(200, new object[] { ServerTimeMs });

            var client = UtsClients.RestClient(mockHttp, configure: options =>
                options.FallbackHosts = new[] { "fallback.example.com" });

            await client.TimeAsync();

            mockHttp.CapturedRequests.Should().HaveCount(2);
        }

        [Fact]
        public async Task QueueResponseForHost_OnlyAnswersThatHost()
        {
            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));
            mockHttp.QueueResponseForHost("rest.ably.io", 500);

            var client = UtsClients.RestClient(mockHttp, configure: options =>
                options.FallbackHosts = new[] { "fallback.example.com" });

            await client.TimeAsync();

            var requests = mockHttp.CapturedRequests;
            requests.Should().HaveCount(2);
            requests[1].Url.Host.Should().Be("fallback.example.com");
        }

        [Fact]
        public async Task TheRequestBodyAndQueryParametersAreCaptured()
        {
            PendingHttpRequest captured = null;
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                captured = req;
                req.RespondWith(201, new object[0]);
            });

            var client = UtsClients.RestClient(mockHttp);
            await client.Channels.Get("uts-mock").PublishAsync("event", "payload");

            captured.Should().NotBeNull();
            captured.Method.Should().Be("POST");
            captured.Path.Should().Be("/channels/uts-mock/messages");
            captured.BodyText.Should().Contain("payload");
            captured.Headers.Should().ContainKey("Content-Type");
        }

        [Fact]
        public async Task QueryParametersAreParsedAndUrlDecoded()
        {
            PendingHttpRequest captured = null;
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                captured = req;
                req.RespondWith(200, new object[0], new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                });
            });

            var client = UtsClients.RestClient(mockHttp);
            await client.Channels.Get("uts mock").HistoryAsync(new PaginatedRequestParams { Limit = 25 });

            captured.Should().NotBeNull();
            captured.Url.QueryParams["limit"].Should().Be("25");
            captured.Path.Should().Contain("uts%20mock");
        }

        [Fact]
        public async Task HandlerExceptionsAreRecordedAndSurfaced()
        {
            var mockHttp = new MockHttpClient(onRequest: _ => throw new InvalidOperationException("boom"));
            var client = UtsClients.RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            (await act.Should().ThrowAsync<AblyException>()).And.Message.Should().Contain("boom");
            mockHttp.HandlerErrors.Should().ContainSingle().Which.Message.Should().Be("boom");
        }

        [Fact]
        public async Task AnUnansweredRequestFailsWithAnExplanationRatherThanHanging()
        {
            // A mock with no handler, no queue and no waiter would otherwise hang to the test timeout
            // with nothing to say.
            var mockHttp = new MockHttpClient();
            var client = UtsClients.RestClient(mockHttp);

            Func<Task> act = () => client.TimeAsync();

            (await act.Should().ThrowAsync<AblyException>())
                .And.Message.Should().Contain("nothing answered");
        }

        [Fact]
        public void ResetClearsTheTimelineButKeepsTheHandlers()
        {
            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200));
            mockHttp.QueueResponse(500);

            mockHttp.Reset();

            mockHttp.CapturedRequests.Should().BeEmpty();
            mockHttp.ConnectionAttempts.Should().BeEmpty();
            mockHttp.OnRequest.Should().NotBeNull();
        }

        [Fact]
        public async Task TimestampsComeFromTheInjectedClock()
        {
            var clock = new TestClock(TestClock.FixedStart);
            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = UtsClients.RestClient(mockHttp, clock);
            await client.TimeAsync();

            mockHttp.CapturedRequests.Single().Timestamp.Should().Be(TestClock.FixedStart);
        }
    }
}
