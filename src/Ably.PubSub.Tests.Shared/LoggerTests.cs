using System;
using FluentAssertions;
using Xunit;

namespace IO.Ably.AcceptanceTests
{
    public sealed class LoggerTests : IDisposable
    {
        [Fact]
        public void TestLogger()
        {
            var sink = new TestLoggerSink();
            var logger = InternalLogger.Create();

            using (logger.CreateDisposableLoggingContext(null))
            {
                sink.LastLevel.Should().BeNull();
                sink.LastMessage.Should().BeNull();

                logger.LogLevel.Should().Be(Defaults.DefaultLogLevel);
                logger.LogLevel = LogLevel.Debug;

                // null destination shouldn't throw
                logger.LoggerSink = null;
                logger.Debug("msg");

                logger.LoggerSink = sink;

                // Basic messages
                logger.Error("Test Error Message");
                sink.LastLevel.Should().Be(LogLevel.Error);
                sink.LastMessage.Should().EndWith("Test Error Message");

                logger.Debug("Test Info Message");
                sink.LastLevel.Should().Be(LogLevel.Debug);
                sink.LastMessage.Should().EndWith("Test Info Message");

                logger.Debug("Test Debug Message");
                sink.LastLevel.Should().Be(LogLevel.Debug);
                sink.LastMessage.Should().EndWith("Test Debug Message");

                // Verify the log level works
                logger.LogLevel = LogLevel.Warning;
                logger.Error("Test Error Message");
                logger.Debug("Test Info Message");
                logger.Debug("Test Debug Message");
                sink.LastLevel.Should().Be(LogLevel.Error);
                sink.LastMessage.Should().EndWith("Test Error Message");

                // Revert the level
                logger.LogLevel = Defaults.DefaultLogLevel;
            }

            // test that the Logger gets instanced
            DefaultLogger.LoggerInstance.Should().NotBeNull();
        }

        [Fact]
        public void ClientOptionsWithNoLoggerSpecified_ShouldUseTheDefaultLogger()
        {
            var opts = new ClientOptions();
            Assert.Same(opts.Logger, DefaultLogger.LoggerInstance);
        }

        [Fact]
        public void LoggerInstances_ShouldNotInteract()
        {
            var logger1 = InternalLogger.Create();
            var logger2 = InternalLogger.Create();

            logger1.LogLevel.Should().Be(logger2.LogLevel);
            logger1.LogLevel = LogLevel.Debug;
            logger2.LogLevel = LogLevel.Error;

            logger1.LogLevel.Should().Be(LogLevel.Debug);
            logger2.LogLevel.Should().Be(LogLevel.Error);
            logger1.LogLevel.Should().NotBe(logger2.LogLevel);
        }

        public void Dispose()
        {
            DefaultLogger.LoggerSink = new DefaultLoggerSink();
        }
    }
}
