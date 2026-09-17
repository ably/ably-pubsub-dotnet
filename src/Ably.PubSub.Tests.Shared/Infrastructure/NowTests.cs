using System;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class NowTests
    {
        [Fact]
        public void Now_ValueFunctionReturnsValueProperty()
        {
            var now = new Now();
            now.Value.Should().Be(now.ValueFn());
        }

        [Fact]
        public void Now_ResetUpdatesValue()
        {
            var now = new Now();

            var oldNow = now.Value;

            const int delta = 500;

            var newNow = oldNow.AddMilliseconds(delta);
            now.Reset(newNow);

            now.Value.Should().Be(newNow);
            (now.Value - oldNow).Should().BeGreaterOrEqualTo(TimeSpan.FromMilliseconds(delta));
        }
    }
}
