using System;
using System.Threading.Tasks;
using IO.Ably.Realtime;

namespace IO.Ably.Utils
{
    internal class ConnectionChangeAwaiter
    {
        private ConnectionState _currentState;

        private readonly Connection _connection;
        private readonly string _id = Guid.NewGuid().ToString("D").Split('-')[0];

        public ConnectionChangeAwaiter(Connection connection)
        {
            _connection = connection;
            _currentState = _connection.State;
        }

        public async Task<(bool, ConnectionState?)> Wait(TimeSpan timeout)
        {
            if (DefaultLogger.IsDebug)
            {
                DefaultLogger.Debug($"[{_id}] Waiting for state change for {timeout.TotalSeconds} seconds");
            }

            TaskCompletionSource<(bool, ConnectionState?)> taskCompletionSource =
                new TaskCompletionSource<(bool, ConnectionState?)>();

            try
            {
                _connection.InternalStateChanged += ChangeListener;
                var tResult = taskCompletionSource.Task;
                return await tResult.TimeoutAfter(timeout, (false, null));
            }
            finally
            {
                _connection.InternalStateChanged -= ChangeListener;
            }

            void ChangeListener(object sender, ConnectionStateChange change)
            {
                _currentState = change.Current;
                taskCompletionSource.SetResult((true, _currentState));
            }
        }
    }
}
