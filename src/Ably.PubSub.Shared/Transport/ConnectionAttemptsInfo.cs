using System;
using System.Collections.Generic;
using System.Linq;
using IO.Ably.Realtime;
using IO.Ably.Transport.States.Connection;

namespace IO.Ably.Transport
{
    internal class ConnectionAttemptsInfo
    {
        private readonly Func<DateTimeOffset> _now;

        public ConnectionAttemptsInfo(Func<DateTimeOffset> now = null)
        {
            _now = now ?? Defaults.NowFunc();
        }

        internal List<ConnectionAttempt> Attempts { get; } = new List<ConnectionAttempt>();

        internal DateTimeOffset? FirstAttempt => Attempts.Any() ? Attempts.First().Time : (DateTimeOffset?)null;

        internal int NumberOfAttempts => Attempts.Count;

        internal bool TriedToRenewToken { get; private set; }

        public void Reset()
        {
            Attempts.Clear();
            TriedToRenewToken = false;
        }

        public void RecordTokenRetry()
        {
            TriedToRenewToken = true;
        }

        public int DisconnectedCount() => Attempts.SelectMany(x => x.FailedStates)
            .Count(x => x.State == ConnectionState.Disconnected && x.ShouldUseFallback());

        public int SuspendedCount() => Attempts.SelectMany(x => x.FailedStates)
            .Count(x => x.State == ConnectionState.Suspended);

        public void UpdateAttemptState(ConnectionStateBase newState, ILogger logger)
        {
            switch (newState.State)
            {
                case ConnectionState.Connecting:
                    logger.Debug("Recording connection attempt.");
                    Attempts.Add(new ConnectionAttempt(_now()));
                    break;
                case ConnectionState.Failed:
                case ConnectionState.Closed:
                case ConnectionState.Connected:
                    logger.Debug("Resetting Attempts collection.");
                    Reset();
                    break;
                case ConnectionState.Suspended:
                case ConnectionState.Disconnected:
                    logger.Debug($"Recording failed attempt for state {newState.State}.");
                    if (newState.Exception != null)
                    {
                        RecordAttemptFailure(newState.State, newState.Exception);
                    }
                    else
                    {
                        RecordAttemptFailure(newState.State, newState.Error);
                    }

                    break;
            }
        }

        private void RecordAttemptFailure(ConnectionState state, ErrorInfo error)
        {
            var attempt = Attempts.LastOrDefault() ?? new ConnectionAttempt(_now());
            attempt.FailedStates.Add(new AttemptFailedState(state, error));
            if (Attempts.Count == 0)
            {
                Attempts.Add(attempt);
            }
        }

        private void RecordAttemptFailure(ConnectionState state, Exception ex)
        {
            if (Attempts.Any())
            {
                var attempt = Attempts.Last();
                attempt.FailedStates.Add(new AttemptFailedState(state, ex));
            }
        }
    }
}
