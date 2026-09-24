using System;
using System.Collections.Generic;
using System.Linq;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport.States.Connection;

namespace Ably.PubSub.Transport
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

        /// <summary>
        /// How many times we have skipped the disconnected retry timeout and reconnected straight
        /// away since last being connected. RTN17j sanctions retrying immediately to work through the
        /// fallback domains, but the traversal must be bounded or RTB1 is never reached.
        /// </summary>
        internal int InstantRetryCount { get; private set; }

        public void Reset()
        {
            Attempts.Clear();
            TriedToRenewToken = false;
            InstantRetryCount = 0;
        }

        public void RecordInstantRetry()
        {
            InstantRetryCount++;
        }

        public void RecordTokenRetry()
        {
            TriedToRenewToken = true;
        }

        public int DisconnectedCount() => Attempts.SelectMany(x => x.FailedStates)
            .Count(x => x.State == ConnectionState.Disconnected && x.ShouldUseFallback());

        public int SuspendedCount() => Attempts.SelectMany(x => x.FailedStates)
            .Count(x => x.State == ConnectionState.Suspended);

        public void UpdateAttemptState(ConnectionStateBase newState, ConnectionState previousState, ILogger logger)
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
                        RecordAttemptFailure(
                            newState.State,
                            newState.Exception,
                            droppedAnEstablishedConnection: previousState == ConnectionState.Connected);
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

        private void RecordAttemptFailure(ConnectionState state, Exception ex, bool droppedAnEstablishedConnection)
        {
            // Mirrors the ErrorInfo overload above, including the empty-collection case, which is
            // the normal one here: the only caller passing an exception is the transport dropping out
            // of CONNECTED, and entering CONNECTED has just cleared the collection. Dropping it
            // altogether would leave FirstAttempt null and delay the RTN14e clock.
            //
            // Recorded for that clock but held back from RTN17 host selection, which is what
            // droppedAnEstablishedConnection carries - see AttemptFailedState.
            var attempt = Attempts.LastOrDefault() ?? new ConnectionAttempt(_now());
            attempt.FailedStates.Add(new AttemptFailedState(state, ex, droppedAnEstablishedConnection));
            if (Attempts.Count == 0)
            {
                Attempts.Add(attempt);
            }
        }
    }
}
