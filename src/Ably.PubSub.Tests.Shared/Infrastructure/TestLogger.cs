using System;

namespace IO.Ably.Tests
{
    /// <summary>
    /// A test logger to check if a message has been logged.
    /// Uses string.StartsWith, so partial matches on the start of a string are valid.
    /// </summary>
    internal class TestLogger : ILogger
    {
        public int SeenCount { get; set; }

        public string MessageToTest { get; set; }

        public string FullMessage { get; private set; }

        public TestLogger(string messageToTest)
        {
            LogLevel = LogLevel.Debug;
            MessageToTest = messageToTest;
            SeenCount = 0;
        }

        public bool MessageSeen { get; private set; }

        public LogLevel LogLevel { get; set; }

        public bool IsDebug => LogLevel == LogLevel.Debug;

        public ILoggerSink LoggerSink { get; set; }

        public void Error(string message, Exception ex)
        {
            Test(message);
            Test(ex.Message);
        }

        public void Error(string message, params object[] args)
        {
            Test(message);
        }

        public void Warning(string message, params object[] args)
        {
            Test(message);
        }

        public void Debug(string message, params object[] args)
        {
            Test(message);
        }

        private void Test(string message)
        {
            if (message.StartsWith(MessageToTest))
            {
                MessageSeen = true;
                FullMessage = message;
                SeenCount++;
            }
        }

        public void Reset()
        {
            MessageSeen = false;
        }
    }
}
