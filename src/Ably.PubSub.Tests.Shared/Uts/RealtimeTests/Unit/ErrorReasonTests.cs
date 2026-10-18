// Connection errorReason Tests (RTN25)
// Derived from uts/realtime/unit/connection/error_reason_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    public class ErrorReasonTests : UtsUnitTestBase
    {
        public ErrorReasonTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN25/error-reason-on-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonOnFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40005, "Invalid API key", 400));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "invalid.key:secret", AutoConnect = false });

            // Test Steps
            // Initially errorReason should be null
            client.Connection.ErrorReason.Should().BeNull();

            // Start connection
            client.Connect();

            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // errorReason is set with error details
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40005);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(400);
            client.Connection.ErrorReason.Message.Should().Be("Invalid API key");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN25/error-reason-disconnected-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonDisconnected()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Connection attempt fails
                conn.RespondWithRefused();
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for DISCONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // errorReason is set
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Message.Should().NotBeNull();

            // Error indicates connection failure
            // (Exact error code/message depends on implementation)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN25/error-reason-suspended-2
        [UtsFact(Skip = "UTS spec error RTN25: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN25")]
        public Task RTN25_ErrorReasonSuspended()
        {
            // SPEC ERROR RTN25: the spec needs a short connectionStateTtl (a local "DEFAULT_CONNECTION_STATE_TTL" or none at all) that the
            // client cannot have: no CONNECTED delivers one and DF1a makes the default 120s, so SUSPENDED is never reached in the
            // advanced time. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN25 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN25/error-reason-token-error-3
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonTokenError()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });
            InstallMock(mockWs);

            // Use token directly (no way to renew) — RSA4a applies
            var client = Realtime(new ClientOptions { Token = "expired_token", AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Per RSA4a2: no means to renew → FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // errorReason should indicate no means to renew (RSA4a2: error code 40171)
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40171);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN25/error-reason-cleared-on-connect-4
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonClearedOnConnect()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First attempt fails
                    conn.RespondWithRefused();
                }
                else
                {
                    // Second attempt succeeds
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            // NOTE: EnableFakeTimers is called before the client is built (a timer already running would stay on the real clock); the spec calls it first in Test Steps
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100),
                AutoConnect = false,
            });

            // Test Steps
            // Start connection (will fail initially)
            client.Connect();

            // Wait for DISCONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // errorReason should be set after failure
            client.Connection.ErrorReason.Should().NotBeNull();
            var failureError = client.Connection.ErrorReason;
            failureError.Should().NotBeNull();

            // Advance time to trigger retry
            await AdvanceTime(150);

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Assertions
            // errorReason should be cleared after successful connection
            // Note: Specification doesn't explicitly require this, but it's common practice
            // Some implementations may keep the last error for debugging purposes
            // Verify implementation behavior:

            // Either:
            // A) errorReason is cleared on successful connection
            // NOTE: the spec offers two outcomes (A cleared, B kept); outcome A is asserted
            client.Connection.ErrorReason.Should().BeNull();

            // Or:
            // B) errorReason is kept but clearly not relevant to current state
            // (Implementation-specific behavior)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN25/error-reason-protocol-error-5
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14g", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonProtocolError()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                // Empty channel = connection-level error
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(50000, "Internal server error", 500));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // errorReason is set from ERROR protocol message
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(500);
            client.Connection.ErrorReason.Message.Should().Be("Internal server error");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN25/error-reason-in-state-change-6
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN25")]
        public async Task RTN25_ErrorReasonInStateChange()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40003, "Access token invalid", 400));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Track state changes
            var stateChanges = new CaptureList<ConnectionStateChange>();

            client.Connection.On(ConnectionEvent.Failed, change => stateChanges.Add(change));

            // Start connection
            client.Connect();

            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // State change event was emitted
            stateChanges.Count.Should().Be(1);

            var change = stateChanges[0];

            // State change has reason populated
            change.Reason.Should().NotBeNull();
            change.Reason.Code.Should().Be(40003);
            ((int?)change.Reason.StatusCode).Should().Be(400);
            change.Reason.Message.Should().Be("Access token invalid");

            // Connection errorReason matches state change reason
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(change.Reason.Code);
            client.Connection.ErrorReason.Message.Should().Be(change.Reason.Message);
            await CloseClient(client);
        }
    }
}
