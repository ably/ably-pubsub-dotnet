using System;
using System.Collections.Generic;
using Ably.PubSub;

namespace NotificationsPublisher
{
    /// <summary>
    /// Custom implementation for the Ably loggerSink which helps us capture the log messages so they can be later displayed.
    /// </summary>
    public class AppLogger : ILoggerSink
    {
        private const int MaxMessages = 1000;
        private readonly object _lock = new ();

        /// <summary>
        /// Captured log messages.
        /// </summary>
        public List<string> Messages { get; } = new ();

        /// <summary>
        /// Executed every time a messages is added to the list.
        /// </summary>
        public Action<string> OnMessageAdded { get; set; }

        /// <inheritdoc />
        public void LogEvent(LogLevel level, string message)
        {
            if (message.Contains("HeartbeatMonitor"))
            {
                return;
            }

            lock (_lock)
            {
                var msg = $"{level}: {message}";
                Messages.Add(msg);

                if (Messages.Count > MaxMessages)
                {
                    Messages.RemoveAt(0);
                }

                OnMessageAdded(msg);
            }
        }
    }
}
