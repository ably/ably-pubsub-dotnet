using System;
using FluentAssertions;
using IO.Ably.Realtime.Workflow;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    /// <summary>
    /// Covers the check that decides whether locally held connection state is too old to resume
    /// with - RTN15g and, since it must include the maxIdleInterval, RTN15g2.
    /// </summary>
    [Trait("spec", "RTN15g")]
    [Trait("spec", "RTN15g2")]
    public class ConnectionStateFreshnessSpecs : AblySpecs
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromSeconds(15);

        private readonly Now _now = new Now();
        private readonly RealtimeState _state = new RealtimeState();

        [Fact]
        public void WhenNothingHasEverBeenReceived_ShouldNotBeStale()
        {
            // No ConfirmedAliveAt means there is no connection state to consider discarding.
            Connection(ttl: Ttl, maxIdleInterval: MaxIdleInterval, aliveAt: null);

            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeFalse();
        }

        [Fact]
        public void WhenWithinTheTtl_ShouldNotBeStale()
        {
            Connection(ttl: Ttl, maxIdleInterval: MaxIdleInterval, aliveAt: _now.Value);

            Advance(TimeSpan.FromSeconds(119));

            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeFalse();
        }

        [Fact]
        public void WhenPastTheTtlButWithinTheMaxIdleInterval_ShouldNotBeStale()
        {
            // The point of RTN15g2. At 130s we are past the 120s ttl, but the server may have been
            // silent for up to maxIdleInterval before we would have noticed, so the real window is
            // 135s and the state is still resumable.
            Connection(ttl: Ttl, maxIdleInterval: MaxIdleInterval, aliveAt: _now.Value);

            Advance(TimeSpan.FromSeconds(130));

            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeFalse();
        }

        [Fact]
        public void WhenPastTheTtlPlusTheMaxIdleInterval_ShouldBeStale()
        {
            Connection(ttl: Ttl, maxIdleInterval: MaxIdleInterval, aliveAt: _now.Value);

            Advance(TimeSpan.FromSeconds(136));

            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeTrue();
        }

        [Fact]
        public void WithNoMaxIdleInterval_ShouldMeasureAgainstTheTtlAlone()
        {
            Connection(ttl: Ttl, maxIdleInterval: null, aliveAt: _now.Value);

            Advance(TimeSpan.FromSeconds(121));

            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeTrue();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(15)]
        [InlineData(-1)]
        public void WithAnUnrepresentablyLargeTtl_ShouldNotThrow(int maxIdleIntervalSeconds)
        {
            // Regression. This used to be computed as ConfirmedAliveAt.Add(ttl), which throws
            // ArgumentOutOfRangeException once the result runs past DateTimeOffset.MaxValue. The
            // exception escaped into the command loop, where it was logged and dropped - silently
            // abandoning whichever state transition was in progress. Reachable today from
            // ConnectionSandboxOperatingSystemEventsForNetworkSpecs, which injects a MaxValue ttl
            // through a Connected message to assert RTN21 override behaviour.
            Connection(
                ttl: TimeSpan.MaxValue,
                maxIdleInterval: TimeSpan.FromSeconds(maxIdleIntervalSeconds),
                aliveAt: _now.Value);

            var ex = Record.Exception(() => _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn));

            ex.Should().BeNull();

            // An unreachable window can never have elapsed.
            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeFalse();
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-120)]
        public void WithANegativeMaxIdleInterval_ShouldNotThrow(int maxIdleIntervalSeconds)
        {
            // Nothing between the wire and here validates the sign - TimeSpanJsonConverter will hand
            // back a negative TimeSpan for a negative number - and a negative made the overflow
            // guard's own subtraction throw. That exception escapes HandleSetStateCommand and is
            // dropped by the command loop, leaving the client wedged in DISCONNECTED with no
            // transport: the same failure this method was rewritten to remove.
            Connection(
                ttl: Ttl,
                maxIdleInterval: TimeSpan.FromSeconds(maxIdleIntervalSeconds),
                aliveAt: _now.Value);

            Advance(TimeSpan.FromSeconds(130));

            var ex = Record.Exception(() => _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn));
            ex.Should().BeNull();

            // Treated as no promise at all, so the window is the ttl alone and 130s is past it.
            _state.Connection.HasConnectionStateTtlPassed(_now.ValueFn).Should().BeTrue();
        }

        private void Connection(TimeSpan ttl, TimeSpan? maxIdleInterval, DateTimeOffset? aliveAt)
        {
            _state.Connection.ConnectionStateTtl = ttl;
            _state.Connection.MaxIdleInterval = maxIdleInterval;

            if (aliveAt.HasValue)
            {
                _state.Connection.SetConfirmedAlive(aliveAt.Value);
            }
        }

        private void Advance(TimeSpan by) => _now.Reset(_now.Value.Add(by));

        public ConnectionStateFreshnessSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
