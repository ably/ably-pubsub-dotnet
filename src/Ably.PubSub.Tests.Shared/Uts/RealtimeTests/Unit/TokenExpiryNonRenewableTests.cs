// Token Expiry with Non-Renewable Token Tests (RSA4a, RSA4a1, RSA4a2)
// Derived from uts/realtime/unit/auth/token_expiry_non_renewable_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: spec logLevel info is rendered as LogLevel.Debug (the SDK has no Info level)

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

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class TokenExpiryNonRenewableTests : UtsUnitTestBase
    {
        public TokenExpiryNonRenewableTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RSA4a1/non-renewable-token-logs-warning-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RSA4a1")]
        public async Task RSA4a1_NonRenewableTokenLogsWarning()
        {
            // Setup
            var logHandler = new CapturingLogSink();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Test Steps
            // Instantiate with token only — no key, no authCallback, no authUrl
            // NOTE: spec logLevel LOG_INFO is rendered as LogLevel.Debug (the SDK has no Info level); the message filter below keeps the spec's "info" level as Debug
            var client = Realtime(new ClientOptions
            {
                Token = "non-renewable-token",
                AutoConnect = false,
                LogHandler = logHandler,
                LogLevel = LogLevel.Debug,
            });

            // Assertions
            // A log message at info level with error code 40171 should have been emitted
            // DEVIATION: spec says info level (rendered as Debug), SDK logs the 40171 message at Warning - see deviations.md
            var infoMessages = logHandler.Logs.Snapshot().Where(m => m.Level == LogLevel.Warning).ToList();
            var has40171Message = infoMessages.Any(m =>
                m.Message.Contains("40171")
                || (m.Message.Contains("no means") && m.Message.Contains("renew")));
            has40171Message.Should().BeTrue();

            // TI5: log message should include the help URL
            var hasHelpUrl = infoMessages.Any(m => m.Message.Contains("https://help.ably.io/error/40171"));
            hasHelpUrl.Should().BeTrue();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4a2/token-error-non-renewable-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4a2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4a2")]
        public async Task RSA4a2_TokenErrorNonRenewableFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                // Server responds with token error (40142 = token expired)
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });
            InstallMock(mockWs);

            // Client with token only — no means to renew
            var client = Realtime(new ClientOptions
            {
                Token = "expired-token",
                AutoConnect = false,
            });

            // Test Steps
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection transitioned to FAILED (not DISCONNECTED — no retry)
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason has code 40171 (non-renewable token error)
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40171);

            // State change event also carries the error
            var failedChanges = stateChanges.Snapshot().Where(c => c.Current == ConnectionState.Failed).ToList();
            failedChanges.Should().HaveCount(1);
            failedChanges[0].Reason.Should().NotBeNull();
            failedChanges[0].Reason.Code.Should().Be(40171);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4a2/token-error-non-renewable-no-retry-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4a2")]
        public async Task RSA4a2_TokenErrorNonRenewableNoRetry()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess();

                // Always respond with token error
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40140, "Token error", 401));
            });
            InstallMock(mockWs);

            // Client with token only — no means to renew
            var client = Realtime(new ClientOptions
            {
                Token = "non-renewable-token",
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // Only one connection attempt was made (no retry)
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Connection is in FAILED state
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error code is 40171
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40171);

            await CloseClient(client);
        }
    }
}
