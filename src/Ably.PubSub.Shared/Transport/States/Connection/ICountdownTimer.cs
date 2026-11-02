using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Ably.PubSub.Transport.States.Connection
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

    /// <summary>
    /// Where the SDK's timers and delays come from. The default is the real clock; the UTS unit tests
    /// install a fake one through the internal <c>ClientOptions.TimerScheduler</c> so they can advance
    /// time instead of waiting for it.
    /// </summary>
    internal interface ITimerScheduler
    {
        /// <summary>
        /// Runs <paramref name="callback"/> once after <paramref name="delay"/>.
        /// </summary>
        /// <param name="delay">How long to wait.</param>
        /// <param name="callback">What to run when the delay elapses.</param>
        /// <returns>A handle; disposing it cancels the callback.</returns>
        IDisposable Schedule(TimeSpan delay, Action callback);

        /// <summary>A task that completes after <paramref name="delay"/>, as <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
        /// <param name="delay">How long to wait.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The delay task.</returns>
        Task Delay(TimeSpan delay, CancellationToken cancellationToken = default);
    }

    /// <summary>The real clock: a <see cref="Timer"/> and <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    internal sealed class SystemTimerScheduler : ITimerScheduler
    {
        public static readonly SystemTimerScheduler Instance = new SystemTimerScheduler();

        private SystemTimerScheduler()
        {
        }

        public IDisposable Schedule(TimeSpan delay, Action callback)
            => new Timer(state => callback(), null, (int)delay.TotalMilliseconds, Timeout.Infinite);

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
            => Task.Delay(delay, cancellationToken);
    }

    internal class CountdownTimer : ICountdownTimer, IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _name;
        private readonly object _lock;
        private readonly ITimerScheduler _scheduler;
        private IDisposable _timer;
        private Action _elapsed;
        private TimeSpan _delay;
        private bool _aborted;

        public CountdownTimer(string name, ILogger logger, ITimerScheduler scheduler = null)
        {
            _logger = logger ?? DefaultLogger.LoggerInstance;
            _name = name;
            _lock = new object();
            _scheduler = scheduler ?? SystemTimerScheduler.Instance;
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

        private IDisposable StartTimer(TimeSpan delay)
        {
            lock (_lock)
            {
                _aborted = false;
            }

            _delay = delay;
            return _scheduler.Schedule(delay, OnTimerOnElapsed);
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
