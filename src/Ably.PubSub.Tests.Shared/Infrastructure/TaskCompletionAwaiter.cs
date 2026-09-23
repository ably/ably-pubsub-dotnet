using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace IO.Ably.Tests.Infrastructure
{
    public sealed class TaskCompletionAwaiter : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private TaskCompletionSource<bool> _taskCompletionSource;

        /// <summary>
        /// Wait for task(s) to complete
        /// </summary>
        /// <param name="timeoutMs">set the timeout in MS</param>
        /// <param name="taskCount">sets the number of tasks. Used with Tick().
        /// Tick() will need to be called this number of times before a timeout to be considered a success.</param>
        public TaskCompletionAwaiter(int timeoutMs = 10000, int taskCount = 1)
        {
            TimeoutMs = timeoutMs;

            if (taskCount < 1)
            {
                throw new ArgumentException("Must be greater than zero", nameof(taskCount));
            }

            TaskCount = taskCount;

            _cancellationTokenSource = new CancellationTokenSource(TimeoutMs);
            _taskCompletionSource = new TaskCompletionSource<bool>();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _cancellationTokenSource.Token.Register(() =>
            {
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds >= TimeoutMs)
                {
                    TimeoutExpired = true;
                }

                _taskCompletionSource.TrySetResult(false);
            });
        }

        public int TimeoutMs { get; }

        public int TaskCount { get; private set; }

        public bool TimeoutExpired { get; private set; }

        public void Done()
        {
            SetCompleted();
        }

        public void Tick()
        {
            TaskCount--;
            if (TaskCount < 1)
            {
                SetCompleted();
            }
        }

        public void Set(bool value)
        {
            _taskCompletionSource.TrySetResult(value);
        }

        public void SetCompleted()
        {
            _taskCompletionSource.TrySetResult(true);
        }

        public void SetFailed()
        {
            _taskCompletionSource.TrySetResult(false);
        }

        public Task<bool> Task => _taskCompletionSource.Task;

        public TaskAwaiter<bool> GetAwaiter()
        {
            return _taskCompletionSource.Task.GetAwaiter();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Dispose();
            _taskCompletionSource = null;
        }
    }
}
