using System;
using System.Threading.Tasks;

using Ably.PubSub.Tests.Infrastructure;

namespace Ably.PubSub.Tests
{
    internal sealed class TimeoutCallback<T> : IDisposable
    {
        private readonly TaskCompletionAwaiter _tca;

        public TimeoutCallback(int timeout)
        {
            _tca = new TaskCompletionAwaiter(timeout);
        }

        public Task<bool> Task => _tca.Task;

        public Action<T> Wrap(Action<T> callback)
        {
            return csc =>
            {
                callback(csc);
                _tca.SetCompleted();
            };
        }

        public void Dispose()
        {
            _tca.Dispose();
        }
    }
}
