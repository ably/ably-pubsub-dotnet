using System.Threading;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class TimeoutCallbackTests
    {
        private const int BaseMs = 500;

        [Fact]
        public async void TimeoutCallback_FastCallbackCompletesWithTrue()
        {
            var tc = new TimeoutCallback<Counter>(BaseMs);

            var counter = new Counter();
            var f = tc.Wrap(async c =>
            {
                c.Value = 1;
                Thread.Sleep(BaseMs / 2);
            });

            f(counter);
            var result = await tc.Task;
            counter.Value.Should().Be(1);
            result.Should().BeTrue();
        }

        [Fact]
        public async void TimeoutCallback_SlowCallbackCompletesWithFalse()
        {
            var tc = new TimeoutCallback<Counter>(BaseMs);

            var counter = new Counter();
            var f = tc.Wrap(c =>
            {
                c.Value = 1;
                Thread.Sleep(BaseMs * 2);
            });

            f(counter);
            var result = await tc.Task;
            counter.Value.Should().Be(1);
            result.Should().BeFalse();
        }

        private class Counter
        {
            public int Value { get; set; }
        }
    }
}
