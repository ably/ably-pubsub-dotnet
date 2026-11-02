// Realtime Connection Authentication Tests (RTN2e, RTN27b, RSA4, RSA4c, RSA4c1, RSA4c2, RSA4c3, RSA4d, RSA8d, RSA12a)
// Derived from uts/realtime/unit/auth/connection_auth_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving) where the spec closes the client and awaits CLOSED

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class ConnectionAuthTests : UtsUnitTestBase
    {
        public ConnectionAuthTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN2e/token-before-websocket-0
        [UtsFact]
        [Trait("spec", "RTN2e")]
        public async Task RTN2e_TokenBeforeWebsocket()
        {
            // Setup
            var callbackInvoked = 0;
            long callbackInvokedTime = 0;
            long connectionAttemptTime = 0;
            RecordedUrl capturedWsUrl = null;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Volatile.Write(ref connectionAttemptTime, Stopwatch.GetTimestamp());
                Volatile.Write(ref capturedWsUrl, conn.Url);

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Client with authCallback but NO existing token
            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    Interlocked.Exchange(ref callbackInvoked, 1);
                    Volatile.Write(ref callbackInvokedTime, Stopwatch.GetTimestamp());
                    return Task.FromResult<object>(new TokenDetails("callback-provided-token") { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Assertions
            // authCallback was invoked
            (Volatile.Read(ref callbackInvoked) == 1).Should().BeTrue();

            // authCallback was invoked BEFORE WebSocket connection attempt
            Volatile.Read(ref callbackInvokedTime).Should().BeLessThan(Volatile.Read(ref connectionAttemptTime));

            // WebSocket URL contains the token from authCallback
            var url = Volatile.Read(ref capturedWsUrl);
            url.QueryParams["accessToken"].Should().Be("callback-provided-token");

            // WebSocket URL does NOT contain a key parameter (using token auth, not basic auth)
            url.QueryParams.ContainsKey("key").Should().BeFalse();

            // Connection succeeded
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN2e/callback-error-prevents-connect-1
        [UtsFact]
        [Trait("spec", "RTN2e")]
        public async Task RTN2e_CallbackErrorPreventsConnect()
        {
            // Setup
            var connectionAttempted = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Exchange(ref connectionAttempted, 1);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams => throw new Exception("Auth callback failed"),
                AutoConnect = false,
            });

            // Test Steps
            // NOTE: AWAIT_STATE IN [disconnected, failed]: the states are recorded before connect() and polled
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Start connection
            client.Connect();

            // Wait for DISCONNECTED or FAILED state
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected || s == ConnectionState.Failed),
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(50),
                description: "connection entered DISCONNECTED or FAILED");

            // Assertions
            // No WebSocket connection was attempted
            (Volatile.Read(ref connectionAttempted) == 1).Should().BeFalse();

            // Error reason is set
            client.Connection.ErrorReason.Should().NotBeNull();
            (((int?)client.Connection.ErrorReason.StatusCode) == 401 || client.Connection.ErrorReason.Code == 40170).Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN2e/callback-params-include-clientid-2
        [UtsFact]
        [Trait("spec", "RTN2e")]
        public async Task RTN2e_CallbackParamsIncludeClientid()
        {
            // Setup
            TokenParams receivedParams = null;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    Volatile.Write(ref receivedParams, tokenParams);
                    return Task.FromResult<object>(new TokenDetails("token-for-client")
                    {
                        Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                        ClientId = "my-client-id",
                    });
                },
                ClientId = "my-client-id",
                AutoConnect = false,
            });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for CONNECTED state
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(5));

            // Assertions
            // authCallback received TokenParams with clientId
            var received = Volatile.Read(ref receivedParams);
            received.Should().NotBeNull();
            received.ClientId.Should().Be("my-client-id");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN2e/reuse-valid-token-3
        [UtsFact]
        [Trait("spec", "RTN2e")]
        public async Task RTN2e_ReuseValidToken()
        {
            // Setup
            var callbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });

            // Disclosures: the mock answers CLOSE with CLOSED (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    Interlocked.Increment(ref callbackCount);

                    // Valid for 1 hour
                    return Task.FromResult<object>(new TokenDetails("reusable-token") { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // First connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Disconnect
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Second connection
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // authCallback was only invoked once (token was reused)
            Volatile.Read(ref callbackCount).Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4c2/callback-error-causes-disconnected-0
        [UtsFact]
        [Trait("spec", "RSA4c2")]
        public async Task RSA4c2_CallbackErrorCausesDisconnected()
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
            client.Connect();

            // authCallback fails on first attempt — connection should go to DISCONNECTED
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected, TimeSpan.FromSeconds(5));

            // Assertions
            // RSA4c1: errorReason has code 80019 wrapping the underlying cause
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);
            client.Connection.ErrorReason.Cause.Should().NotBeNull();
            client.Connection.ErrorReason.Cause.Code.Should().Be(50000);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4c3/callback-error-stays-connected-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4c3", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4c3")]
        public async Task RSA4c3_CallbackErrorStaysConnected()
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

                    // Subsequent calls fail (reauth)
                    throw new AblyException(new ErrorInfo("Auth server unavailable", 50000, HttpStatusCode.InternalServerError));
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record state changes
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // Server requests re-authentication (RTN22)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            // Wait for errorReason to be set (auth failure propagates asynchronously)
            await UtsWait.PollUntil(
                () => client.Connection.ErrorReason != null,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(50),
                description: "connection errorReason is set");

            // Assertions
            // RSA4c3: Connection remains CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // No state transitions away from connected occurred
            var nonConnectedChanges = stateChanges.Snapshot().Where(c => c.Current != ConnectionState.Connected).ToList();
            nonConnectedChanges.Should().BeEmpty();

            // RSA4c1: errorReason has code 80019 wrapping the underlying cause
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int?)client.Connection.ErrorReason.StatusCode).Should().Be(401);
            client.Connection.ErrorReason.Cause.Should().NotBeNull();
            client.Connection.ErrorReason.Cause.Code.Should().Be(50000);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RSA4d/callback-403-causes-failed-0
        [UtsFact]
        [Trait("spec", "RSA4d")]
        public async Task RSA4d_Callback403CausesFailed()
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
                AuthCallback = tokenParams => throw new AblyException(new ErrorInfo("Account disabled", 40300, HttpStatusCode.Forbidden)),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();

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

        // UTS: realtime/unit/RSA4d/callback-403-reauth-causes-failed-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSA4d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA4d")]
        public async Task RSA4d_Callback403ReauthCausesFailed()
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
    }
}
