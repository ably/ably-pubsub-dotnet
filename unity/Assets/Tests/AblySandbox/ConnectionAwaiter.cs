using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub;
using Ably.PubSub.Realtime;

namespace Assets.Tests.AblySandbox
{
    internal class ConnectionAwaiter
    {
        private readonly List<ConnectionState> _awaitedStates = new List<ConnectionState>();
        private static int WaitForSeconds = 16;

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
            return Wait(TimeSpan.FromSeconds(WaitForSeconds));
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
            var resultTask = _taskCompletionSource.Task;
            var completedTask = await Task.WhenAny(resultTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (completedTask == resultTask)
            {
                stopwatch.Stop();
                return stopwatch.Elapsed;
            }

            DefaultLogger.Debug($"[{_id} Timeout exceeded. Throwing TimeoutException");
            RemoveListener();
            throw new TimeoutException(
                $"Expected ''{_awaitedStates.Select(x => x.ToString()).JoinStrings()}' but current state was '{_connection.State}'");
        }
    }
}
