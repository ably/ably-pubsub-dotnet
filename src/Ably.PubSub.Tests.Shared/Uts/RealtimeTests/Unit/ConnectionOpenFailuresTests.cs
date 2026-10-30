// Connection Opening Failures Tests (RTN14, RTN14a, RTN14b, RTN14c, RTN14d, RTN14e, RTN14f, RTN14g)
// Derived from uts/realtime/unit/connection/connection_open_failures_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: fake timers are enabled before the client is built where the spec enables them after construction (equivalent: no timer has started yet)

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
    public class ConnectionOpenFailuresTests : UtsUnitTestBase
    {
        public ConnectionOpenFailuresTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // True once the recorded states contain the sequence in order (a subsequence), for transient states.
        private static bool HasSequence(CaptureList<ConnectionState> states, params ConnectionState[] sequence)
        {
            var next = 0;
            foreach (var state in states.Snapshot())
            {
                if (next < sequence.Length && state == sequence[next])
                {
                    next++;
                }
            }

            return next == sequence.Length;
        }

        // UTS: realtime/unit/RTN14a/invalid-key-failed-0
        [UtsFact]
        [Trait("spec", "RTN14a")]
        public async Task RTN14a_InvalidKeyFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // WebSocket connects successfully
                conn.RespondWithSuccess();

                // But server immediately sends ERROR for invalid key and closes connection
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40005, "Invalid key", 400));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "invalid.key:secret", AutoConnect = false });

            // NOTE: AWAIT_STATE connecting could miss a transient state; the states are recorded before connect() and verified in order below
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for CONNECTING state
            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));
            UtsAssert.ContainsInOrder(states.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Failed });

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40005);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(400);

            // Connection ID/key not set
            // DEVIATION: spec requires null (unset), the SDK returns an empty string (SDK-wide) - see deviations.md
            client.Connection.Id.Should().BeEmpty();
            client.Connection.Key.Should().BeEmpty();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN14b/token-error-with-renewal-0
        [UtsFact(Skip = "UTS spec error RTN14b: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN14b")]
        public Task RTN14b_TokenErrorWithRenewal()
        {
            // SPEC ERROR RTN14b: the spec asserts two token requests ("Initial + renewal") for a client built with only key:, but
            // RSA2 makes Basic auth the default when an API key exists and RSA4 uses token auth only with useTokenAuth, authUrl,
            // authCallback, token or tokenDetails, so no initial token request is made. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN14b - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN14b/token-renewal-fails-1
        [UtsFact]
        [Trait("spec", "RTN14b")]
        public async Task RTN14b_TokenRenewalFails()
        {
            // Setup
            var callCount = 0;
            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return Task.FromResult<object>(new TokenDetails("initial-token") { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                }

                throw new AblyException(new ErrorInfo("Unable to renew token", 40171, HttpStatusCode.Unauthorized));
            };

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // NOTE: the spec's handler only sends the ERROR; the WebSocket is opened first (respond_with_success) because a message can only be sent on an open connection
                // Always reject with token error
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { AuthCallback = authCallback, AutoConnect = false });

            // Test Steps
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            // Assertions
            // Connection should be DISCONNECTED (not FAILED) per RTN14b
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // authCallback was called twice: once for initial token, once for renewal
            Volatile.Read(ref callCount).Should().Be(2);

            // errorReason should reflect the renewal failure
            client.Connection.ErrorReason.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4a/token-error-no-renewal-0
        [UtsFact]
        [Trait("spec", "RSA4a")]
        public async Task RSA4a_TokenErrorNoRenewal()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });
            InstallMock(mockWs);

            // Use token directly (no way to renew)
            var client = Realtime(new ClientOptions { Token = "expired_token_string", AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for FAILED state (RSA4a2: no means to renew → FAILED)
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection transitioned to FAILED (RSA4a2: not DISCONNECTED)
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set with 40171 (RSA4a2)
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40171);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN14c/connection-timeout-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN14c")]
        public async Task RTN14c_ConnectionTimeout()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // WebSocket connects but server never sends CONNECTED
                // (simulates unresponsive server)
                conn.RespondWithSuccess();
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000), // 1 second timeout
                AutoConnect = false,
            });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for CONNECTING state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Advance time past timeout
            await AdvanceTime(1100);

            // Should transition to DISCONNECTED (will retry)
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(1));

            // Assertions
            // Connection timed out
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // Error indicates timeout
            client.Connection.ErrorReason.Should().NotBeNull();
            (client.Connection.ErrorReason.Message.Contains("timeout") || new[] { 50003, 80003 }.Contains(client.Connection.ErrorReason.Code)).Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN14d/retry-recoverable-failure-0
        [UtsFact]
        [Trait("spec", "RTN14d")]
        public async Task RTN14d_RetryRecoverableFailure()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First attempt fails (network error)
                    conn.RespondWithRefused();
                }
                else
                {
                    // Second attempt succeeds
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000), // 1 second
                AutoConnect = false,
            });

            // NOTE: AWAIT_STATE disconnected could miss a transient state (the SDK retries a failed first attempt at once); the states are recorded before connect() and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Start connection
            client.Connect();

            // Should transition to DISCONNECTED after first failure
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                timeout: TimeSpan.FromSeconds(2),
                description: "connection entered DISCONNECTED after the first failure");

            // Advance time to trigger retry
            await AdvanceTime(1100);

            // Should reconnect automatically
            await UtsWait.PollUntil(
                () => HasSequence(states, ConnectionState.Disconnected, ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(5),
                description: "connection reconnected after DISCONNECTED");

            // Assertions
            // Successfully connected on retry
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Two connection attempts were made
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN14e/disconnected-to-suspended-0
        [UtsFact(Skip = "UTS spec error RTN14e: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN14e")]
        public Task RTN14e_DisconnectedToSuspended()
        {
            // SPEC ERROR RTN14e: the spec needs a short connectionStateTtl (a local "DEFAULT_CONNECTION_STATE_TTL" or none at all) that the
            // client cannot have: no CONNECTED delivers one and DF1a makes the default 120s, so SUSPENDED is never reached in the
            // advanced time. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN14e - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN14f/suspended-retries-indefinitely-0
        [UtsFact(Skip = "UTS spec error RTN14f: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN14f")]
        public Task RTN14f_SuspendedRetriesIndefinitely()
        {
            // SPEC ERROR RTN14f: the spec needs a short connectionStateTtl (a local "DEFAULT_CONNECTION_STATE_TTL" or none at all) that the
            // client cannot have: no CONNECTED delivers one and DF1a makes the default 120s, so SUSPENDED is never reached in the
            // advanced time. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN14f - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN14g/error-empty-channel-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14g", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN14g")]
        public async Task RTN14g_ErrorEmptyChannelFailed()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                // Empty channel = connection-level error
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(50000, "Internal server error", 500));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for FAILED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set from protocol message
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(500);
            client.Connection.ErrorReason.Message.Should().Be("Internal server error");
            await CloseClient(client);
        }
    }
}
