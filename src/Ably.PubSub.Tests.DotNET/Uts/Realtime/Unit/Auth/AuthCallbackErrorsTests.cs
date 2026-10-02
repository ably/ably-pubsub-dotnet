using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Auth
{
    /// <summary>
    /// Derived from uts/realtime/unit/auth/auth_callback_errors_test.md in ably/specification.
    ///
    /// Spec points: RSA4c, RSA4c2, RSA4c3, RSA4d, RSA4e, RSA4f
    ///
    /// <para>
    /// What the connection does with an auth failure depends on the state it is in - DISCONNECTED
    /// from CONNECTING, nothing at all from CONNECTED - and on whether the failure carries a 403.
    /// </para>
    ///
    /// <para>
    /// The spec's never-resolving callback is translated as one that waits well past
    /// <c>RealtimeRequestTimeout</c>, which is shortened in that test. <c>AblyAuth</c> bounds the
    /// callback with <c>TimeoutAfter(Options.RealtimeRequestTimeout)</c>, so the timeout is a real
    /// timer and not something a <see cref="TestClock"/> can advance - the skill's Timers rule.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class AuthCallbackErrorsTests : UtsTestBase
    {
        public AuthCallbackErrorsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RSA4c2/callback-error-connecting-disconnected-0
        [Fact]
        public async Task RSA4c2_CallbackErrorConnectingDisconnected()
        {
            var authCallbackCount = 0;

            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    if (authCallbackCount == 1)
                    {
                        throw new AblyException(new ErrorInfo(
                            "Auth server unavailable",
                            50000,
                            HttpStatusCode.InternalServerError));
                    }

                    return Task.FromResult<object>(new TokenDetails("valid-token-" + authCallbackCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };

                // So the connection settles in DISCONNECTED rather than retrying into the
                // assertions with the callback's second, successful answer.
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(
                ConnectionState.Disconnected,
                "RSA4c2 - an auth failure while connecting is retriable, not fatal");

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            ((int)client.Connection.ErrorReason.StatusCode.Value).Should().Be(401);
            client.Connection.ErrorReason.Cause.Should().NotBeNull("RSA4c2 - the cause is set");
            client.Connection.ErrorReason.Cause.Code.Should().Be(50000);

            var disconnects = UtsClients.Snapshot(stateChanges)
                .Where(change => change.Current == ConnectionState.Disconnected)
                .ToList();
            disconnects.Should().NotBeEmpty();
            disconnects[0].Reason.Should().NotBeNull();
            disconnects[0].Reason.Code.Should().Be(80019);
        }

        // UTS: realtime/unit/RSA4c2/callback-timeout-connecting-disconnected-1
        [Fact]
        public async Task RSA4c2_CallbackTimeoutConnectingDisconnected()
        {
            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;

                // The spec's NEVER_RESOLVING_FUTURE. A delay an order of magnitude past the timeout
                // below is the same thing from the SDK's point of view, and it cannot outlive the
                // test.
                options.AuthCallback = async tokenParams =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    return new TokenDetails("never-arrives");
                };

                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500);
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            // Read from the recorder, not from Connection.State. A timed-out auth attempt carries a
            // retryable status, which earns RTN15a/RTN17j's *immediate* reconnect - that path
            // ignores DisconnectedRetryTimeout - so the connection is back in CONNECTING before the
            // assertions can sample it. Same reasoning as the class note on
            // ConnectionOpenFailuresTests.
            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(10));

            var disconnected = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Disconnected);

            disconnected.Reason.Should().NotBeNull();
            disconnected.Reason.Code.Should().Be(80019);
            ((int)disconnected.Reason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RSA4c3/callback-error-connected-stays-0
        [Fact]
        public async Task RSA4c3_CallbackErrorConnectedStays()
        {
            var authCallbackCount = 0;

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    if (authCallbackCount == 1)
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

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // RTN22 - the server asks the client to re-authenticate.
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.PollUntil(
                () => authCallbackCount >= 2,
                "the server-initiated reauth reached the callback",
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RSA4c3 - the existing token is still valid, so the connection is unaffected");

            UtsClients.Snapshot(stateChanges).Should().BeEmpty(
                "RSA4c3 - a failed reauth while connected produces no state change at all");

            client.Connection.ErrorReason.Should().BeNull(
                "there is no state change to carry an error");
        }

        // UTS: realtime/unit/RSA4d/callback-403-connecting-failed-0
        [Fact]
        public async Task RSA4d_Callback403ConnectingFailed()
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

            connectionAttempted.Should().BeFalse(
                "RSA4d - auth fails before any transport is opened");

            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(80019);
            ((int)failed.Reason.StatusCode.Value).Should().Be(403);
            failed.Reason.Cause.Should().NotBeNull("RSA4d - the cause is the original 403");
        }

        // UTS: realtime/unit/RSA4d/callback-403-reauth-failed-1
        //
        // DEVIATION. RSA4d is not conditional on connection state: an authCallback that results in
        // an ErrorInfo with statusCode 403 must transition the connection to FAILED. It is the one
        // case that overrides RSA4c3's "a failed reauth while connected changes nothing" - which the
        // passing RSA4c3 test above confirms the SDK gets right for a non-403 error. This SDK makes
        // no distinction: measured, a 403 from the callback during an RTN22 reauth is swallowed like
        // any other and the connection stays CONNECTED. See Uts/deviations.md.
        [DeviationFact]
        public async Task RSA4d_Callback403ReauthFailed()
        {
            var authCallbackCount = 0;

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    if (authCallbackCount == 1)
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

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // RTN22 - the server asks the client to re-authenticate, and this time auth is forbidden.
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AuthMessage());

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            ((int)failed.Reason.StatusCode.Value).Should().Be(403);
        }

        // UTS: realtime/unit/RSA4f/callback-invalid-type-format-0
        //
        // DEVIATION, and narrow: the code is right and the status is not. RSA4c2 requires an
        // ErrorInfo with "code 80019, statusCode 401"; the connection does reach DISCONNECTED with
        // 80019, but the unsupported-callback-type branch raises it with
        // HttpStatusCode.BadRequest (AblyAuth.cs:343-347), so the status is 400. Recorded with the
        // RSA4e entry in Uts/deviations.md, which is the same family of wrong auth-error metadata.
        [DeviationFact]
        public async Task RSA4f_CallbackInvalidTypeFormat()
        {
            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;

                // Neither a string, a TokenRequest nor a TokenDetails.
                options.AuthCallback = tokenParams => Task.FromResult<object>(12345);
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
            ((int)client.Connection.ErrorReason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RSA4f/callback-oversized-token-format-1
        [Fact]
        public async Task RSA4f_CallbackOversizedTokenFormat()
        {
            var oversizedToken = new string('x', 131073);

            var client = RealtimeClient(ConnectingMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(oversizedToken);
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
            ((int)client.Connection.ErrorReason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RSA4e/rest-callback-error-40170-0
        //
        // The one REST test in this file: RSA4e is the REST-side counterpart of RSA4c.
        //
        // DEVIATION. RSA4e requires a REST request whose authCallback fails to "result in an error
        // with code 40170, statusCode 401". This SDK reports 80019/401 instead - the code RSA4c1
        // specifies for the *realtime* case - because one shared wrapper in
        // AblyAuth.RequestTokenAsync serves both and always raises
        // ErrorCodes.ClientAuthProviderRequestFailed. Measured: code 80019, status Unauthorized,
        // cause code 0, message "Error calling AuthCallback, token request failed.". The SDK knows
        // about 40170 - ErrorCodes.ClientCallbackError - and uses it for the inner null/timeout
        // case, so what is missing is the REST/realtime split at the outer wrapper. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSA4e_RestCallbackError40170()
        {
            var client = RestClient(
                new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req => req.RespondWith(200, new { channelId = "test-channel" })),
                configure: options =>
                {
                    options.Key = null;
                    options.AuthCallback = tokenParams =>
                        throw new Exception("Network failure connecting to auth server");
                });

            Func<Task> act = () => client.Channels.Get("test-channel").StatusAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40170);
            ((int)error.StatusCode.Value).Should().Be(401);
            error.Message.Should().NotBeNullOrEmpty();
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
