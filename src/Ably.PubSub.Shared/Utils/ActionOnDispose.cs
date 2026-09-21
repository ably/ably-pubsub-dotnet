using System;

namespace IO.Ably
{
    /// <summary>Utility class that implements IDisposable bu calling the provided action.</summary>
    internal class ActionOnDispose : IDisposable
    {
        private readonly Action _action;

        public ActionOnDispose(Action act)
        {
            _action = act ?? throw new ArgumentNullException();
        }

        void IDisposable.Dispose()
        {
            _action();
        }
    }
}
