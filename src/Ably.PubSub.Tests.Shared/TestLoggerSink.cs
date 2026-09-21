using System.Collections.Generic;

namespace IO.Ably.AcceptanceTests
{
    internal sealed class TestLoggerSink : ILoggerSink
    {
        void ILoggerSink.LogEvent(LogLevel level, string message)
        {
            LastLevel = level;
            LastMessage = message;
            Messages.Add(level + ": " + message);
        }

        public LogLevel? LastLevel { get; private set; }

        public string LastMessage { get; private set; }

        public ICollection<string> Messages { get; } = new List<string>();
    }
}
