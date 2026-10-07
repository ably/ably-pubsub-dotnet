using System.Collections.Generic;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// One entry of the specs' <c>captured_logs</c>.
    ///
    /// <para>
    /// The specs write their log handler as <c>(level, message, context)</c>. The third field has
    /// nothing to hold it here: <c>ILoggerSink.LogEvent</c> is handed a level and a message and
    /// nothing else, so there is no structured context to assert on and the message text carries
    /// whatever the spec looks for in it.
    /// </para>
    /// </summary>
    public sealed class CapturedLog
    {
        internal CapturedLog(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Message { get; }
    }

    /// <summary>
    /// The specs' <c>logHandler: (level, message) =&gt; captured_logs.push(...)</c>, installed
    /// through <c>ClientOptions.LogHandler</c>.
    ///
    /// <para>
    /// Locked, because the SDK logs from whichever thread completes the work being logged - a
    /// request continuation for the REST tier, the realtime workflow's reader thread for the
    /// realtime one.
    /// </para>
    /// </summary>
    public sealed class CapturingLogSink : ILoggerSink
    {
        private readonly List<CapturedLog> _logs = new List<CapturedLog>();

        public void LogEvent(LogLevel level, string message)
        {
            lock (_logs)
            {
                _logs.Add(new CapturedLog(level, message));
            }
        }

        /// <summary>The specs' <c>captured_logs</c>, copied so it is safe to assert on.</summary>
        public List<CapturedLog> Snapshot()
        {
            lock (_logs)
            {
                return new List<CapturedLog>(_logs);
            }
        }
    }
}
