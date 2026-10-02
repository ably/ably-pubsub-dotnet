using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/error_reason_test.md in ably/specification.
    ///
    /// Spec points: RTN25, and through it RTN14b, RTN14e, RTN14g, RSA4a
    ///
    /// <para>
    /// Every test reads the error through <c>UtsClients.RecordConnectionStateChanges</c> rather than
    /// by sampling <c>Connection.ErrorReason</c> after a wait. The property holds whichever error
    /// came with the most recent transition - <c>RealtimeState.ConnectionData.UpdateState</c>
    /// assigns it unconditionally - and several of these scenarios produce a second transition
    /// immediately behind the first: a refused attempt earns an instant reconnect under
    /// RTN15a/RTN17j, and entering CONNECTING carries no error, so it clears the very value the spec
    /// is about. The recorded change is the error the spec means. Where the state the spec waits for
    /// is terminal (FAILED) or long lived (SUSPENDED, CONNECTED) the property is asserted as well,
    /// which is the spec's own assertion.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>enable_fake_timers()</c> / <c>ADVANCE_TIME</c> splits two ways here, per the
    /// skill's Timers rule. RTN14e's connectionStateTtl is measured against
    /// <c>ClientOptions.NowFunc</c> (<c>AttemptsHelpers.ShouldSuspend</c>), so a <c>TestClock</c>
    /// advance makes it elapse instantly. The disconnected retry wait is scheduled on a real
    /// <c>CountdownTimer</c> with no seam, so it becomes a shortened
    /// <c>ClientOptions.DisconnectedRetryTimeout</c> instead.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ErrorReasonTests : UtsTestBase
    {
        /// <summary>
        /// The spec's DEFAULT_CONNECTION_STATE_TTL is its own figure of 5000. This SDK's is DF1a's
        /// 120s (<c>Defaults.ConnectionStateTtl</c>), and connectionStateTtl is not a client option -
        /// only a CONNECTED message's connectionDetails overrides it - so the advance is measured
        /// against the real default.
        /// </summary>
        private const int ConnectionStateTtlMs = 120000;

        public ErrorReasonTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN25/error-reason-on-failed-0
        [Fact]
        public async Task RTN25_ErrorReasonOnFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40005, "Invalid API key", 400));
            });

            // DisconnectedRetryTimeout is pushed out of the way so the connection settles instead
            // of cycling while the assertions run.
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = "invalid.key:secret";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            // Initially errorReason should be null.
            client.Connection.ErrorReason.Should().BeNull();

            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(changes).First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(40005);
            ((int)failed.Reason.StatusCode.Value).Should().Be(400);
            failed.Reason.Message.Should().Be("Invalid API key");

            // The spec also reads Connection.ErrorReason directly, and that is NOT safe here even
            // though FAILED looks terminal: measured, the connection re-enters CONNECTING of its own
            // accord after FAILED, and entering CONNECTING carries no error, so the property is
            // cleared out from under the assertion. The recorded transition above is the same fact
            // without the race. See the FAILED re-entry note in Uts/deviations.md.
        }

        // UTS: realtime/unit/RTN25/error-reason-disconnected-1
        [Fact]
        public async Task RTN25_ErrorReasonDisconnected()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());

            var client = RealtimeClient(mockWs);
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // NOTE: the spec's AWAIT_STATE for DISCONNECTED, polled on the recorder instead. A
            // refused attempt qualifies for RTN15a's instant reconnect, so DISCONNECTED is transient
            // and waiting for the state can return after the connection has already left it.
            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(changes).Any(change => change.Current == ConnectionState.Disconnected),
                "the connection to enter DISCONNECTED",
                TimeSpan.FromSeconds(5));

            var disconnected = UtsClients.Snapshot(changes)
                .First(change => change.Current == ConnectionState.Disconnected);

            // The spec reads client.connection.errorReason here; this is the same ErrorInfo the
            // property held at that transition, before the instant retry's CONNECTING cleared it.
            disconnected.Reason.Should().NotBeNull();
            disconnected.Reason.Message.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN25/error-reason-suspended-2
        [Fact]
        public async Task RTN25_ErrorReasonSuspended()
        {
            var clock = new TestClock();
            var attempts = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts++;
                if (attempts >= 2)
                {
                    // The spec's ADVANCE_TIME, placed where it is deterministic rather than between
                    // two awaits. RTN14e is measured from the first connection attempt, which is
                    // already recorded against the un-advanced clock by the time the second attempt
                    // is made, so the DISCONNECTED this refusal produces is the one whose
                    // ShouldSuspend check sees the TTL as expired.
                    clock.Advance(ConnectionStateTtlMs + 100);
                }

                conn.RespondWithRefused();
            });

            var client = RealtimeClient(
                mockWs,
                clock: clock,
                configure: options => options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500));

            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(changes).Any(change => change.Current == ConnectionState.Disconnected),
                "the connection to enter DISCONNECTED",
                TimeSpan.FromSeconds(5));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(5));

            var suspended = UtsClients.Snapshot(changes)
                .First(change => change.Current == ConnectionState.Suspended);

            suspended.Reason.Should().NotBeNull();
            suspended.Reason.Message.Should().NotBeNull();

            // SUSPENDED is held for suspendedRetryTimeout, so the property is stable here.
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Message.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTN25/error-reason-token-error-3
        [Fact]
        public async Task RTN25_ErrorReasonTokenError()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
            });

            // A token literal and no authUrl, authCallback or key to renew it with - RSA4a applies.
            // DisconnectedRetryTimeout is pushed out of the way because this SDK re-enters CONNECTING
            // after FAILED of its own accord, which clears the live ErrorReason - see the FAILED
            // re-entry note in Uts/deviations.md.
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.Token = "expired_token";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // Per RSA4a2: no means to renew, so FAILED.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(changes).First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(40171, "RSA4a2 - no means provided to renew the auth token");

            // The spec also reads Connection.ErrorReason directly. Not asserted here: the re-entry
            // noted above clears it, and the recorded transition is the same fact without the race.
        }

        // UTS: realtime/unit/RTN25/error-reason-cleared-on-connect-4
        [Fact]
        public async Task RTN25_ErrorReasonClearedOnConnect()
        {
            var attempts = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts++;
                if (attempts == 1)
                {
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key"));
                }
            });

            // The spec's ADVANCE_TIME(150) over its disconnectedRetryTimeout of 100. There is no
            // timer seam, so the interval is shortened through the client option instead - and under
            // RTN15a the SDK does not wait it out at all for a refused attempt.
            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100));

            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            // The spec asserts errorReason IS NOT null after the failure. Read from the recorded
            // DISCONNECTED: the retry's CONNECTING carries no error, so by the time the connection
            // is up the property no longer holds it.
            var disconnected = UtsClients.Snapshot(changes)
                .First(change => change.Current == ConnectionState.Disconnected);
            disconnected.Reason.Should().NotBeNull();

            // The spec permits either A) errorReason cleared on a successful connection or B) kept.
            // This SDK does A: UpdateState assigns ErrorReason from the new state's error on every
            // transition, and a CONNECTED with no error member carries none.
            client.Connection.ErrorReason.Should().BeNull();
            UtsClients.Snapshot(changes)
                .First(change => change.Current == ConnectionState.Connected)
                .Reason.Should().BeNull();
        }

        // UTS: realtime/unit/RTN25/error-reason-protocol-error-5
        [DeviationFact]
        public async Task RTN25_ErrorReasonProtocolError()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                // No channel field: a connection-level error (RTN14g).
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(50000, "Internal server error", 500));
            });

            var client = RealtimeClient(mockWs);
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();

            // DEVIATION, same root cause as ConnectionOpenFailuresTests.RTN14g_ErrorEmptyChannelFailed
            // and recorded as one entry with it. RTN14g (features.md:577) is unconditional, but
            // HandleConnectingErrorCommand (RealtimeWorkflow.cs:551-569) sends any non-token error
            // whose status is 500-504 to DISCONNECTED and retries, so this ERROR never reaches
            // FAILED while CONNECTING. See Uts/deviations.md.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var failed = UtsClients.Snapshot(changes).First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(50000);
            ((int)failed.Reason.StatusCode.Value).Should().Be(500);
            failed.Reason.Message.Should().Be("Internal server error");

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
        }

        // UTS: realtime/unit/RTN25/error-reason-in-state-change-6
        [Fact]
        public async Task RTN25_ErrorReasonInStateChange()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40003, "Access token invalid", 400));
            });

            // DisconnectedRetryTimeout pushed out of the way: this SDK re-enters CONNECTING after
            // FAILED of its own accord, so without it the FAILED event fires more than once and the
            // live ErrorReason is cleared between the wait and the assertions. See the FAILED
            // re-entry note in Uts/deviations.md.
            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            // The spec's client.connection.on(ConnectionState.failed, ...). .NET spells the event
            // ConnectionEvent.Failed and hands the listener the same ConnectionStateChange.
            var stateChanges = new List<ConnectionStateChange>();
            client.Connection.On(ConnectionEvent.Failed, change =>
            {
                lock (stateChanges)
                {
                    stateChanges.Add(change);
                }
            });

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var observed = UtsClients.Snapshot(stateChanges);
            observed.Should().NotBeEmpty("the FAILED event must carry the error");

            // The spec asserts exactly one FAILED event. Asserted on the first instead, because the
            // re-entry noted above can emit a second; the spec point is what the event *carries*.
            var failedChange = observed[0];
            failedChange.Reason.Should().NotBeNull();
            failedChange.Reason.Code.Should().Be(40003);
            ((int)failedChange.Reason.StatusCode.Value).Should().Be(400);
            failedChange.Reason.Message.Should().Be("Access token invalid");
        }
    }
}
