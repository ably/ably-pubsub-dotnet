using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/connection_failures_test.md in ably/specification.
    ///
    /// Spec points: RTN14h, RTN15, RTN15a, RTN15b, RTN15c4, RTN15c5, RTN15c6, RTN15c7, RTN15e,
    /// RTN15h1, RTN15h2, RTN15h3, RTN15j
    ///
    /// <para>
    /// Failures that arrive while the connection is already CONNECTED, as distinct from the
    /// open-time failures in <see cref="ConnectionOpenFailuresTests"/>.
    /// </para>
    ///
    /// <para>
    /// Token renewal is driven through <c>authCallback</c> rather than a key plus mock HTTP. The
    /// spec itself prefers this - it offers the callback as "a more portable alternative" that "is
    /// clearer about the number of token requests" - and it makes the renewal count an assertion on
    /// a local variable instead of on URL matching.
    /// </para>
    ///
    /// <para>
    /// The spec's intermediate <c>AWAIT_STATE ... connecting</c> steps are not waited on where the
    /// SDK passes through CONNECTING faster than a listener can be registered after the fact; the
    /// recorded state sequence carries them instead. RTN15h2 is the case the spec warns about
    /// itself: RTN15h2i puts a transient DISCONNECTED between CONNECTED and CONNECTING, so a naive
    /// wait for DISCONNECTED matches the wrong transition. Those tests count transitions in the
    /// recording rather than sampling <c>Connection.State</c>.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionFailuresTests : UtsTestBase
    {
        public ConnectionFailuresTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN15h1/token-error-no-renew-0
        //
        // Two translation notes, both measured.
        //
        // The spec asserts errorReason.code == 40142 - the code the server sent. RSA4a2
        // (features.md:190) is explicit that this is not it: with no means to renew, "the client
        // library should indicate an error with error code 40171 ... and ... transition the
        // connection to the FAILED state". RTN15h1 itself only requires that errorReason "will be
        // set". This SDK raises 40171, which is the compliant code, so the assertion follows RSA4a2
        // and the spec test's 40142 is recorded as a spec error in Uts/deviations.md.
        //
        // The FAILED transition is read out of the recording rather than from Connection.State,
        // because this SDK re-enters CONNECTING after FAILED of its own accord and
        // RealtimeState.ConnectionData.UpdateState reassigns ErrorReason on every transition - so by
        // the time a sample can be taken, FAILED and its reason are both gone. Measured:
        // Connected -> Disconnected(80003) -> Failed(40171) -> Connecting -> Connected. Same
        // technique and same underlying defect as ConnectionOpenFailuresTests' FAILED re-entry note.
        [Fact]
        public async Task RTN15h1_TokenErrorNoRenew()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            // A bare token and nothing to renew it with.
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.Token = "some_token_string";
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.DisconnectedMessage(40142, "Token expired", 401));

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(2));

            var failed = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull("RTN15h1 - the errorReason is set");
            failed.Reason.Code.Should().Be(40171, "RSA4a2 - no means to renew");
            ((int)failed.Reason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RTN15h2/token-error-renew-success-0
        [Fact]
        public async Task RTN15h2_TokenErrorRenewSuccess()
        {
            var tokenRequestCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(connectionAttemptCount == 1
                    ? ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0)
                    : ProtocolMessages.ConnectedMessage("connection-1", "key-1-renewed", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenRequestCount = tokenRequestCount + 1;
                    return Task.FromResult<object>(new TokenDetails("renewed_token_" + tokenRequestCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var firstConnectionId = client.Connection.Id;
            var firstConnectionKey = client.Connection.Key;

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.DisconnectedMessage(40142, "Token expired", 401));

            await reconnected;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            tokenRequestCount.Should().Be(2, "the initial token plus one renewal");
            client.Connection.Id.Should().Be(firstConnectionId, "the same id means the resume took");
            client.Connection.Key.Should().NotBe(firstConnectionKey);
            client.Connection.Key.Should().Be("key-1-renewed");
        }

        // UTS: realtime/unit/RTN15h2/token-error-renew-fails-1
        [Fact]
        public async Task RTN15h2_TokenErrorRenewFails()
        {
            var callCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callCount = callCount + 1;
                    if (callCount == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("valid-token-1")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                        });
                    }

                    throw new AblyException(new ErrorInfo("Token renewal failed", 40171, System.Net.HttpStatusCode.Unauthorized));
                };

                // Long enough that the connection sits in the post-renewal DISCONNECTED while the
                // assertions run rather than cycling out of it.
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // RTN15h2i's transient DISCONNECTED comes first, so the wait is for the *second* one -
            // the one that follows the failed renewal. The spec flags this trap itself.
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.DisconnectedMessage(40142, "Token expired", 401));

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(changes)
                    .Count(change => change.Current == ConnectionState.Disconnected) >= 2,
                "two DISCONNECTED transitions: RTN15h2i's transient one and the renewal failure",
                TimeSpan.FromSeconds(5));

            // Asserted from the recording rather than from Connection.State: the connection does
            // not sit in DISCONNECTED - measured, it is back in CONNECTING within milliseconds, and
            // not because of DisconnectedRetryTimeout, which is pushed to ten minutes above.
            var disconnects = UtsClients.Snapshot(changes)
                .Where(change => change.Current == ConnectionState.Disconnected)
                .ToList();

            disconnects.Should().HaveCountGreaterOrEqualTo(2);
            disconnects.Last().Reason.Should().NotBeNull(
                "RTN15h2 - a failed renewal transitions to DISCONNECTED with the errorReason set");
            callCount.Should().BeGreaterThan(1, "renewal was attempted and failed");
        }

        // UTS: realtime/unit/RTN15h3/non-token-error-resume-0
        [Fact]
        public async Task RTN15h3_NonTokenErrorResume()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.DisconnectedMessage(80003, "Connection disconnected", 500));

            await reconnected;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be(originalConnectionId);
            connectionAttemptCount.Should().Be(2);

            mockWs.ConnectionAttempts[1].QueryParams["resume"].Should().Be("key-1");
        }

        // UTS: realtime/unit/RTN15j/error-empty-channel-failed-0
        [Fact]
        public async Task RTN15j_ErrorEmptyChannelFailed()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // No channel on the ERROR makes it connection-level.
            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.ErrorMessage(50000, "Internal error", 500));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Read from the recording, not from Connection.State: the FAILED re-entry defect means
            // the connection leaves FAILED on its own. Measured:
            // Connected -> Disconnected(80003) -> Failed(50000) -> Connecting -> Connected.
            var failed = UtsClients.Snapshot(stateChanges)
                .First(change => change.Current == ConnectionState.Failed);

            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(50000);
        }

        // UTS: realtime/unit/RTN15a/unexpected-transport-disconnect-0
        [Fact]
        public async Task RTN15a_UnexpectedTransportDisconnect()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;
            var states = UtsClients.RecordConnectionStates(client.Connection);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            // No protocol message - the transport simply goes away.
            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be(originalConnectionId);
            connectionAttemptCount.Should().Be(2);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue("the spec waits for DISCONNECTED, then CONNECTING, then CONNECTED");
        }

        // UTS: realtime/unit/RTN15b/successful-resume-0
        // UTS: realtime/unit/RTN15e/connection-key-updated-0
        //
        // RTN15e has its own spec test id but no separate scenario - its section points at this one
        // for the setup and asserts the updated key, which is asserted here.
        [Fact]
        public async Task RTN15b_SuccessfulResume()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(connectionAttemptCount == 1
                    ? ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0)
                    : ProtocolMessages.ConnectedMessage("connection-1", "key-1-updated", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.Id.Should().Be("connection-1");

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            client.Connection.Id.Should().Be("connection-1", "RTN15c6 - the resume succeeded");
            client.Connection.Key.Should().Be("key-1-updated", "RTN15e");
            mockWs.ConnectionAttempts[1].QueryParams["resume"].Should().Be("key-1", "RTN15b1");
            connectionAttemptCount.Should().Be(2);
        }

        // UTS: realtime/unit/RTN15c7/failed-resume-new-id-0
        [Fact]
        public async Task RTN15c7_FailedResumeNewId()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
                }
                else
                {
                    // A different connectionId plus an error means the resume was refused.
                    var connected = ProtocolMessages.ConnectedMessage(
                        "connection-2",
                        "key-2",
                        maxIdleInterval: 0);
                    connected["error"] = ProtocolMessages.ErrorObject(
                        80008,
                        "Unable to recover connection",
                        400);
                    conn.SendToClient(connected);
                }
            });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var originalConnectionId = client.Connection.Id;

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            client.Connection.Id.Should().Be("connection-2");
            client.Connection.Id.Should().NotBe(originalConnectionId);
            client.Connection.Key.Should().Be("key-2");
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);
            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "a failed resume is still a connection");
        }

        // UTS: realtime/unit/RTN14h/resume-after-ttl-0
        [Fact]
        public async Task RTN14h_ResumeAfterTtl()
        {
            var clock = new TestClock();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (connectionAttemptCount == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        "connection-1",
                        "key-1",
                        connectionStateTtl: 5000,
                        maxIdleInterval: 0));
                }
                else
                {
                    // Every reconnection attempt fails, so the connection runs past its TTL and
                    // becomes SUSPENDED while still retrying.
                    conn.RespondWithRefused();
                }
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.ActiveConnection.SimulateDisconnect();

            // Ordered deliberately: the advance has to come *after* the SDK has recorded when the
            // connection first went down, because connectionStateTtl is measured from that instant
            // against NowFunc. Advancing first looked fine in isolation and failed consistently
            // under load, where the workflow thread had not yet processed the disconnect and so
            // stamped it with the already-advanced clock - leaving zero elapsed time and no
            // SUSPENDED. A second connection attempt proves the disconnect has been processed.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt, which means the disconnect has been recorded",
                TimeSpan.FromSeconds(10));

            clock.Advance(6000);

            // Generous, because getting to SUSPENDED rides several real DisconnectedRetryTimeout
            // cycles and this file runs alongside the rest of the suite. Only a hang spends it.
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(30));

            UtsClients.Snapshot(states).Should().Contain(ConnectionState.Suspended);

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 3,
                "at least one reconnection attempt after the connection became suspended",
                TimeSpan.FromSeconds(30));

            // RTN14h: every attempt after the first still carries the original connectionKey, even
            // the ones made after the TTL expired.
            var reconnectAttempts = mockWs.ConnectionAttempts.Skip(1).ToList();
            reconnectAttempts.Should().NotBeEmpty();
            reconnectAttempts.Should().OnlyContain(
                attempt => attempt.QueryParams.ContainsKey("resume"),
                "RTN14h - reconnection attempts in SUSPENDED still attempt to resume");
        }

        // UTS: realtime/unit/RTN15c5/token-error-during-resume-0
        [Fact]
        public async Task RTN15c5_TokenErrorDuringResume()
        {
            var tokenRequestCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
                }
                else if (connectionAttemptCount == 2)
                {
                    // The resume attempt is rejected with a token error.
                    conn.SendToClientAndClose(
                        ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
                }
                else
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-2", maxIdleInterval: 0));
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenRequestCount = tokenRequestCount + 1;
                    return Task.FromResult<object>(new TokenDetails("token-" + tokenRequestCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            tokenRequestCount.Should().Be(2, "the initial token plus one renewal");
        }

        // UTS: realtime/unit/RTN15c5/token-error-during-resume-0 (the attempt-count assertion)
        //
        // DEVIATION, and only on the count. The substance of RTN15c5 holds and is asserted by the
        // test above: the token error on the resume attempt is routed to renewal
        // (RealtimeWorkflow.cs:554 sends any token error during CONNECTING to
        // HandleConnectingTokenErrorCommand), exactly one new token is obtained, and the connection
        // comes back. But the SDK opens a fourth transport where the spec expects three. Measured
        // sequence: Connected -> Disconnected(80003) -> Connecting -> Disconnected(50000) ->
        // Connecting -> Connected -> Connected, with four connection attempts and two token
        // requests - the trailing Connected->Connected being a second CONNECTED on a second
        // transport.
        //
        // Recorded as its own entry rather than folded into the RTN14g family: the renewal path
        // here is the correct one, so this is a redundant attempt rather than the same
        // mis-routing, and I have not isolated which of the two paths opens the extra transport.
        // See Uts/deviations.md.
        [DeviationFact]
        public async Task RTN15c5_TokenErrorDuringResumeAttemptCount()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
                }
                else if (connectionAttemptCount == 2)
                {
                    conn.SendToClientAndClose(
                        ProtocolMessages.ErrorMessage(40142, "Token expired", 401));
                }
                else
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-2", maxIdleInterval: 0));
                }
            });

            var tokenCount = 0;
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(new TokenDetails("token-" + tokenCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                    });
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            connectionAttemptCount.Should().Be(
                3,
                "RTN15c5 - the initial connection, the refused resume, and one attempt after renewal");
        }

        // UTS: realtime/unit/RTN15c4/fatal-error-during-resume-0
        //
        // DEVIATION, the same root cause as the gated RTN14g test in
        // ConnectionOpenFailuresTests. A resume attempt runs in the CONNECTING state, and
        // HandleConnectingErrorCommand (RealtimeWorkflow.cs:551-569) sends any non-token error with
        // a 500-504 status to DISCONNECTED and retries instead of to FAILED. Measured:
        // Connected -> Disconnected(80003) -> Connecting -> Disconnected(50000) -> Connecting, with
        // no FAILED at all. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTN15c4_FatalErrorDuringResume()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    conn.SendToClient(
                        ProtocolMessages.ConnectedMessage("connection-1", "key-1", maxIdleInterval: 0));
                }
                else
                {
                    conn.SendToClientAndClose(
                        ProtocolMessages.ErrorMessage(50000, "Internal error", 500));
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            connectionAttemptCount.Should().Be(2);
        }
    }
}
