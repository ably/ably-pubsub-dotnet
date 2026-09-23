using System;

namespace IO.Ably
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1600:Elements should be documented",
        Justification = "Internal interface.")]
    internal interface ILogger
    {
        LogLevel LogLevel { get; set; }

        bool IsDebug { get; }

        ILoggerSink LoggerSink { get; set; }

        void Error(string message, Exception ex);

        void Error(string message, params object[] args);

        void Warning(string message, params object[] args);

        void Debug(string message, params object[] args);
    }
}
