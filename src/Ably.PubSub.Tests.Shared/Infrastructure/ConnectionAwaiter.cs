using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using IO.Ably.Realtime;

namespace IO.Ably.Tests.Infrastructure
{
    internal class ConnectionAwaiter
    {
        private readonly List<ConnectionState> _awaitedStates = new List<ConnectionState>();

        private readonly Connection _connection;
        private readonly TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>();
        private readonly string _id = Guid.NewGuid().ToString("D").Split('-')[0];

        public ConnectionAwaiter(Connection connection, params ConnectionState[] awaitedStates)
        {
            _connection = connection;
            if (awaitedStates != null && awaitedStates.Length > 0)
            {
                _awaitedStates.AddRange(awaitedStates);
            }
            else
            {
                throw new ArgumentNullException(nameof(awaitedStates), "Please add at least one awaited state");
            }
        }

        private void RemoveListener()
        {
            DefaultLogger.Debug($"[{_id}] Removing Connection listener");
            _connection.Off(Conn_StateChanged);
        }

        private void Conn_StateChanged(ConnectionStateChange e)
        {
            if (_awaitedStates.Contains(e.Current))
            {
                DefaultLogger.Debug($"[{_id}] Desired state was reached.");
                RemoveListener();
                _taskCompletionSource.SetResult(true);
            }
        }

        public Task<TimeSpan> Wait()
        {
            return Wait(TimeSpan.FromSeconds(16));
        }

        public async Task<TimeSpan> Wait(TimeSpan timeout)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (DefaultLogger.IsDebug)
            {
                DefaultLogger.Debug(
                    $"[{_id}] Waiting for state {string.Join(",", _awaitedStates)} for {timeout.TotalSeconds} seconds");
            }

            if (_awaitedStates.Contains(_connection.State))
            {
                DefaultLogger.Debug($"Current state is {_connection.State}. Desired state reached.");
                return TimeSpan.Zero;
            }

            _connection.On(Conn_StateChanged);
            var tResult = _taskCompletionSource.Task;
            var tCompleted = await Task.WhenAny(tResult, Task.Delay(timeout)).ConfigureAwait(false);
            if (tCompleted == tResult)
            {
                stopwatch.Stop();
                return stopwatch.Elapsed;
            }

            DefaultLogger.Debug($"[{_id} Timeout exceeded. Throwing TimeoutException");
            RemoveListener();
            throw new TimeoutException(
                $"Expected '{_awaitedStates.Select(x => x.ToString()).JoinStrings()}' but current state was '{_connection.State}'");
        }
    }
}
