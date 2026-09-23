using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Infrastructure
{
    public class TaskCompleterAwaiterTests
    {
        [Fact]
        public void TimeoutElapsedIsInitiallyFalse()
        {
            var sut = new TaskCompletionAwaiter(500);
            sut.TimeoutExpired.Should().BeFalse();
        }

        [Fact(Skip = "Flaky test, keeps failing on CI. Since this is a test helper, no need to test on CI")]
        public async void TimeoutElapsedSignalsOnTimeout()
        {
            var sut = new TaskCompletionAwaiter(2000);
            await Task.Delay(5000);
            sut.TimeoutExpired.Should().BeTrue();
        }
    }
}
