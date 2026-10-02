using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/logging.md in ably/specification.
    ///
    /// Spec points: RSC2, RSC2b, TO3b, TO3c
    ///
    /// Three structural differences between the spec's logging API and this SDK's are settled once
    /// here rather than restated in every test:
    ///
    /// 1. The spec's handler is <c>LogHandler(level, message, context)</c>. The .NET sink is
    ///    <c>ILoggerSink.LogEvent(LogLevel level, string message)</c> — there is no structured context
    ///    map anywhere in the SDK. TO3c's assertion on <c>log.context</c> is therefore adapted to the
    ///    fields the API does carry, and <c>rest/unit/TO3c2/context-contains-expected-keys-0</c>, whose
    ///    only assertions are on context keys, is not translated at all.
    /// 2. This SDK's <c>LogLevel</c> is <c>Debug | Warning | Error | None</c>: no Info and no Verbose.
    ///    The spec's <c>verbose</c> is this SDK's <c>Debug</c>, documented as "logs everything"; its
    ///    <c>info</c> has no counterpart, so TO3b's two <c>level == info</c> assertions are dropped
    ///    with a note at the site.
    /// 3. The SDK has no per-client logger: <c>ClientOptions.Logger</c> defaults to the process-wide
    ///    <c>DefaultLogger.LoggerInstance</c> and <c>PubSubHttpClient.InitializeAbly</c> writes this
    ///    client's <c>LogLevel</c> and <c>LogHandler</c> onto whichever instance it is handed. Every
    ///    test here hands it one of its own, for the reason given on <see cref="LoggingClient"/>.
    ///
    /// The spec file's header also lists RSC3 and RSC4, for which it defines no tests.
    /// </summary>
    public class LoggingTests : UtsTestBase
    {
        private const long ServerTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

        public LoggingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC2/default-log-level-warn-0
        [Fact]
        public async Task RSC2_DefaultLogLevelWarn()
        {
            var sink = new CapturingLogSink();
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            // No logLevel is passed, so ClientOptions.LogLevel keeps its default.
            var client = LoggingClient(mockHttp, sink);

            await client.TimeAsync();

            var capturedLogs = sink.Snapshot();

            // The spec's assertion: every captured event is error or warn.
            capturedLogs
                .Where(log => log.Level != LogLevel.Warning && log.Level != LogLevel.Error)
                .Should()
                .BeEmpty();

            // The spec's requirement line, asserted directly, because the assertion above is weak on
            // this SDK: the successful /time path logs nothing at or above Warning (TO3b below captures
            // the debug traffic the same call produces once the level is lowered), so the "ALL" check
            // would also hold over an empty list.
            client.Options.LogLevel.Should().Be(LogLevel.Warning);
        }

        // UTS: rest/unit/TO3b/log-level-changeable-0
        [Fact]
        public async Task TO3b_LogLevelChangeable()
        {
            var sink = new CapturingLogSink();
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            // The spec's logLevel of verbose. This SDK's lowest level is Debug, documented as the
            // verbose setting that logs everything; there is no separate Verbose.
            var client = LoggingClient(mockHttp, sink, LogLevel.Debug);

            await client.TimeAsync();

            var capturedLogs = sink.Snapshot();

            // The spec's two "level == info" assertions have no counterpart: there is no Info in this
            // SDK's LogLevel. What the spec point is about — the level being changeable away from the
            // default Warning — is carried by the debug traffic now reaching the handler, none of which
            // RSC2_DefaultLogLevelWarn sees.
            var debugLogs = capturedLogs.Where(log => log.Level == LogLevel.Debug).ToList();
            debugLogs.Should().NotBeEmpty();

            // The spec looks for a debug log for the HTTP request by matching the literal text
            // "HTTP request". This SDK words the same event "Sending GET request to /time"
            // (PubSubHttpClient.ExecuteRequest) and "Executing request. Host: ... Request: /time"
            // (AblyHttpRequester.Execute), so the request path is what identifies it.
            debugLogs.Should().Contain(log => log.Message.Contains("/time"));
        }

        // UTS: rest/unit/TO3c/custom-handler-structured-events-0
        [Fact]
        public async Task TO3c_CustomHandlerStructuredEvents()
        {
            var sink = new CapturingLogSink();
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            // The spec's logLevel of info. Debug is the only level below Warning in this SDK, so it is
            // what makes the handler see anything at all on a successful request.
            var client = LoggingClient(mockHttp, sink, LogLevel.Debug);

            await client.TimeAsync();

            var capturedLogs = sink.Snapshot();

            // The custom handler was called.
            capturedLogs.Should().NotBeEmpty();

            // ADAPTED: the spec asserts "ANY log: log.context IS NOT EMPTY". There is no context map in
            // this SDK — ILoggerSink.LogEvent takes (LogLevel, string) and nothing else — so what is
            // asserted instead is that the events arrive carrying the two structured fields the API
            // does define: the event's own level, and a non-empty message.
            capturedLogs.Should().Contain(log => log.Level == LogLevel.Debug);
            capturedLogs.Where(log => string.IsNullOrWhiteSpace(log.Message)).Should().BeEmpty();
        }

        // UTS: rest/unit/RSC2b/log-level-none-suppresses-all-0
        [Fact]
        public async Task RSC2b_LogLevelNoneSuppressesAll()
        {
            var sink = new CapturingLogSink();
            var mockHttp = new MockHttpClient(
                onRequest: req => req.RespondWith(200, new object[] { ServerTimeMs }));

            var client = LoggingClient(mockHttp, sink, LogLevel.None);

            await client.TimeAsync();

            sink.Snapshot().Should().BeEmpty();
        }

        /// <summary>
        /// The spec's <c>Rest(options: ClientOptions(logHandler: ..., logLevel: ...))</c>, plus one
        /// adaptation.
        ///
        /// <c>ClientOptions.Logger</c> is not set by the spec and defaults to the process-wide
        /// <c>DefaultLogger.LoggerInstance</c>, which <c>InitializeAbly</c> then mutates with this
        /// client's level and handler. Left on the singleton these tests would be at the mercy of any
        /// other test constructing a client at the same time — <c>xunit.runner.json</c> runs two
        /// collections in parallel, and every client construction resets that one instance's level to
        /// its own <c>ClientOptions.LogLevel</c>, which would silently empty the debug expectations
        /// below. Each test therefore gets its own logger instance through the internal
        /// <c>ClientOptions.Logger</c> seam, which is how the SDK's own logging tests isolate
        /// themselves too. The path under test is unchanged: <c>InitializeAbly</c> still applies
        /// <c>LogLevel</c> and <c>LogHandler</c> to it, and <c>AblyHttpOptions</c> still hands the same
        /// instance to <c>AblyHttpRequester</c>, which is what logs the request.
        /// </summary>
        private PubSubHttpClient LoggingClient(
            MockHttpClient mockHttp,
            CapturingLogSink sink,
            LogLevel? logLevel = null)
        {
            return RestClient(mockHttp, configure: options =>
            {
                options.Logger = InternalLogger.Create();
                options.LogHandler = sink;

                if (logLevel.HasValue)
                {
                    options.LogLevel = logLevel.Value;
                }
            });
        }
    }
}
