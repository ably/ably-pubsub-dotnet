using System;
using System.Collections.Generic;

namespace IO.Ably.Transport
{
    internal sealed class ConnectionAttempt
    {
        public ConnectionAttempt(DateTimeOffset time)
        {
            Time = time;
        }

        public DateTimeOffset Time { get; }

        public List<AttemptFailedState> FailedStates { get; } = new List<AttemptFailedState>();
    }
}
