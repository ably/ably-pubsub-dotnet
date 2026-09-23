using System;

namespace IO.Ably.Tests
{
    internal class Now
    {
        public Now()
        {
            Value = DateTimeOffset.Now;
        }

        public DateTimeOffset Value { get; private set; }

        public void Reset(DateTimeOffset now)
        {
            Value = now;
        }

        public DateTimeOffset ValueFn() => Value;
    }
}
