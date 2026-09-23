using System;
using System.Threading.Tasks;

namespace IO.Ably.Tests.Infrastructure
{
    /// <summary>
    /// Count a certain number of ticks and complete or timeout
    /// </summary>
    internal sealed class TaskCountAwaiter : IDisposable
    {
        private readonly TaskCompletionAwaiter _awaiter;
        private int _index;

        public TaskCountAwaiter(int count, int timeoutMs = 10000)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Must be 1 or more");
            }

            _index = count;
            _awaiter = new TaskCompletionAwaiter(timeoutMs);
        }

        public Task<bool> Task => _awaiter.Task;

        public void Tick()
        {
            _index--;
            if (_index == 0)
            {
                _awaiter.SetCompleted();
            }
        }

        public void Dispose()
        {
            _awaiter.Dispose();
        }
    }
}
