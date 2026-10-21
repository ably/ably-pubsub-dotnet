// Logging Tests (RSC2, RSC3, RSC4, TO3b, TO3c)
// Derived from uts/rest/unit/logging.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: logHandler is a CapturingLogSink (ILoggerSink has no context argument); info and verbose map to LogLevel.Debug (rest-mapping.md section 3)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class LoggingTests : UtsUnitTestBase
    {
        public LoggingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC2/default-log-level-warn-0
        [UtsFact]
        [Trait("spec", "RSC2")]
        public async Task RSC2_DefaultLogLevelWarn()
        {
            // Setup
            var capturedLogs = new CapturingLogSink();

            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new[] { 1704067200000L }));

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "app.key:secret",
                LogHandler = capturedLogs,
            });

            // Test Steps
            await client.TimeAsync();

            // Default level is warn, so info/debug/verbose messages should be filtered
            capturedLogs.Logs.Snapshot().Should().NotContain(log => !(log.Level == LogLevel.Error || log.Level == LogLevel.Warning));
        }

        // UTS: rest/unit/TO3b/log-level-changeable-0
        // DEVIATION: see deviations.md
        [DeviationFact("TO3b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TO3b")]
        public async Task TO3b_LogLevelChangeable()
        {
            // Setup
            var capturedLogs = new CapturingLogSink();

            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new[] { 1704067200000L }));

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            // NOTE: logLevel verbose is LogLevel.Debug: the SDK has no Verbose level
            var client = Rest(new ClientOptions
            {
                Key = "app.key:secret",
                LogLevel = LogLevel.Debug,
                LogHandler = capturedLogs,
            });

            // Test Steps
            await client.TimeAsync();

            // With verbose, should have info+debug+verbose messages
            // NOTE: the SDK has no Info level: info is read as LogLevel.Debug (rest-mapping.md section 3), the nearest level
            var infoLogs = capturedLogs.Logs.Snapshot().Where(l => l.Level == LogLevel.Debug).ToList();
            infoLogs.Count.Should().BeGreaterThan(0);

            // Must have an info log for the time() method entry (not checked directly,
            // but client creation emits "Client created" at info level)
            capturedLogs.Logs.Snapshot().Should().Contain(log => log.Level == LogLevel.Debug);

            // Must have a debug log for the HTTP request
            var debugLogs = capturedLogs.Logs.Snapshot().Where(l => l.Level == LogLevel.Debug).ToList();
            debugLogs.Should().Contain(log => log.Message.Contains("HTTP request"));
        }

        // UTS: rest/unit/TO3c/custom-handler-structured-events-0
        [UtsFact]
        [Trait("spec", "TO3c")]
        public async Task TO3c_CustomHandlerStructuredEvents()
        {
            // Setup
            var capturedLogs = new CapturingLogSink();

            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new[] { 1704067200000L }));

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var handler = capturedLogs;

            // NOTE: logLevel info is LogLevel.Debug: the SDK has no Info level (rest-mapping.md section 3)
            var client = Rest(new ClientOptions
            {
                Key = "app.key:secret",
                LogLevel = LogLevel.Debug,
                LogHandler = handler,
            });

            // Test Steps
            await client.TimeAsync();

            // Custom handler was called
            capturedLogs.Logs.Count.Should().BeGreaterThan(0);

            // Structured context is provided
            // NOTE: no assertion: ILoggerSink.LogEvent(level, message) has no context argument (README Known gaps: (level, message, context))
            // ASSERT ANY log IN captured_logs: log.context IS NOT EMPTY
        }

        // UTS: rest/unit/TO3c2/context-contains-expected-keys-0
        // DEVIATION: see deviations.md
        [DeviationFact("TO3c2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TO3c2")]
        public async Task TO3c2_ContextContainsExpectedKeys()
        {
            // Setup
            var capturedLogs = new CapturingLogSink();

            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new[] { 1704067200000L }));

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "app.key:secret",
                LogLevel = LogLevel.Debug,
                LogHandler = capturedLogs,
            });

            // Test Steps
            await client.TimeAsync();

            // Find the HTTP request log
            var httpLogs = capturedLogs.Logs.Snapshot().Where(l => l.Message.Contains("HTTP request") && l.Level == LogLevel.Debug).ToList();
            httpLogs.Count.Should().BeGreaterThanOrEqualTo(1);

            // NOTE: no assertions: ILoggerSink.LogEvent(level, message) has no context argument (README Known gaps: (level, message, context))
            // ASSERT "method" IN http_logs[0].context
            // ASSERT "host" IN http_logs[0].context
            // ASSERT "path" IN http_logs[0].context
        }

        // UTS: rest/unit/RSC2b/log-level-none-suppresses-all-0
        [UtsFact]
        [Trait("spec", "RSC2b")]
        public async Task RSC2b_LogLevelNoneSuppressesAll()
        {
            // Setup
            var capturedLogs = new CapturingLogSink();

            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new[] { 1704067200000L }));

            // NOTE: the spec omits install_mock(mock_http) here; the mock is installed so the client uses it
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "app.key:secret",
                LogLevel = LogLevel.None,
                LogHandler = capturedLogs,
            });

            // Test Steps
            await client.TimeAsync();

            // No logs should be captured
            capturedLogs.Logs.Count.Should().Be(0);
        }
    }
}
