using System.Diagnostics;

namespace IO.Ably
{
    /// <summary>The default logger implementation, that writes to debug output.</summary>
    internal class DefaultLoggerSink : ILoggerSink
    {
        private readonly object _syncRoot = new object();

        public void LogEvent(LogLevel level, string message)
        {
            lock (_syncRoot)
            {
                Debug.WriteLine($"Ably: [{level}] {message}");
            }
        }
    }
}
