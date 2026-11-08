using System.Linq;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>One entry of the specs' <c>captured_logs</c> / <c>captured_log_messages</c>.</summary>
    public sealed class CapturedLog
    {
        internal CapturedLog(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Message { get; }

        public override string ToString() => $"{Level}: {Message}";
    }

    /// <summary>The specs' <c>logHandler: (level, message) =&gt; captured_logs.push(...)</c>.</summary>
    public sealed class CapturingLogSink : ILoggerSink
    {
        private readonly CaptureList<CapturedLog> _logs = new CaptureList<CapturedLog>();

        /// <summary>Everything logged so far, safe to assert on while the client is running.</summary>
        public CaptureList<CapturedLog> Logs => _logs;

        public void LogEvent(LogLevel level, string message) => _logs.Add(new CapturedLog(level, message));

        /// <summary>True if any line at <paramref name="level"/> contains <paramref name="text"/>.</summary>
        public bool Contains(LogLevel level, string text)
            => _logs.Any(log => log.Level == level && log.Message != null && log.Message.Contains(text));
    }
}
