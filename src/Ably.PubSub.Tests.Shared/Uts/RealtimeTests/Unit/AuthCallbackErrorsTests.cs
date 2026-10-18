// Auth Callback Error Handling Tests (RSA4c, RSA4c2, RSA4c3, RSA4d, RSA4e, RSA4f)
// Derived from uts/realtime/unit/auth/auth_callback_errors_test.md
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
    public class AuthCallbackErrorsTests : UtsUnitTestBase
    {
        public AuthCallbackErrorsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RSA4c2/callback-error-connecting-disconnected-0
        [UtsFact]
        [Trait("spec", "RSA4c2")]
        public async Task RSA4c2_CallbackErrorConnectingDisconnected()
        {
            // Setup
            var authCallbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    if (count == 1)
                    {
                        throw new AblyException(new ErrorInfo("Auth server unavailable", 50000, HttpStatusCode.InternalServerError));
                    }

                    return Task.FromResult<object>(new TokenDetails("valid-token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            client.Connect();

            // authCallback fails on first attempt — connection should go to DISCONNECTED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4c2: Connection transitioned to DISCONNECTED (not FAILED — it's retriable)
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // RSA4c2: errorReason has code 80019 wrapping the underlying cause
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);

            // RSA4c2: cause is set to the underlying error from authCallback
            client.Connection.ErrorReason.Cause.Should().NotBeNull();
            client.Connection.ErrorReason.Cause.Code.Should().Be(50000);

            // State change event carries the same error
            var disconnectedChanges = stateChanges.Snapshot().Where(c => c.Current == ConnectionState.Disconnected).ToList();
            disconnectedChanges.Count.Should().BeGreaterThanOrEqualTo(1);
            disconnectedChanges[0].Reason.Should().NotBeNull();
            disconnectedChanges[0].Reason.Code.Should().Be(80019);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4c2/callback-timeout-connecting-disconnected-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4c2")]
        public async Task RSA4c2_CallbackTimeoutConnectingDisconnected()
        {
            // Setup
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                // Never returns — simulates a timeout
                AuthCallback = tokenParams => new TaskCompletionSource<object>().Task,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(10000),
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            client.Connect();

            // Advance time past realtimeRequestTimeout
            await AdvanceTime(11000);

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4c2: Connection transitioned to DISCONNECTED
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // RSA4c2: errorReason has code 80019
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4c3/callback-error-connected-stays-0
        [UtsFact(Skip = "UTS spec error RSA4c3: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSA4c3")]
        public Task RSA4c3_CallbackErrorConnectedStays()
        {
            // SPEC ERROR RSA4c3: the test asserts errorReason stays null after a failed RTN22 reauth while CONNECTED,
            // but features.md RSA4c1 says the 80019 ErrorInfo is "set as the connection errorReason" (it cites
            // specification#466, which this features.md does not contain). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA4c3 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RSA4d/callback-403-connecting-failed-0
        [UtsFact]
        [Trait("spec", "RSA4d")]
        public async Task RSA4d_Callback403ConnectingFailed()
        {
            // Setup
            var connectionAttempted = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Exchange(ref connectionAttempted, 1);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams => throw new AblyException(new ErrorInfo("Account disabled", 40300, HttpStatusCode.Forbidden)),
                AutoConnect = false,
            });

            // Test Steps
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            client.Connect();

            // authCallback returns 403 — connection should go directly to FAILED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4d: Connection went to FAILED (not DISCONNECTED)
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // No WebSocket connection was attempted (auth failed before transport)
            (Volatile.Read(ref connectionAttempted) == 1).Should().BeFalse();

            // RSA4d: ErrorInfo has code 80019 and statusCode 403
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(403);

            // Cause is the original 403 error
            client.Connection.ErrorReason.Cause.Should().NotBeNull();
            client.Connection.ErrorReason.Cause.Code.Should().Be(40300);
            ((int?)client.Connection.ErrorReason.Cause.StatusCode).Should().Be(403);

            // State change event carries the error
            var failedChanges = stateChanges.Snapshot().Where(c => c.Current == ConnectionState.Failed).ToList();
            failedChanges.Should().HaveCount(1);
            failedChanges[0].Reason.Should().NotBeNull();
            failedChanges[0].Reason.Code.Should().Be(80019);
            ((int?)failedChanges[0].Reason.StatusCode).Should().Be(403);

            // No DISCONNECTED state was reached (went directly to FAILED)
            var disconnectedChanges = stateChanges.Snapshot().Where(c => c.Current == ConnectionState.Disconnected).ToList();
            disconnectedChanges.Should().BeEmpty();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4d/callback-403-reauth-failed-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4d")]
        public async Task RSA4d_Callback403ReauthFailed()
        {
            // Setup
            var authCallbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    if (count == 1)
                    {
                        // First call succeeds (initial connection)
                        return Task.FromResult<object>(new TokenDetails("initial-token") { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                    }

                    // Reauth fails with 403
                    throw new AblyException(new ErrorInfo("Account suspended", 40300, HttpStatusCode.Forbidden));
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Server requests re-authentication (RTN22)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            // authCallback returns 403 — connection should go to FAILED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4d: FAILED with code 80019 and statusCode 403
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(403);
            client.Connection.ErrorReason.Cause.Should().NotBeNull();
            client.Connection.ErrorReason.Cause.Code.Should().Be(40300);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4f/callback-invalid-type-format-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4f", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4f")]
        public async Task RSA4f_CallbackInvalidTypeFormat()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                // Return an invalid type — an integer is not a valid token format
                AuthCallback = tokenParams => Task.FromResult<object>(12345),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();

            // Invalid format from authCallback — connection should go to DISCONNECTED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4c2: Connection transitioned to DISCONNECTED
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // RSA4c2: errorReason has code 80019
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4f/callback-oversized-token-format-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4f", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4f")]
        public async Task RSA4f_CallbackOversizedTokenFormat()
        {
            // Setup
            // Generate a token string larger than 128KiB (131072 bytes)
            var oversizedToken = new string('x', 131073);

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // NOTE: a string return is read as a TokenRequest (RSA8d), so the token string is wrapped in TokenDetails to test the 128KiB rule itself
            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(oversizedToken)),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();

            // Oversized token — connection should go to DISCONNECTED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4c2: Connection transitioned to DISCONNECTED
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            // RSA4c2: errorReason has code 80019
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4e/rest-callback-error-40170-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4e", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4e")]
        public async Task RSA4e_RestCallbackError40170()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JArray()));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                // Generic error — not an explicit ErrorInfo from Ably
                AuthCallback = tokenParams => throw new Exception("Network failure connecting to auth server"),
            });

            // Test Steps
            // Attempt a REST request that requires authentication
            var channel = client.Channels.Get("test-channel");
            Func<Task> act = () => channel.StatusAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            // RSA4e: Error has code 40170 and statusCode 401
            error.Code.Should().Be(40170);
            ((int?)error.StatusCode).Should().Be(401);

            // Error message should be descriptive
            error.Message.Should().NotBeNull();
            error.Message.Length.Should().BeGreaterThan(0);
        }
    }
}
