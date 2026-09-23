using System;
using System.Diagnostics;
using System.Text;

namespace IO.Ably
{
    internal class InternalLogger : IInternalLogger
    {
        private InternalLogger()
            : this(Defaults.DefaultLogLevel, new DefaultLoggerSink())
        {
        }

        private InternalLogger(LogLevel logLevel, ILoggerSink loggerSink, Func<DateTimeOffset> nowProvider = null)
        {
            LogLevel = logLevel;
            LoggerSink = loggerSink;
            Now = nowProvider ?? Defaults.NowFunc();
        }

        internal static IInternalLogger Create()
        {
            return new InternalLogger();
        }

        internal static IInternalLogger Create(LogLevel logLevel, ILoggerSink loggerSink)
        {
            return new InternalLogger(logLevel, loggerSink);
        }

        /// <summary>Maximum level to log.</summary>
        /// <remarks>E.g. set to LogLevel.Warning to have only errors and warnings in the log.</remarks>
        public LogLevel LogLevel { get; set; }

        public ILoggerSink LoggerSink { get; set; }

        public bool IsDebug => LogLevel == LogLevel.Debug;

        private Func<DateTimeOffset> Now { get; }

        public IDisposable CreateDisposableLoggingContext(ILoggerSink loggerSink)
        {
            ILoggerSink oldLoggerSink = LoggerSink;
            LoggerSink = loggerSink;
            return new ActionOnDispose(() => LoggerSink = oldLoggerSink);
        }

        /// <summary>Log an error message.</summary>
        public void Error(string message, Exception ex)
        {
            Message(LogLevel.Error, $"{message} {GetExceptionDetails(ex)}");
        }

        /// <summary>Log an error message.</summary>
        public void Error(string message, params object[] args)
        {
            Message(LogLevel.Error, message, args);
        }

        /// <summary>Log a warning message.</summary>
        public void Warning(string message, params object[] args)
        {
            Message(LogLevel.Warning, message, args);
        }

        /// <summary>Log a debug message.</summary>
        public void Debug(string message, params object[] args)
        {
            Message(LogLevel.Debug, message, args);
        }

        private void Message(LogLevel level, string message, params object[] args)
        {
            try
            {
                ILoggerSink loggerSink = LoggerSink;
                if (LogLevel == LogLevel.None || level < LogLevel || loggerSink == null)
                {
                    return;
                }

                var timeStamp = GetLogMessagePrefix();
                if (args == null || args.Length == 0)
                {
                    loggerSink.LogEvent(level, timeStamp + " " + message);
                }
                else
                {
                    loggerSink.LogEvent(level, timeStamp + " " + string.Format(message, args));
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Error logging message. Error: {e.Message}");
            }
        }

        private string GetLogMessagePrefix()
        {
            var timeStamp = Now().ToString("hh:mm:ss.fff");
            return $"{timeStamp}";
        }

        /// <summary>Produce long multiline string with the details about the exception, including inner exceptions, if any.</summary>
        private static string GetExceptionDetails(Exception ex)
        {
            try
            {
                if (ex == null)
                {
                    return "No exception information";
                }

                var message = new StringBuilder();

                if (ex is AblyException ablyException)
                {
                    message.AppendLine($"Error code: {ablyException.ErrorInfo.Code}")
                        .AppendLine($"Status code: {ablyException.ErrorInfo.StatusCode}")
                        .AppendLine($"Reason: {ablyException.ErrorInfo.Message}");
                }

                message.AppendLine(ex.Message)
                    .AppendLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    message.AppendLine("Inner exception:")
                        .AppendLine(GetExceptionDetails(ex.InnerException));
                }

                return message.ToString();
            }
            catch (Exception e)
            {
                return $"Error getting exception details. Error: {e.Message}";
            }
        }
    }
}
