using System;

namespace IO.Ably
{
    /// <summary>
    /// Extends the public 'ILogger' interface with additional internal capabilities.
    /// </summary>
    internal interface IInternalLogger : ILogger
    {
        /// <summary>
        /// Allows the caller to temporarily install a 'LogEvent' implementation that will be reverted
        /// when the returned context is disposed.
        /// </summary>
        /// <param name="sink">The 'LogEvent' to use until the context is disposed.</param>
        /// <returns>The returned context should be disposed to revert to the previous 'LogEvent' implementation.</returns>
        IDisposable CreateDisposableLoggingContext(ILoggerSink sink);
    }
}
