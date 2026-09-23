using System;
using IO.Ably.Transport.States.Connection;

namespace IO.Ably.Tests
{
    internal class FakeTimer : ICountdownTimer
    {
        public Action OnTimeOut { get; private set; }

        public TimeSpan LastDelay { get; set; }

        public bool StartedWithAction { get; private set; }

        public bool Aborted { get; set; }

        public void Start(TimeSpan delay, Action onTimeOut)
        {
            OnTimeOut = onTimeOut;
            StartedWithAction = true;
            LastDelay = delay;
        }

        public void Abort(bool trigger = false)
        {
            Aborted = true;
        }
    }
}
