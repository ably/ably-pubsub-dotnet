using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;

namespace Ably.PubSub.Tests.Infrastructure
{
    internal class ConnectionAwaiter
    {
        private readonly List<ConnectionState> _awaitedStates = new List<ConnectionState>();

        private readonly Connection _connection;
        private readonly TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                _taskCompletionSource.TrySetResult(true);
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

            // Subscribe BEFORE checking the current state. Checking first leaves a window in which the connection
            // can reach the awaited state without this awaiter ever being notified.
            _connection.On(Conn_StateChanged);
            try
            {
                if (_awaitedStates.Contains(_connection.State))
                {
                    DefaultLogger.Debug($"Current state is {_connection.State}. Desired state reached.");
                    return TimeSpan.Zero;
                }

                var tResult = _taskCompletionSource.Task;
                using (var delayCancellation = new CancellationTokenSource())
                {
                    var tCompleted = await Task.WhenAny(tResult, Task.Delay(timeout, delayCancellation.Token)).ConfigureAwait(false);
                    if (tCompleted == tResult)
                    {
                        delayCancellation.Cancel();
                        stopwatch.Stop();
                        return stopwatch.Elapsed;
                    }
                }

                // The state may have been reached just as the timeout fired.
                if (tResult.IsCompleted)
                {
                    stopwatch.Stop();
                    return stopwatch.Elapsed;
                }

                DefaultLogger.Debug($"[{_id} Timeout exceeded. Throwing TimeoutException");
                throw new TimeoutException(
                    $"Expected '{_awaitedStates.Select(x => x.ToString()).JoinStrings()}' but current state was '{_connection.State}'");
            }
            finally
            {
                RemoveListener();
            }
        }
    }
}
