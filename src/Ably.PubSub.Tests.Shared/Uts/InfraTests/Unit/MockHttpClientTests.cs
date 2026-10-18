using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>Self-tests of <see cref="MockHttpClient"/> against <c>mock_http.md</c>.</summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class MockHttpClientTests : UtsUnitTestBase
    {
        public MockHttpClientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static ClientOptions NoFallbacks() => new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] };

        [Fact]
        public async Task ConnectionLevelFailureRecordsAnAttemptButNoRequest()
        {
            var mock = new MockHttpClient(onConnectionAttempt: connection => connection.RespondWithRefused());
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            Func<Task> act = () => rest.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();
            mock.ConnectionAttempts.Should().ContainSingle().Which.Host.Should().Be("main.realtime.ably.net");
            mock.CapturedRequests.Should().BeEmpty("a refused connection never sends the request");
        }

        [Fact]
        public async Task RequestLevelFailureRecordsTheRequest()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(
                500,
                new { error = new { code = 50000, statusCode = 500, message = "server error for the test" } }));
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            Func<Task> act = () => rest.TimeAsync();

            (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo.Code.Should().Be(50000);
            mock.ConnectionAttempts.Should().ContainSingle();
            mock.CapturedRequests.Should().ContainSingle().Which.Path.Should().Be("/time");
        }

        [Fact]
        public async Task DnsErrorIsAConnectionLevelFailure()
        {
            var mock = new MockHttpClient(onConnectionAttempt: connection => connection.RespondWithDnsError());
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            Func<Task> act = () => rest.TimeAsync();

            await act.Should().ThrowAsync<AblyException>();
            mock.ConnectionAttempts.Should().ContainSingle();
            mock.CapturedRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task AwaitStyleAnswersARequestTheTestAwaitsAfterStartingTheCall()
        {
            var mock = new MockHttpClient();
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            var call = rest.TimeAsync();
            var request = await mock.AwaitRequest();
            request.Path.Should().Be("/time");
            call.IsCompleted.Should().BeFalse("the request waits for the test's answer");
            request.RespondWith(200, new[] { 1234L });

            (await call).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234L));
        }

        [UtsFact]
        public async Task ARequestTheTestJustAnsweredIsBusyUntilTheSdkHasTheResponse()
        {
            // F4: the answer is a RunContinuationsAsynchronously completion, so the mock's own continuation runs
            // later on the pool. Until it has handed the response back, the request must not read as parked.
            var mock = new MockHttpClient();
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            for (var i = 0; i < 20; i++)
            {
                var call = rest.TimeAsync();
                var request = await mock.AwaitRequest();
                await UtsWait.PollUntil(() => mock.IsIdle, description: "the unanswered request to be parked");

                request.RespondWith(200, new[] { 1234L });
                var idle = mock.IsIdleWithInFlight(out var inFlight);

                (idle && inFlight > 0).Should().BeFalse("an answered request still in flight is busy, not parked (iteration {0})", i);
                (await call).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234L));
            }

            mock.IsIdle.Should().BeTrue();
        }

        [Fact]
        public async Task AwaitStyleAnswersAConnectionAttempt()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(200, new[] { 1L }));
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            var waiting = mock.AwaitConnectionAttempt();
            var call = rest.TimeAsync();
            var connection = await waiting;
            connection.Host.Should().Be("main.realtime.ably.net");
            connection.Port.Should().Be(443);
            connection.Tls.Should().BeTrue();
            connection.RespondWithSuccess();

            (await call).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1L));
        }

        [Fact]
        public async Task WaiterRegisteredBeforeTheRequestTakesPrecedenceOverTheHandler()
        {
            var handlerCalls = 0;
            var mock = new MockHttpClient(onRequest: request =>
            {
                Interlocked.Increment(ref handlerCalls);
                request.RespondWith(200, new[] { 2L });
            });
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            var waiting = mock.AwaitRequest();
            var first = rest.TimeAsync();
            var awaited = await waiting;
            handlerCalls.Should().Be(0, "the waiter took the request");
            awaited.RespondWith(200, new[] { 1L });
            (await first).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1L));

            (await rest.TimeAsync()).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(2L), "with no waiter the handler answers");
            handlerCalls.Should().Be(1);
        }

        [Fact]
        public async Task RequestTheHandlerLeavesUnansweredIsParkedForTheNextAwait()
        {
            PendingHttpRequest seenByHandler = null;
            var mock = new MockHttpClient(onRequest: request => seenByHandler = request);
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            var call = rest.TimeAsync();
            await UtsWait.PollUntil(() => seenByHandler != null);
            var parked = await mock.AwaitRequest();

            parked.Should().BeSameAs(seenByHandler);
            parked.RespondWith(200, new[] { 3L });
            (await call).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(3L));
        }

        [Fact]
        public async Task LegacyQueuedResponsesAnswerRequestsInOrderAndAreCaptured()
        {
            var mock = new MockHttpClient();
            mock.QueueResponse(200, new[] { 10L });
            mock.QueueResponse(200, new[] { 20L });
            mock.QueueResponseForHost("other.example.test", 200, new[] { 99L });
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            (await rest.TimeAsync()).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(10L));
            (await rest.TimeAsync()).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(20L));

            mock.CapturedRequests.Should().HaveCount(2);
            mock.CapturedRequests.Select(r => r.Path).Should().Equal("/time", "/time");
        }

        [Fact]
        public async Task LegacyQueuedResponseForHostSkipsOtherHosts()
        {
            var mock = new MockHttpClient();
            mock.QueueResponseForHost("main.realtime.ably.net", 200, new[] { 7L });
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            (await rest.TimeAsync()).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(7L));
        }

        [Fact]
        public async Task HttpRequestTimeoutRunsOnTheFakeClock()
        {
            EnableFakeTimers();
            var mock = new MockHttpClient(onRequest: request => request.RespondWithTimeout());
            InstallMock(mock);
            var options = NoFallbacks();
            options.HttpRequestTimeout = TimeSpan.FromSeconds(5);
            var rest = Rest(options);

            var call = rest.TimeAsync();
            await UtsWait.PollUntil(() => mock.CapturedRequests.Count == 1);
            await ProcessPendingEvents();
            await AdvanceTime(4900);
            call.IsCompleted.Should().BeFalse("HttpRequestTimeout (5 s) hasn't passed on the fake clock");

            await AdvanceTime(200);

            Func<Task> act = () => UtsWait.WithTimeout(call, TimeSpan.FromSeconds(5), "the request to time out");
            await act.Should().ThrowAsync<AblyException>();
        }

        [Fact]
        public async Task RespondWithDelayRunsOnTheFakeClock()
        {
            EnableFakeTimers();
            var mock = new MockHttpClient(onRequest: request => request.RespondWithDelay(2000, 200, new[] { 5L }));
            InstallMock(mock);
            var rest = Rest(NoFallbacks());

            var call = rest.TimeAsync();
            await UtsWait.PollUntil(() => mock.CapturedRequests.Count == 1);
            await AdvanceTime(1900);
            call.IsCompleted.Should().BeFalse("the 2 s delay is on the fake clock");

            await AdvanceTime(200);

            (await UtsWait.WithTimeout(call, TimeSpan.FromSeconds(5), "the delayed response")).Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(5L));
        }

        [Fact]
        public void StrictDefaultIsInstalledWhenATestInstallsNoMock()
        {
            Rest(new ClientOptions { Key = ValidKey });

            DefaultHttp.Should().NotBeNull();
            DefaultHttp.IsStrictDefault.Should().BeTrue();
            UnexpectedRequests.Should().BeEmpty();
        }
    }
}
