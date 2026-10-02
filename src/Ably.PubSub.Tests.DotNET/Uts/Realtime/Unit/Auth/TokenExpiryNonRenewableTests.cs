using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Auth
{
    /// <summary>
    /// Derived from uts/realtime/unit/auth/token_expiry_non_renewable_test.md in ably/specification.
    ///
    /// Spec points: RSA4a, RSA4a1, RSA4a2
    ///
    /// <para>
    /// A client built from a bare token with no key, authCallback or authUrl cannot renew it, so it
    /// has to say so at construction (RSA4a1) and treat the server's token error as fatal rather
    /// than retriable (RSA4a2).
    /// </para>
    ///
    /// <para>
    /// The spec's <c>logHandler</c> is <c>ClientOptions.LogHandler</c>, an <c>ILoggerSink</c>, which
    /// <see cref="CapturingLogSink"/> implements. Its <c>LogEvent(level, message)</c> carries no
    /// structured error code, so the "a message with error code 40171" assertion is made against
    /// the message text - which is what the spec's own fallback condition does too.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class TokenExpiryNonRenewableTests : UtsTestBase
    {
        public TokenExpiryNonRenewableTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RSA4a1/non-renewable-token-logs-warning-0
        [Fact]
        public void RSA4a1_NonRenewableTokenLogsWarning()
        {
            var sink = new CapturingLogSink();

            RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.Token = "non-renewable-token";
                options.Logger = InternalLogger.Create();
                options.LogHandler = sink;
                options.LogLevel = LogLevel.Debug;
            });

            var logs = sink.Snapshot();

            logs.Should().Contain(
                log => log.Message.Contains("40171")
                       || (log.Message.Contains("no means") && log.Message.Contains("renew")),
                "RSA4a1 - instantiation says that the token cannot be renewed");

            logs.Should().Contain(
                log => log.Message.Contains("help.ably.io/error/40171"),
                "TI5 - the message carries the help url");
        }

        // UTS: realtime/unit/RSA4a2/token-error-non-renewable-failed-0
        [Fact]
        public async Task RSA4a2_TokenErrorNonRenewableFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(
                    ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.Token = "non-renewable-token";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failedChanges = UtsClients.Snapshot(stateChanges)
                .Where(change => change.Current == ConnectionState.Failed)
                .ToList();

            // The spec asserts exactly one FAILED change, and that count is not stable here -
            // D21's FAILED re-entry lets the connection cycle back through CONNECTING and fail
            // again, so a slower run sees more than one. A transient DISCONNECTED can also precede
            // the FAILED, depending on whether the server's ERROR or its socket close is processed
            // first. What the requirement is actually about is that the outcome is FAILED with
            // 40171, which is asserted. The retry is the subject of the gated
            // RSA4a2_TokenErrorNonRenewableNoRetry below.
            failedChanges.Should().NotBeEmpty("not DISCONNECTED - this is not retriable");
            failedChanges[0].Reason.Should().NotBeNull();
            failedChanges[0].Reason.Code.Should().Be(40171, "RSA4a2");

            UtsClients.Snapshot(stateChanges)
                .Should().NotContain(
                    change => change.Current == ConnectionState.Suspended,
                    "RSA4a2 - a non-renewable token error is not retried into SUSPENDED");
        }

        // UTS: realtime/unit/RSA4a2/token-error-non-renewable-no-retry-1
        //
        // DEVIATION, and the clearest user-visible consequence of D21 found so far. RSA4a2 is
        // explicit that the library "should ... not retry the request" - a token it cannot renew
        // will not get better by being retried. The SDK reaches FAILED correctly (the test above
        // passes) and then re-enters CONNECTING on its own and attempts again. Measured: three
        // connection attempts where the spec requires one, with DisconnectedRetryTimeout pushed to
        // ten minutes so the ordinary retry timer is not the cause. See D21 in Uts/deviations.md.
        [DeviationFact]
        public async Task RSA4a2_TokenErrorNonRenewableNoRetry()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(
                    ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.Token = "non-renewable-token";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            connectionAttemptCount.Should().Be(1, "RSA4a2 - no retry");

            UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed)
                .Reason.Code.Should().Be(40171);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
