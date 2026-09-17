using System;
using System.Linq;
using FluentAssertions;
using IO.Ably.Shared.Utils;
using Xunit;

namespace IO.Ably.Tests.Shared.Utils
{
    public class ReconnectionStrategyTest
    {
        [Fact]
        [Trait("spec", "RTB1")]
        public void ShouldCalculateRetryTimeoutsUsingBackOffAndJitter()
        {
            var retryAttempts = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var initialTimeoutValue = 15;

            var retryTimeouts = retryAttempts.Select(attempt =>
                ReconnectionStrategy.GetRetryTime(initialTimeoutValue, attempt)).ToList();

            retryTimeouts.Distinct().Count().Should().Be(10);
            retryTimeouts.FindAll(timeout => timeout >= 30).Should().BeEmpty();

            // Upper bound = min((retryAttempt + 2) / 3, 2) * initialTimeout
            // Lower bound = 0.8 * Upper bound
            retryTimeouts[0].Should().BeInRange(12, 15);
            retryTimeouts[1].Should().BeInRange(16, 20);
            retryTimeouts[2].Should().BeInRange(20, 25);
            for (var i = 3; i < retryTimeouts.Count; i++)
            {
                retryTimeouts[i].Should().BeInRange(24, 30);
            }

            void AssertBoundsUsingFormula(double retryTimeout, int attempt)
            {
                var (lowerBound, upperBound) = Bounds(attempt, initialTimeoutValue);
                retryTimeout.Should().BeInRange(lowerBound, upperBound);
            }

            for (var i = 0; i < retryTimeouts.Count; i++)
            {
                AssertBoundsUsingFormula(retryTimeouts[i], retryAttempts[i]);
            }
        }

        public static (double LowerBound, double UpperBound) Bounds(int retryAttempt, int initialTimeout)
        {
            var upperBound = Math.Min((retryAttempt + 2) / 3d, 2d) * initialTimeout;
            var lowerBound = 0.8 * upperBound;
            return (lowerBound, upperBound);
        }
    }
}
