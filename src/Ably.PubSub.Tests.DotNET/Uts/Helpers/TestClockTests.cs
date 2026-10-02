using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// Tests for the clock seam.
    ///
    /// The point of these is not that <c>TestClock</c> adds numbers correctly — it is that advancing it
    /// actually changes what the SDK does. A clock the SDK does not read would make every
    /// <c>ADVANCE_TIME</c> in the specs a no-op, and the tests built on it would pass without exercising
    /// the elapsing they are named for.
    /// </summary>
    public class TestClockTests
    {
        private const long ServerTimeMs = 1609459200000;

        public TestClockTests(ITestOutputHelper output)
        {
            Output = output;
        }

        private ITestOutputHelper Output { get; }

        [Fact]
        public void AdvanceMovesTheClockForwardOnly()
        {
            var clock = new TestClock(TestClock.FixedStart);

            clock.Advance(1500);

            clock.Now.Should().Be(TestClock.FixedStart.AddMilliseconds(1500));
            clock.Invoking(c => c.Advance(TimeSpan.FromSeconds(-1)))
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void NowFuncReadsTheCurrentValueEachTime()
        {
            var clock = new TestClock(TestClock.FixedStart);
            var read = clock.NowFunc;

            clock.Advance(1000);

            read().Should().Be(TestClock.FixedStart.AddSeconds(1));
        }

        [Fact]
        public async Task AdvancingPastFallbackRetryTimeoutSendsTheClientBackToThePrimaryHost()
        {
            // This is the seam proving itself. AblyHttpRequester stamps FallbackHostUsedFrom from
            // ClientOptions.NowFunc and compares it against NowFunc() on the next request
            // (Http/AblyHttpRequester.cs, GetHost()). Without the clock reaching that comparison, no
            // amount of advancing would move the client off its preferred fallback.
            var clock = new TestClock(TestClock.FixedStart);
            var hosts = new List<string>();
            var failPrimary = true;

            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                hosts.Add(req.Url.Host);
                var isPrimary = req.Url.Host == "rest.ably.io";
                req.RespondWith(
                    isPrimary && failPrimary ? 500 : 200,
                    new object[] { ServerTimeMs });
            });

            var client = UtsClients.RestClient(mockHttp, clock, options =>
            {
                options.FallbackHosts = new[] { "fallback.example.com" };
                options.FallbackRetryTimeout = TimeSpan.FromSeconds(60);
            });

            // First call: the primary fails, the fallback answers and becomes preferred.
            await client.TimeAsync();
            hosts.Should().Equal("rest.ably.io", "fallback.example.com");

            // Second call, with the cache still warm: straight to the remembered fallback.
            hosts.Clear();
            failPrimary = false;
            await client.TimeAsync();
            hosts.Should().Equal("fallback.example.com");

            // Advance past FallbackRetryTimeout and the preference expires.
            hosts.Clear();
            clock.Advance(TimeSpan.FromSeconds(61));
            await client.TimeAsync();
            hosts.Should().Equal(
                new[] { "rest.ably.io" },
                "the fallback preference should have expired once the clock passed FallbackRetryTimeout");
        }

        [Fact]
        public async Task AdvancingPastHttpMaxRetryDurationStopsTheRetryLoop()
        {
            // The other thing the SDK measures rather than schedules: EnsureMaxRetryDurationNotExceeded
            // compares Now() against the request's start time.
            var clock = new TestClock(TestClock.FixedStart);
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                req.RespondWith(500);
            });

            var client = UtsClients.RestClient(mockHttp, clock, options =>
            {
                options.FallbackHosts = new[] { "a.example.com", "b.example.com", "c.example.com" };
                options.HttpMaxRetryDuration = TimeSpan.FromSeconds(15);
            });

            Func<Task> act = () => client.TimeAsync();

            (await act.Should().ThrowAsync<AblyException>())
                .And.Message.Should().Contain("Cumulative retry timeout");
            mockHttp.CapturedRequests.Count.Should().BeLessThan(4);
        }
    }
}
