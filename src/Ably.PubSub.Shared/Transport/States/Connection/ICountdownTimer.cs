using System;
using System.Diagnostics;
using System.Threading;

namespace IO.Ably.Transport.States.Connection
{
    /// <summary>
    /// Internal interface used for countdown timer.
    /// </summary>
    internal interface ICountdownTimer
    {
        /// <summary>
        /// Starts a timer.
        /// </summary>
        /// <param name="delay">when to fire.</param>
        /// <param name="onTimeOut">action to execute.</param>
        void Start(TimeSpan delay, Action onTimeOut);

        /// <summary>
        /// Aborts the timer.
        /// </summary>
        /// <param name="trigger">whether to trigger the action. Default: false.</param>
        void Abort(bool trigger = false);
    }

    internal class CountdownTimer : ICountdownTimer, IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _name;
        private readonly object _lock;
        private Timer _timer;
        private Action _elapsed;
        private TimeSpan _delay;
        private bool _aborted;

        public CountdownTimer(string name, ILogger logger)
        {
            _logger = logger ?? DefaultLogger.LoggerInstance;
            _name = name;
            _lock = new object();
        }

        public void Start(TimeSpan delay, Action elapsed)
        {
            if (elapsed == null)
            {
                throw new ArgumentNullException(nameof(elapsed));
            }

            if (_logger.IsDebug)
            {
                _logger.Debug($"Setting up timer '{_name}' to run action after {delay.TotalSeconds} seconds.");
            }

            _elapsed = elapsed;

            if (_timer != null)
            {
                Abort();
            }

            _timer = StartTimer(delay);
        }

        private Timer StartTimer(TimeSpan delay)
        {
            lock (_lock)
            {
                _aborted = false;
            }

            _delay = delay;
            var timer = new Timer(state => OnTimerOnElapsed(), null, (int)delay.TotalMilliseconds, Timeout.Infinite);
            return timer;
        }

        private void OnTimerOnElapsed()
        {
            lock (_lock)
            {
                if (_aborted)
                {
                    if (_logger.IsDebug)
                    {
                        _logger.Debug($"Timer '{_name}' aborted. Skipping OnElapsed callback.");
                    }

                    return;
                }
            }

            Debug.Assert(_elapsed != null, "Did you forget to call Start?");

            try
            {
                if (_logger.IsDebug)
                {
                    _logger.Debug($"Timer '{_name}' interval {_delay.TotalSeconds} seconds elapsed and calling action.");
                }

                _elapsed();
            }
            catch (Exception ex)
            {
                _logger.Error("Error in method called by timer.", ex);
            }
        }

        public void Abort(bool trigger = false)
        {
            if (trigger)
            {
                OnTimerOnElapsed();
            }

            lock (_lock)
            {
                _aborted = true;
            }

            if (_logger.IsDebug)
            {
                _logger.Debug($"Aborting timer '{_name}'");
            }

            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
