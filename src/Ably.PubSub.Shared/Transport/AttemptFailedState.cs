using System;
using IO.Ably.Realtime;

namespace IO.Ably.Transport
{
    internal sealed class AttemptFailedState
    {
        public AttemptFailedState(ConnectionState state, ErrorInfo error)
        {
            State = state;
            Error = error;
        }

        public AttemptFailedState(ConnectionState state, Exception ex)
        {
            State = state;
            Exception = ex;
        }

        public ErrorInfo Error { get; }

        public Exception Exception { get; }

        public ConnectionState State { get; }

        public bool ShouldUseFallback()
        {
            return IsDisconnectedOrSuspendedState() &&
                   (IsRecoverableError() || IsRecoverableException());
        }

        private bool IsDisconnectedOrSuspendedState()
        {
            return State == ConnectionState.Disconnected || State == ConnectionState.Suspended;
        }

        private bool IsRecoverableException()
        {
            return Exception != null;
        }

        private bool IsRecoverableError()
        {
            return Error != null && Error.IsRetryableStatusCode();
        }
    }
}
