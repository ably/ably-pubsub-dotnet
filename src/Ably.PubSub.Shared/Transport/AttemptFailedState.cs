using System;
using IO.Ably.Realtime;

namespace IO.Ably.Transport
{
    internal sealed class AttemptFailedState
    {
        private readonly bool _droppedAnEstablishedConnection;

        public AttemptFailedState(ConnectionState state, ErrorInfo error)
        {
            State = state;
            Error = error;
        }

        public AttemptFailedState(ConnectionState state, Exception ex, bool droppedAnEstablishedConnection = false)
        {
            State = state;
            Exception = ex;
            _droppedAnEstablishedConnection = droppedAnEstablishedConnection;
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
            // RTN17f admits an exception through RSC15l1, "host unresolvable or unreachable", which
            // describes an attempt that never landed. A transport dropping out of CONNECTED says the
            // opposite - the host answered a moment ago - so it is not on its own grounds to move off
            // the primary, and RTN17i requires the primary be preferred regardless. The reconnect
            // still goes out; if the datacenter really has gone, that attempt fails at connect time
            // and is eligible, so the fallbacks are one attempt away rather than skipped.
            return Exception != null && _droppedAnEstablishedConnection == false;
        }

        private bool IsRecoverableError()
        {
            return Error != null && Error.IsRetryableStatusCode();
        }
    }
}
