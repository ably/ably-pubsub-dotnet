using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/connection_open_failures_test.md in ably/specification.
    ///
    /// Spec points: RTN14a, RTN14b, RTN14c, RTN14d, RTN14e, RTN14f, RTN14g, RSA4a (RSA4a2)
    ///
    /// <para>
    /// <b>The spec's fake timers split two ways.</b> RTN14c's connect deadline and the DISCONNECTED
    /// and SUSPENDED retry waits are <em>scheduled</em> on timers with no injection point, so they
    /// become shortened client options. RTN14e's connectionStateTtl is <em>measured</em> against
    /// <c>ClientOptions.NowFunc</c>, so a <see cref="TestClock"/> advance makes it elapse instantly.
    /// </para>
    ///
    /// <para>
    /// <b>DISCONNECTED is too brief to sample in several of these scenarios.</b> A refused attempt
    /// and a connect timeout both carry a retryable status code, which earns an immediate reconnect,
    /// so the connection is back in CONNECTING within microseconds — and CONNECTING carries no
    /// <c>ErrorReason</c>, because the SDK reassigns it on every transition. Those tests read the
    /// transition out of <c>UtsClients.RecordConnectionStateChanges</c> instead, which is the shape
    /// <c>mock_websocket.md</c> prescribes. Where the state the spec waits for is terminal or
    /// long-lived, the properties are asserted directly as the spec writes them.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionOpenFailuresTests : UtsTestBase
    {
        /// <summary>
        /// The spec invents its own <c>DEFAULT_CONNECTION_STATE_TTL = 5000</c>. This SDK's is DF1a's
        /// 120s (<c>Defaults.ConnectionStateTtl</c>), and connectionStateTtl is not a client option -
        /// only a CONNECTED message's connectionDetails overrides it, and these connections never get
        /// that far - so the TestClock advance is measured against the real default.
        /// </summary>
        private const int ConnectionStateTtlMs = 120000;

        public ConnectionOpenFailuresTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN14a/invalid-key-failed-0
        [Fact]
        public async Task RTN14a_InvalidKeyFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // The WebSocket connects successfully.
                conn.RespondWithSuccess();

                // But the server immediately sends ERROR for the invalid key and closes the connection.
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40005, "Invalid key", 400));
            });

            // DisconnectedRetryTimeout is pushed out of the way so the connection settles instead of
            // cycling while the assertions run: measured, this SDK re-enters CONNECTING after FAILED
            // of its own accord. See the FAILED re-entry note in Uts/deviations.md.
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = "invalid.key:secret";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            // The spec's first AWAIT_STATE is for CONNECTING. Connect() enters it synchronously and the
            // handler answers inside that call, so waiting for it would assert nothing (skill trap 1);
            // the recorders below carry it instead.
            var states = UtsClients.RecordConnectionStates(client.Connection);
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(changes).Find(c => c.Current == ConnectionState.Failed);
            failed.Should().NotBeNull("the connection must transition to FAILED");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Failed)
                .Should().BeTrue("the connection must pass through CONNECTING on its way to FAILED");

            // Read off the recorded transition, not the live property - see the comment above.
            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(40005);
            ((int)failed.Reason.StatusCode.Value).Should().Be(400);

            // RTN8d, RTN9d. The spec writes `IS null`; this connection never received a CONNECTED, so
            // the fields were never set and read back as empty strings rather than null. Adapted to
            // "no id, no key", which is the observable the spec points are about — this scenario
            // cannot distinguish "cleared on FAILED" from "never assigned", so asserting null here
            // would be asserting an initialisation detail rather than RTN8d.
            client.Connection.Id.Should().BeNullOrEmpty();
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN14b/token-error-with-renewal-0
        [Fact]
        public async Task RTN14b_TokenErrorWithRenewal()
        {
            var tokenRequestCount = 0;
            var connectionAttemptCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (req.Path.Contains("/keys/"))
                    {
                        tokenRequestCount++;
                        var issued = DateTimeOffset.UtcNow;
                        req.RespondWith(200, new
                        {
                            token = "renewed_token_" + tokenRequestCount,
                            keyName = "appId.keyId",
                            issued = issued.ToUnixTimeMilliseconds(),
                            expires = issued.AddHours(1).ToUnixTimeMilliseconds(),
                            capability = "{\"*\":[\"*\"]}",
                        });
                    }
                });

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    // First attempt: token error, then the server closes the connection.
                    conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
                }
                else
                {
                    // Second attempt: success.
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key"));
                }
            });

            var client = RealtimeClient(mockWs, mockHttp);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            client.Connection.State.Should().Be(ConnectionState.Connected);

            // ADAPTED — the spec's count is wrong, not the SDK's behaviour. Recorded under
            // "UTS Spec Errors" in Uts/deviations.md.
            //
            // The spec asserts `token_request_count == 2` with the comment "# Initial + renewal",
            // but its own setup gives the client a plain key and no useTokenAuth. A key-authenticated
            // realtime connection does not exchange the key for a token first: TransportParams.Create
            // puts the key in the connection's query string (Transport/TransportParams.cs:102-105),
            // so there is no initial token request to count and the renewal is the only one. The
            // features spec's RTN14b says nothing about an initial request — the "2" comes solely
            // from that comment.
            //
            // What RTN14b actually requires is asserted, and holds: a token error triggered a
            // renewal, the connection was retried, and it reached CONNECTED.
            tokenRequestCount.Should().Be(
                1,
                "the token error must trigger exactly one renewal; there is no initial token "
                + "request because a key-authenticated realtime connection sends the key itself");

            // ADAPTED, same reason as the count above. The spec's "connection was attempted twice"
            // means "the first attempt failed and a later one succeeded after renewal"; this SDK
            // makes more than two because RTN17j's immediate-retry budget produces a burst against
            // the remaining domain before the renewed attempt lands. Asserting exactly 2 would be
            // asserting the retry budget, not RTN14b.
            connectionAttemptCount.Should().BeGreaterOrEqualTo(
                2,
                "the first attempt must fail and a later one succeed after the token was renewed");
        }

        // UTS: realtime/unit/RTN14b/token-renewal-fails-1
        [Fact]
        public async Task RTN14b_TokenRenewalFails()
        {
            var callCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Always reject with a token error. The spec does not respond_with_success here; the
                // mock attaches the transport listener before the attempt is answered
                // (ConnectionManager.CreateTransport sets Listener then calls Connect), so the frame
                // still reaches the connecting state and the attempt is simply left unanswered.
                conn.SendToClient(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("initial-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                        });
                    }

                    throw new AblyException(
                        new ErrorInfo("Unable to renew token", 40171, HttpStatusCode.Unauthorized));
                };
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // The renewal failure carries a 401, which is not a retryable status code, so there is no
            // instant retry and DISCONNECTED is held for disconnectedRetryTimeout - left at its 15s
            // default here precisely so that the authCallback is not invoked a third time before the
            // assertions run.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(5));

            // DISCONNECTED, not FAILED, per RTN14b.
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            UtsClients.Snapshot(stateChanges)
                .Should().Contain(change => change.Current == ConnectionState.Disconnected);

            // Once for the initial token, once for the renewal.
            callCount.Should().Be(2);

            client.Connection.ErrorReason.Should().NotBeNull();
        }

        // UTS: realtime/unit/RSA4a/token-error-no-renewal-0
        [Fact]
        public async Task RSA4a_TokenErrorNoRenewal()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });

            // A token literal and no way to renew it: no key, no authUrl, no authCallback.
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.Token = "expired_token_string";

                // Pushed out of the way so the connection settles rather than cycling while the
                // assertions run. See the note below on why that is necessary.
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // RSA4a2: no means to renew, so FAILED rather than DISCONNECTED. The SDK does reach it.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            // Asserted on the recorded transition rather than by sampling Connection.ErrorReason
            // afterwards. Measured: the connection reaches FAILED with 40171 and then re-enters
            // CONNECTING of its own accord a bounded number of times before settling, so the live
            // property is a race even though the transition itself is not. The re-entry is noted
            // under "Investigated and not defects" in Uts/deviations.md as a follow-up; it is not
            // what this spec point is about.
            var failed = UtsClients.Snapshot(stateChanges)
                .Find(c => c.Current == ConnectionState.Failed);

            failed.Should().NotBeNull("the connection must transition to FAILED");
            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(
                40171,
                "RSA4a2 - no means provided to renew the auth token");
        }

        // UTS: realtime/unit/RTN14c/connection-timeout-0
        [Fact]
        public async Task RTN14c_ConnectionTimeout()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // The WebSocket connects but the server never sends CONNECTED, simulating an
                // unresponsive server.
                conn.RespondWithSuccess();
            });

            // The spec's realtimeRequestTimeout of 1000 plus ADVANCE_TIME(1100). The connect deadline
            // is scheduled on a CountdownTimer with no seam, so the option is shortened rather than the
            // clock advanced - 200ms is the same test, waited out for real.
            var client = RealtimeClient(mockWs, configure: options =>
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200));

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // The spec's AWAIT_STATE for CONNECTING. Connect() enters it synchronously, so the premise
            // worth waiting on is the attempt itself (skill trap 1).
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 1,
                "the first connection attempt",
                TimeSpan.FromSeconds(5));

            // DISCONNECTED carries a 504 here, which is retryable, so RTN15a/RTN17j reconnect
            // immediately and the state is CONNECTING again before any await returns. Poll the
            // cumulative recorder rather than sampling the live state.
            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges)
                    .Any(change => change.Current == ConnectionState.Disconnected),
                "the connection to enter DISCONNECTED",
                TimeSpan.FromSeconds(5));

            var timedOut = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Disconnected);

            timedOut.Reason.Should().NotBeNull();

            var message = timedOut.Reason.Message ?? string.Empty;
            var indicatesTimeout = message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                                   || timedOut.Reason.Code == 50003
                                   || timedOut.Reason.Code == 80003;

            indicatesTimeout.Should().BeTrue(
                "the DISCONNECTED reason should indicate a timeout but was {0} ({1})",
                message,
                timedOut.Reason.Code);
        }

        // UTS: realtime/unit/RTN14d/retry-recoverable-failure-0
        [Fact]
        public async Task RTN14d_RetryRecoverableFailure()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;

                if (connectionAttemptCount == 1)
                {
                    // First attempt fails with a network error.
                    conn.RespondWithRefused();
                }
                else
                {
                    // Second attempt succeeds.
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key"));
                }
            });

            // The spec's disconnectedRetryTimeout of 1000 plus ADVANCE_TIME(1100), shortened because the
            // wait is scheduled rather than measured. Under RTN15a the SDK does not wait it out at all
            // for the first refused attempt, so this is belt and braces.
            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(200));

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges)
                    .Any(change => change.Current == ConnectionState.Disconnected),
                "the connection to enter DISCONNECTED after the first failure",
                TimeSpan.FromSeconds(2));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionAttemptCount.Should().Be(2);
        }

        // UTS: realtime/unit/RTN14e/disconnected-to-suspended-0
        [Fact]
        public async Task RTN14e_DisconnectedToSuspended()
        {
            var clock = new TestClock();
            var attempts = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts++;

                if (attempts == 2)
                {
                    // The spec's ADVANCE_TIME(DEFAULT_CONNECTION_STATE_TTL + 100), placed where it is
                    // deterministic rather than between two awaits: the first DISCONNECTED retries
                    // instantly, so there is no window in the test body. RTN14e is measured from the
                    // first connection attempt, which was recorded against the un-advanced clock, and
                    // the decision is taken when an attempt fails (SetDisconnectedStateCommand's
                    // ShouldSuspend check) - so this refusal is the one that converts to SUSPENDED.
                    clock.Advance(ConnectionStateTtlMs + 100);
                }

                // All connection attempts fail.
                conn.RespondWithRefused();
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(200));

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(stateChanges)
                    .Any(change => change.Current == ConnectionState.Disconnected),
                "the connection to enter DISCONNECTED",
                TimeSpan.FromSeconds(5));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(5));

            // SUSPENDED is held for suspendedRetryTimeout, left at its 30s default here, so the
            // properties are stable.
            client.Connection.State.Should().Be(ConnectionState.Suspended);
            client.Connection.ErrorReason.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN14f/suspended-retries-indefinitely-0
        [Fact]
        public async Task RTN14f_SuspendedRetriesIndefinitely()
        {
            var clock = new TestClock();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;

                if (connectionAttemptCount == 2)
                {
                    // As in RTN14e: this SDK converts DISCONNECTED to SUSPENDED when an attempt fails
                    // past connectionStateTtl, so the ttl is elapsed on the TestClock before the second
                    // refusal. NOTE: that spends one of the spec's three attempts, so the retry the
                    // spec makes out of SUSPENDED is the third here rather than the second. The
                    // assertion on the recorded states below is what pins that retry down.
                    clock.Advance(ConnectionStateTtlMs + 100);
                }

                if (connectionAttemptCount < 3)
                {
                    // First 2 attempts fail.
                    conn.RespondWithRefused();
                }
                else
                {
                    // Third attempt succeeds.
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key"));
                }
            });

            // The spec's two ADVANCE_TIME(1100) steps over suspendedRetryTimeout: scheduled waits, so
            // both intervals are shortened instead.
            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(200);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(200);
            });

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(5));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Connected);
            connectionAttemptCount.Should().BeGreaterOrEqualTo(3);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Disconnected,
                ConnectionState.Suspended,
                ConnectionState.Connected)
                .Should().BeTrue("the connection must retry out of SUSPENDED rather than stopping there");
        }

        // UTS: realtime/unit/RTN14g/error-empty-channel-failed-0
        [DeviationFact]
        public async Task RTN14g_ErrorEmptyChannelFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                // No channel field: an empty channel attribute means a connection-level error.
                conn.SendToClientAndClose(
                    ProtocolMessages.ErrorMessage(50000, "Internal server error", 500));
            });

            var client = RealtimeClient(mockWs);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // DEVIATION, confirmed against the features spec. RTN14g (features.md:577) is
            // unconditional: an ERROR with an empty channel attribute, for any reason other than
            // RTN14b, transitions the connection to FAILED. The SDK instead conflates "retryable
            // HTTP status" with "recoverable connection failure":
            // HandleConnectingErrorCommand (RealtimeWorkflow.cs:551-569) sends any non-token error
            // whose status is 500-504 to DISCONNECTED and retries. Measured: a 50000/500
            // connection-level ERROR gives Connecting -> Disconnected(50000) -> Connecting ->
            // Disconnected(50000) and never reaches FAILED. ConnectionConnectedState does implement
            // RTN14g for an ERROR arriving once connected, so the gap is specific to CONNECTING.
            // See Uts/deviations.md.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Failed);

            var failed = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            ((int)client.Connection.ErrorReason.StatusCode.Value).Should().Be(500);
            client.Connection.ErrorReason.Message.Should().Be("Internal server error");
        }
    }
}
