using System;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Transport.States.Connection;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    public class CountDownTimerSpecs : AblySpecs
    {
        public CountDownTimerSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        [Trait("intermittent", "true")]
        public async Task CountdownTimer_Start_StartsCountdown()
        {
            // Arrange
            var timer = CreateCountdownTimer();
            var callCounter = new CallCounter();

            // Act
            timer.Start(50, callCounter.Invoke);
            await Process(callCounter);

            // Assert
            callCounter.Count.Should().Be(1);
        }

        [Fact]
        public async Task CountdownTimer_Abort_StopsCountdown()
        {
            // Arrange
            var timer = CreateCountdownTimer();
            var callCounter = new CallCounter();
            timer.Start(50, callCounter.Invoke);

            // Act
            timer.Abort();
            await Process(callCounter);

            // Assert
            callCounter.Count.Should().Be(0);
        }

        [Fact]
        public async Task CountdownTimer_AbortStart_StartsNewCountdown()
        {
            const int millisecondStartDelay = 100;

            // Arrange
            var timer = CreateCountdownTimer();
            var callCounter = new CallCounter();
            timer.Start(millisecondStartDelay, callCounter.Invoke);

            // Act
            timer.Abort();
            timer.Start(millisecondStartDelay, callCounter.Invoke);
            await Process(callCounter);

            // Assert
            callCounter.Count.Should().Be(1);
        }

        [Fact]
        [Trait("intermittent", "true")]
        public async Task CountdownTimer_StartTwice_AbortsOldTimer()
        {
            const int millisecondStartDelay = 10;

            // Arrange
            var timer = CreateCountdownTimer();
            var callCounter1 = new CallCounter();
            var callCounter2 = new CallCounter();

            // Act
            timer.Start(millisecondStartDelay, callCounter1.Invoke);
            timer.Start(millisecondStartDelay, callCounter2.Invoke);
            await Task.Delay(250);
            await Process(callCounter2);

            // Assert
            callCounter2.Count.Should().Be(1);
            callCounter1.Count.Should().Be(0);
        }

        private static async Task Process(CallCounter callCounter)
        {
            for (var i = 0; i < 20; i++)
            {
                if (callCounter.Count == 0)
                {
                    await Task.Delay(50);
                }
                else
                {
                    break;
                }
            }
        }

        private CountdownTimer CreateCountdownTimer()
        {
            return new CountdownTimer("Test timer", Logger);
        }

        private class CallCounter
        {
            public CallCounter()
            {
                Count = 0;
            }

            public int Count { get; private set;  }

            public void Invoke()
            {
                Count++;
            }
        }
    }

    internal static class CountdownTimerExtensions
    {
        public static void Start(this CountdownTimer timer, int milliseconds, Action elapsed)
        {
            timer.Start(TimeSpan.FromMilliseconds(milliseconds), elapsed);
        }
    }
}
