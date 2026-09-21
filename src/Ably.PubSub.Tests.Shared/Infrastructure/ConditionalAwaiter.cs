using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace IO.Ably.Tests.Infrastructure
{
    public sealed class ConditionalAwaiter : IDisposable
    {
        private readonly Func<bool> _condition;
        private readonly Func<string> _getError;
        private readonly Timer _timer;
        private readonly TaskCompletionSource<bool> _completionSource;
        private int _tickCount;
        private readonly int _timeout;

        public ConditionalAwaiter(Func<bool> condition, Func<string> getError = null, int timeout = 10)
        {
            _condition = condition;
            _getError = getError;
            _timer = new Timer
            {
                Enabled = true,
                Interval = 100,
            };
            _timer.Elapsed += TimerOnElapsed;
            _completionSource = new TaskCompletionSource<bool>();
            _timeout = timeout;
        }

        public TaskAwaiter<bool> GetAwaiter()
        {
            return _completionSource.Task.GetAwaiter();
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Interlocked.Increment(ref _tickCount);
            if (_tickCount > _timeout * 10)
            {
                string message = $"{_timeout} seconds elapsed. Giving up.";
                if (_getError != null)
                {
                    message += " Error: " + _getError();
                }

                _completionSource.SetException(new Exception(message));
            }

            if (_condition() && _completionSource.Task.IsCompleted == false)
            {
                _timer.Enabled = false;
                _completionSource.SetResult(true);
                _timer.Elapsed -= TimerOnElapsed;
                _timer.Dispose();
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
