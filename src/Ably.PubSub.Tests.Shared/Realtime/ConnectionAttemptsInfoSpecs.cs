using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    public class ConnectionAttemptsInfoSpecs : MockHttpRealtimeSpecs
    {
        private readonly RealtimeState _state = new RealtimeState();

        private ConnectionAttemptsInfo Info => _state.AttemptsInfo;

        [Fact]
        public void Reset_ShouldResetFirstAttemptAndNumberOfAttempts()
        {
            Info.Reset();
            Info.FirstAttempt.Should().NotHaveValue();
            Info.NumberOfAttempts.Should().Be(0);
        }

        [Fact]
        public void ShouldSuspend_WhenConnectionHasNotBeenAttempted_ShouldReturnFalse()
        {
            _state.ShouldSuspend().Should().BeFalse();
        }

        [Fact]
        public void ShouldSuspend_WhenFirstAttemptIsWithinConnectionStateTtl_ShouldReturnFalse()
        {
            SetNowFunc(() => DateTimeOffset.UtcNow);
            Info.Attempts.Add(new ConnectionAttempt(Now));

            // Move now to default ConnectionStateTtl - 1 second
            NowAdd(Defaults.ConnectionStateTtl.Add(TimeSpan.FromSeconds(-1)));
            _state.ShouldSuspend().Should().BeFalse();
            SetNowFunc(() => DateTimeOffset.UtcNow);
        }

        [Fact]
        public void ShouldSuspend_WhenFirstAttemptEqualOrGreaterThanConnectionStateTtl_ShouldReturnTrue()
        {
            var now = new Now();
            var state = new RealtimeState(null, now.ValueFn);

            state.AttemptsInfo.Attempts.Add(new ConnectionAttempt(now.Value));

            // Move now to default ConnectionStateTtl - 1 second
            now.Reset(DateTimeOffset.UtcNow.Add(Defaults.ConnectionStateTtl));
            state.ShouldSuspend(now.ValueFn).Should().BeTrue("When time is equal"); // =
            now.Reset(DateTimeOffset.UtcNow.Add(Defaults.ConnectionStateTtl).AddSeconds(60));
            state.ShouldSuspend(now.ValueFn).Should().BeTrue("When time is greater than"); // >
        }

        [Theory]
        [InlineData(500)]
        [InlineData(501)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(504)]
        public async Task CanAttemptFallback_WithDefaultHostAndAppropriateError_ShouldBeTrue(int httpCode)
        {
            var client = GetRealtime();

            var result = await client.RestClient.CanFallback(new ErrorInfo("test", 111, (HttpStatusCode)httpCode));
            result.Should().BeTrue();
        }

        [Fact]
        public async Task CanAttemptFallback_WhenInternetCheckFails_ShouldBeFalse()
        {
            var client = GetRealtime(internetCheckOk: false);
            var result = await client.RestClient.CanFallback(ErrorInfo.ReasonUnknown);
            result.Should().BeFalse();
        }

        public ConnectionAttemptsInfoSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private AblyRealtime GetRealtime(Action<ClientOptions> optionsAction = null, bool internetCheckOk = true)
        {
            return GetRealtimeClient(
                request =>
            {
                if (request.Url == Defaults.InternetCheckUrl)
                {
                    return (internetCheckOk ? Defaults.InternetCheckOkMessage : "Blah").ToAblyResponse();
                }

                return DefaultResponse.ToTask();
            }, optionsAction);
        }
    }
}
