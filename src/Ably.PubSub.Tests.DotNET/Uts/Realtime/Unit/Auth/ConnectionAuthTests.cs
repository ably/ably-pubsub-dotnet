using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Auth
{
    /// <summary>
    /// Derived from uts/realtime/unit/auth/connection_auth_test.md in ably/specification.
    ///
    /// Spec points: RTN2e, RTN27b, RSA4c2, RSA4c3, RSA4d
    ///
    /// <para>
    /// The ordering requirement is the subject here: a token has to be in hand before the transport
    /// is opened, because RTN2e puts it in the connect URL.
    /// </para>
    ///
    /// <para>
    /// This file's last four tests repeat the RSA4c2/RSA4c3/RSA4d scenarios from
    /// <see cref="AuthCallbackErrorsTests"/> under their own spec test ids. They are translated
    /// rather than cross-referenced so that every UTS test id has a derived test, and kept to the
    /// assertions their own sections make - which are a subset of the other file's.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionAuthTests : UtsTestBase
    {
        public ConnectionAuthTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN2e/token-before-websocket-0
        [Fact]
        public async Task RTN2e_TokenObtainedBeforeWebSocket()
        {
            var callbackInvoked = false;
            DateTimeOffset? callbackInvokedTime = null;
            DateTimeOffset? connectionAttemptTime = null;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptTime = DateTimeOffset.UtcNow;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackInvoked = true;
                    callbackInvokedTime = DateTimeOffset.UtcNow;
                    return Task.FromResult<object>(new TokenDetails("callback-provided-token")
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            callbackInvoked.Should().BeTrue();
            callbackInvokedTime.Should().NotBeNull();
            connectionAttemptTime.Should().NotBeNull();
            callbackInvokedTime.Value.Should().BeOnOrBefore(
                connectionAttemptTime.Value,
                "RTN2e - the token has to exist before the transport can carry it");

            var url = mockWs.ConnectionAttempts[0].QueryParams;
            url["accessToken"].Should().Be("callback-provided-token");
            url.Should().NotContainKey("key", "token auth, not basic");

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTN2e/callback-error-prevents-connect-1
        [Fact]
        public async Task RTN2e_CallbackErrorPreventsConnect()
        {
            var connectionAttempted = false;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempted = true;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => throw new Exception("Auth callback failed");
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges).Any(change =>
                    change.Current == ConnectionState.Disconnected
                    || change.Current == ConnectionState.Failed),
                "the connection gave up on the failed auth",
                TimeSpan.FromSeconds(5));

            connectionAttempted.Should().BeFalse(
                "RTN27b - no transport is opened without a token");

            var terminal = UtsClients.Snapshot(stateChanges).First(change =>
                change.Current == ConnectionState.Disconnected
                || change.Current == ConnectionState.Failed);

            terminal.Reason.Should().NotBeNull();
            ((int)terminal.Reason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RTN2e/callback-params-include-clientid-2
        [Fact]
        public async Task RTN2e_CallbackParamsIncludeClientId()
        {
            TokenParams receivedParams = null;

            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "my-client-id";
                options.AuthCallback = tokenParams =>
                {
                    receivedParams = tokenParams;
                    return Task.FromResult<object>(new TokenDetails("token-for-client")
                    {
                        ClientId = "my-client-id",
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            receivedParams.Should().NotBeNull();
            receivedParams.ClientId.Should().Be("my-client-id");
        }

        // UTS: realtime/unit/RTN2e/reuse-valid-token-3
        [Fact]
        public async Task RTN2e_MultipleConnectionsReuseValidToken()
        {
            var callbackCount = 0;

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(new TokenDetails("reusable-token")
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Close() sends a CLOSE and waits for the server's CLOSED, so the test has to answer it
            // or the connection sits in CLOSING until realtimeRequestTimeout expires.
            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());
            await closed;

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            callbackCount.Should().Be(1, "RTN2e - a token that is still valid is reused");
        }

        // UTS: realtime/unit/RSA4c2/callback-error-causes-disconnected-0
        //
        // The same scenario as AuthCallbackErrorsTests.RSA4c2_CallbackErrorConnectingDisconnected,
        // under this file's spec test id.
        [Fact]
        public async Task RSA4c2_CallbackErrorCausesDisconnected()
        {
            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => throw new AblyException(new ErrorInfo(
                    "Auth server unavailable",
                    50000,
                    HttpStatusCode.InternalServerError));
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Disconnected);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
        }

        // UTS: realtime/unit/RSA4c3/callback-error-stays-connected-0
        [Fact]
        public async Task RSA4c3_CallbackErrorStaysConnected()
        {
            var callbackCount = 0;

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    if (callbackCount == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("initial-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                        });
                    }

                    throw new AblyException(new ErrorInfo(
                        "Auth server unavailable",
                        50000,
                        HttpStatusCode.InternalServerError));
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.PollUntil(
                () => callbackCount >= 2,
                "the reauth reached the callback",
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RSA4d/callback-403-causes-failed-0
        [Fact]
        public async Task RSA4d_Callback403CausesFailed()
        {
            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => throw new AblyException(
                    new ErrorInfo("Account disabled", 40300, HttpStatusCode.Forbidden));
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            ((int)failed.Reason.StatusCode.Value).Should().Be(403);
        }

        // UTS: realtime/unit/RSA4d/callback-403-reauth-causes-failed-1
        //
        // DEVIATION, the same finding as AuthCallbackErrorsTests.RSA4d_Callback403ReauthFailed: a
        // 403 from the callback during a server-initiated reauth is swallowed and the connection
        // stays CONNECTED, where RSA4d requires FAILED regardless of the state it happens in. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSA4d_Callback403ReauthCausesFailed()
        {
            var callbackCount = 0;

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    if (callbackCount == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("initial-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                        });
                    }

                    throw new AblyException(
                        new ErrorInfo("Account disabled", 40300, HttpStatusCode.Forbidden));
                };

                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Failed);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
