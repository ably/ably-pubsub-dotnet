using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>
    /// An interface exposing the ability to register listeners for a class of events.
    /// </summary>
    /// <typeparam name="TEvent">Type of event.</typeparam>
    /// <typeparam name="TArgs">Type of arguments passed.</typeparam>
    public interface IEventEmitter<TEvent, TArgs> where TEvent : struct where TArgs : EventArgs
    {
        /// <summary>
        /// Unregisters all event listeners.
        /// </summary>
        void Off();

        /// <summary>
        /// Register a given listener for all events.
        /// </summary>
        /// <param name="listener">listener function.</param>
        void On(Action<TArgs> listener);

        /// <summary>
        /// Register a given listener for a single occurrence of a single event.
        /// </summary>
        /// <param name="listener">listener function.</param>
        void Once(Action<TArgs> listener);

        /// <summary>
        /// Unregister the passed listener.
        /// </summary>
        /// <param name="listener">listener function.</param>
        void Off(Action<TArgs> listener);

        /// <summary>
        /// Register the given listener for a given event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">listener function.</param>
        void On(TEvent state, Action<TArgs> action);

        /// <summary>
        /// Register the given listener for a single occurence of a single event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">listener function.</param>
        void Once(TEvent state, Action<TArgs> action);

        /// <summary>
        /// Unregister a given listener for a single event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">listener function.</param>
        void Off(TEvent state, Action<TArgs> action);

        /// <summary>
        /// Register a given listener  for all events.
        /// </summary>
        /// <param name="listener">listener function.</param>
        void On(Func<TArgs, Task> listener);

        /// <summary>
        /// Register a given listener for a single occurrence of a single event.
        /// </summary>
        /// <param name="listener">listener function.</param>
        void Once(Func<TArgs, Task> listener);

        /// <summary>
        /// Unregister the passed listener.
        /// </summary>
        /// <param name="listener">async listener function.</param>
        void Off(Func<TArgs, Task> listener);

        /// <summary>
        /// Register the given listener for a given event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">async listener function.</param>
        void On(TEvent state, Func<TArgs, Task> action);

        /// <summary>
        /// Register the given listener for a single occurence of a single event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">async listener function.</param>
        void Once(TEvent state, Func<TArgs, Task> action);

        /// <summary>
        /// Unregister a given listener for a single event.
        /// </summary>
        /// <param name="state">the event to listen for.</param>
        /// <param name="action">async listener function.</param>
        void Off(TEvent state, Func<TArgs, Task> action);
    }

    /// <summary>
    /// Abstract class that allows other classes to implement the IEventEmitter interface.
    /// </summary>
    /// <typeparam name="TState">Type of Event.</typeparam>
    /// <typeparam name="TArgs">Type of args passed to the listeners.</typeparam>
    public abstract class EventEmitter<TState, TArgs> : IEventEmitter<TState, TArgs>, IDisposable
        where TState : struct
        where TArgs : EventArgs
    {
        internal EventEmitter(ILogger logger)
        {
            Logger = logger ?? DefaultLogger.LoggerInstance;
        }

        internal ILogger Logger { get; set; }

        private readonly List<Emitter<TState, TArgs>> _emitters = new List<Emitter<TState, TArgs>>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private bool _disposedValue;

        internal JArray GetState()
        {
            return new JArray(_emitters.Select(x => new { state = x.State, once = x.Once }));
        }

        /// <summary>
        /// Used to delegate notifying the clients.
        /// </summary>
        protected abstract Action<Action> NotifyClient { get; }

        private class Emitter<TStatePrivate, TArgsPrivate>
            where TStatePrivate : struct
        {
            public Action<TArgsPrivate> Action { get; }

            public Func<TArgsPrivate, Task> AsyncAction { get; }

            public bool Once { get; }

            public TStatePrivate? State { get; }

            public Emitter(Action<TArgsPrivate> action, TStatePrivate? state = null, bool once = false)
            {
                Action = action ?? throw new ArgumentException("Cannot pass a null action to the EventEmitter");
                State = state;
                Once = once;
            }

            public Emitter(Func<TArgsPrivate, Task> asyncAction, TStatePrivate? state = null, bool once = false)
            {
                AsyncAction = asyncAction ?? throw new ArgumentException("Cannot pass a null action to the EventEmitter");
                State = state;
                Once = once;
            }
        }

        /// <inheritdoc/>
        public void Off()
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void On(Action<TArgs> listener)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    _emitters.Add(new Emitter<TState, TArgs>(listener));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        /// <inheritdoc/>
        public void On(Func<TArgs, Task> listener)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    _emitters.Add(new Emitter<TState, TArgs>(listener));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        /// <inheritdoc/>
        public void Once(Action<TArgs> listener)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    _emitters.Add(new Emitter<TState, TArgs>(listener, once: true));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        /// <inheritdoc/>
        public void Once(Func<TArgs, Task> listener)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    _emitters.Add(new Emitter<TState, TArgs>(listener, once: true));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        /// <inheritdoc/>
        public void Off(Action<TArgs> listener)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.RemoveAll(x => x.Action == listener);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void Off(Func<TArgs, Task> listener)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.RemoveAll(x => x.AsyncAction == listener);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void On(TState state, Action<TArgs> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.Add(new Emitter<TState, TArgs>(action, state));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void On(TState state, Func<TArgs, Task> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.Add(new Emitter<TState, TArgs>(action, state));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void Once(TState state, Action<TArgs> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.Add(new Emitter<TState, TArgs>(action, state, true));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void Once(TState state, Func<TArgs, Task> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.Add(new Emitter<TState, TArgs>(action, state, true));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void Off(TState state, Action<TArgs> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.RemoveAll(x => x.Action == action && x.State.HasValue && x.State.Value.Equals(state));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public void Off(TState state, Func<TArgs, Task> action)
        {
            _lock.EnterWriteLock();
            try
            {
                _emitters.RemoveAll(x => x.AsyncAction == action && x.State.HasValue && x.State.Value.Equals(state));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Protected method that Emits an event and calls all registered listeners that match.
        /// </summary>
        /// <param name="state">the event emitted.</param>
        /// <param name="data">the data arguments passed to the listeners.</param>
        protected void Emit(TState state, TArgs data)
        {
            List<Emitter<TState, TArgs>> emitters;
            _lock.EnterUpgradeableReadLock();
            try
            {
                emitters = _emitters.Where(x => x.State == null
                                            || x.State.Value.Equals(state)).ToList();

                if (emitters.Any(x => x.Once))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        foreach (var emitter in emitters.Where(x => x.Once))
                        {
                            _emitters.Remove(emitter);
                        }
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }

            foreach (var emitter in emitters)
            {
                var current = emitter;
                NotifyClient(() =>
                {
                    try
                    {
                        current.Action?.Invoke(data);
                        _ = current.AsyncAction?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error executing on event: {state}", ex);
                    }
                });
            }
        }

        /// <summary>
        /// Dispose(bool disposing) executes in two distinct scenarios. If disposing equals true, the method has
        /// been called directly or indirectly by a user's code. Managed and unmanaged resources can be disposed.
        /// If disposing equals false, the method has been called by the runtime from inside the finalizer and
        /// you should not referenceother objects. Only unmanaged resources can be disposed.
        /// </summary>
        /// <param name="disposing">Refer to the summary.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _lock.Dispose();
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// Implement IDisposable.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
