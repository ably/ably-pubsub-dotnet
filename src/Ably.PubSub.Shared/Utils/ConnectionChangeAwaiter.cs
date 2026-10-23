using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport.States.Connection;

namespace Ably.PubSub.Utils
{
    /// <summary>
    /// Waits for connection state changes. It starts listening when constructed and queues every change
    /// until it is consumed by <see cref="Wait"/>, so a change that happens between construction and
    /// the first <see cref="Wait"/> call (or between two calls) is never missed. Create it before
    /// triggering the action whose outcome is awaited.
    /// </summary>
    internal class ConnectionChangeAwaiter : IDisposable
    {
        private readonly Connection _connection;
        private readonly ITimerScheduler _scheduler;
        private readonly string _id = Guid.NewGuid().ToString("D").Split('-')[0];
        private readonly object _lock = new object();
        private readonly Queue<ConnectionState> _changes = new Queue<ConnectionState>();
        private TaskCompletionSource<bool> _signal;
        private bool _disposed;

        public ConnectionChangeAwaiter(Connection connection, ITimerScheduler scheduler = null)
        {
            _connection = connection;
            _scheduler = scheduler ?? SystemTimerScheduler.Instance;
            _connection.InternalStateChanged += ChangeListener;
        }

        public async Task<(bool, ConnectionState?)> Wait(TimeSpan timeout)
        {
            if (DefaultLogger.IsDebug)
            {
                DefaultLogger.Debug($"[{_id}] Waiting for state change for {timeout.TotalSeconds} seconds");
            }

            TaskCompletionSource<bool> signal;
            lock (_lock)
            {
                if (_changes.Count > 0)
                {
                    return (true, _changes.Dequeue());
                }

                signal = new TaskCompletionSource<bool>();
                _signal = signal;
            }

            var changed = await signal.Task.TimeoutAfter(timeout, false, _scheduler);

            lock (_lock)
            {
                _signal = null;
                if (changed && _changes.Count > 0)
                {
                    return (true, _changes.Dequeue());
                }

                return (false, null);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _connection.InternalStateChanged -= ChangeListener;
        }

        private void ChangeListener(object sender, ConnectionStateChange change)
        {
            TaskCompletionSource<bool> signal;
            lock (_lock)
            {
                _changes.Enqueue(change.Current);
                signal = _signal;
            }

            signal?.TrySetResult(true);
        }
    }
}
