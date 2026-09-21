using System;

using IO.Ably.Transport;

using FluentAssertions;
using IO.Ably.Realtime;
using Xunit;

namespace IO.Ably.Tests.Transport
{
    public class ConnectionAttemptTests
    {
        [Fact]
        public void ConstructedObjectHasExpectedState()
        {
            var t = DateTimeOffset.Now;

            var o = new ConnectionAttempt(t);

            o.Time.Should().Be(t);
            o.FailedStates.Should().NotBeNull();
            o.FailedStates.Should().BeEmpty();
        }

        [Fact]
        public void FailedStatesAccumulatesAndPreservesOrder()
        {
            var first = new AttemptFailedState(ConnectionState.Failed, ErrorInfo.ReasonClosed);
            var second = new AttemptFailedState(ConnectionState.Connecting, ErrorInfo.ReasonTimeout);
            var third = new AttemptFailedState(ConnectionState.Closed, ErrorInfo.ReasonUnknown);

            var o = new ConnectionAttempt(DateTimeOffset.Now);
            o.FailedStates.Add(first);
            o.FailedStates.Add(second);
            o.FailedStates.Add(third);

            o.FailedStates.Count.Should().Be(3);
            o.FailedStates[0].Should().Be(first);
            o.FailedStates[1].Should().Be(second);
            o.FailedStates[2].Should().Be(third);
        }
    }
}
