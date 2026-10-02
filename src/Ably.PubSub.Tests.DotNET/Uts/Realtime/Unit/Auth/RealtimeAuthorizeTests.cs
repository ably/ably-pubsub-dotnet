using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Auth
{
    /// <summary>
    /// Derived from uts/realtime/unit/auth/realtime_authorize.md in ably/specification.
    ///
    /// Spec points: RTC8, RTC8a, RTC8a1, RTC8a2, RTC8a3, RTC8b, RTC8b1, RTC8c, RTN21
    ///
    /// <para>
    /// <c>authorize()</c> on a realtime client does more than mint a token: when CONNECTED it sends
    /// an AUTH protocol message and waits for the server's answer (<c>AblyAuth.cs:577</c> ->
    /// <c>ConnectionManager.OnAuthUpdated</c>), and when not connected it starts a connection
    /// instead.
    /// </para>
    ///
    /// <para>
    /// The spec's RTC8c "from DISCONNECTED" case actually starts the client in INITIALIZED - its own
    /// setup comments say so - and that is what the derived test does.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimeAuthorizeTests : UtsTestBase
    {
        public RealtimeAuthorizeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC8a/authorize-connected-sends-auth-0
        [Fact]
        public async Task RTC8a_AuthorizeConnectedSendsAuth()
        {
            var authCallbackCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-1", "connection-key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // The server answers an AUTH with a fresh CONNECTED, which is what releases authorize().
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                        "connection-id-2",
                        "connection-key-2",
                        maxIdleInterval: 0));
                }
            };

            var tokenDetails = await client.Auth.AuthorizeAsync();

            authCallbackCount.Should().Be(2, "the initial connect plus the authorize");

            var authMessages = mockWs.MessagesFromClient
                .Where(msg => msg.Action == ProtocolMessage.MessageAction.Auth)
                .ToList();
            authMessages.Should().HaveCount(1);
            authMessages[0].Auth.Should().NotBeNull();
            authMessages[0].Auth.AccessToken.Should().Be("token-2");

            tokenDetails.Token.Should().Be("token-2");

            UtsClients.Snapshot(stateChanges)
                .Where(change => change.Current != change.Previous)
                .Should().BeEmpty("a reauth while connected is an UPDATE, not a transition");

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTC8a1/successful-reauth-update-event-0
        [Fact]
        public async Task RTC8a1_SuccessfulReauthEmitsUpdate()
        {
            var authCallbackCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-1", "connection-key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var updateEvents = new List<ConnectionStateChange>();
            var connectedEvents = new List<ConnectionStateChange>();
            client.Connection.On(ConnectionEvent.Update, change => updateEvents.Add(change));
            client.Connection.On(ConnectionEvent.Connected, change => connectedEvents.Add(change));

            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                        "connection-id-2",
                        "connection-key-2",
                        maxIdleInterval: 0));
                }
            };

            await client.Auth.AuthorizeAsync();

            await UtsClients.PollUntil(
                () => updateEvents.Count >= 1,
                "the UPDATE event for the reauth",
                TimeSpan.FromSeconds(5));

            updateEvents.Should().HaveCount(1);
            updateEvents[0].Previous.Should().Be(ConnectionState.Connected);
            updateEvents[0].Current.Should().Be(ConnectionState.Connected);

            connectedEvents.Should().BeEmpty("RTC8a1 - no second CONNECTED event");

            UtsClients.Snapshot(stateChanges)
                .Where(change => change.Current != change.Previous)
                .Should().BeEmpty();

            client.Connection.Id.Should().Be("connection-id-2");
            client.Connection.Key.Should().Be("connection-key-2", "RTN21");
        }

        // UTS: realtime/unit/RTC8a1/capability-downgrade-channel-failed-1
        [Fact]
        public async Task RTC8a1_CapabilityDowngradeFailsChannel()
        {
            const string ChannelName = "private-channel";

            var authCallbackCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-1", "connection-key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };

                // This test drives a reauth plus a channel transition and runs alongside the rest of
                // the suite. OnAuthUpdated bounds its wait for a state change by
                // RealtimeRequestTimeout, whose 10s default is close enough to the work here to be
                // reached on a loaded machine - measured as an intermittent 40140 "Connection state
                // didn't change after Auth updated". Nothing in the test is measuring that deadline,
                // so it is moved out of the way.
                options.RealtimeRequestTimeout = TimeSpan.FromSeconds(60);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            var channelStateChanges = UtsClients.RecordChannelStates(channel);
            var channelChanges = new List<ChannelStateChange>();
            channel.On(change => channelChanges.Add(change));

            // The reauth succeeds at the connection level, then the server rejects the channel for
            // the narrowed capability.
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    // Same connectionId, new key: a reauth does not create a new connection, and
                    // RTN21 scopes the override to the attributes within connectionDetails. Keeping
                    // the id also keeps this on the RTN24 update path rather than whatever a changed
                    // id implies, which is not what this test is about.
                    mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                        "connection-id-1",
                        "connection-key-2",
                        maxIdleInterval: 0));
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        40160,
                        "Channel denied access",
                        401));
                }
            };

            // Started, not awaited. The subject of this test is what happens to the *channel*, and
            // making it wait on authorize() first makes it depend on ConnectionManager
            // .OnAuthUpdated's state-change wait - which is exactly the machinery D24 shows to be
            // unreliable, and which was measured timing out here at a full minute when this file
            // runs alongside the rest of the tier while passing on its own. The channel transition
            // below is driven by the server's ERROR and needs nothing from authorize()'s result.
            var authorizing = client.Auth.AuthorizeAsync();

            await UtsClients.AwaitChannelState(channel, ChannelState.Failed, TimeSpan.FromSeconds(10));

            channel.State.Should().Be(ChannelState.Failed);

            var failedChanges = channelChanges
                .Where(change => change.Current == ChannelState.Failed)
                .ToList();
            failedChanges.Should().HaveCount(1);
            failedChanges[0].Error.Should().NotBeNull();
            failedChanges[0].Error.Code.Should().Be(40160);
            ((int)failedChanges[0].Error.StatusCode.Value).Should().Be(401);

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "a channel-level ERROR does not close the connection");

            UtsClients.Snapshot(channelStateChanges).Should().Contain(ChannelState.Failed);

            // Observed so an eventual fault on it is not an unobserved task exception. Whether it
            // completes or faults is D24's business, not this test's.
            _ = authorizing.ContinueWith(
                task => task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);
        }

        // UTS: realtime/unit/RTC8a2/failed-reauth-connection-failed-0
        //
        // DEVIATION, and the most consequential one in this file - see D24 in Uts/deviations.md.
        // RTC8a2 requires a rejected reauth to fail the authorize() call and to transition the
        // connection to FAILED with the server's error. The SDK does change state to FAILED - its
        // own debug log records "Changing state from Disconnected => Failed" and "Updating state to
        // `Failed`" - but **no application listener is ever told**, and authorize() returns without
        // an error.
        //
        // The cause is a synchronous continuation running inside the event emit.
        // ConnectionChangeAwaiter.Wait creates its TaskCompletionSource without
        // RunContinuationsAsynchronously (ConnectionChangeAwaiter.cs:28) and completes it from an
        // InternalStateChanged handler, so OnAuthUpdated's loop resumes *inside*
        // Connection.NotifyUpdate's unguarded `internalHandlers(this, stateChange)` call
        // (Connection.cs:306) and throws AblyException(Connection.ErrorReason) for the failed state
        // (ConnectionManager.cs:213). That unwinds NotifyUpdate before it reaches
        // NotifyExternalClients on the next line, so the FAILED state change is dropped.
        //
        // Measured: a waiter armed before the reauth and given five seconds never fires, and a
        // recorder attached beforehand shows `Connected -> Disconnected(80003)` then
        // `Failed -> Connecting` with no `-> Failed` in between - the state moved without an event.
        [DeviationFact]
        public async Task RTC8a2_FailedReauthFailsConnection()
        {
            var authCallbackCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    "connection-id-1",
                    "connection-key-1",
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };

                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Armed before the reauth is triggered, so the assertion does not depend on catching a
            // short-lived state after the fact.
            var failedChange = new TaskCompletionSource<ConnectionStateChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.Connection.On(change =>
            {
                if (change.Current == ConnectionState.Failed)
                {
                    failedChange.TrySetResult(change);
                }
            });

            // A connection-level ERROR in answer to the AUTH.
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.SendToClientAndClose(
                        ProtocolMessages.ErrorMessage(40012, "Incompatible clientId", 400));
                }
            };

            Func<Task> act = () => client.Auth.AuthorizeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40012);

            var completed = await Task.WhenAny(
                failedChange.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            completed.Should().BeSameAs(
                failedChange.Task,
                "RTC8a2 - the FAILED transition is emitted to listeners");

            var failed = await failedChange.Task;
            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(40012);
        }

        // UTS: realtime/unit/RTC8a3/authorize-completes-after-response-0
        [Fact]
        public async Task RTC8a3_AuthorizeCompletesAfterResponse()
        {
            var authCallbackCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-1", "connection-key-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // No handler for AUTH: the server deliberately does not answer yet.
            var authorizeTask = client.Auth.AuthorizeAsync();

            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Auth);

            authorizeTask.IsCompleted.Should().BeFalse(
                "RTC8a3 - authorize() waits for the server's answer to the AUTH");

            mockWs.SendToClient(ProtocolMessages.ConnectedMessage(
                "connection-id-2",
                "connection-key-2",
                maxIdleInterval: 0));

            var tokenDetails = await authorizeTask;

            tokenDetails.Token.Should().Be("token-2");
        }

        // UTS: realtime/unit/RTC8b/authorize-connecting-halts-attempt-0
        //
        // DEVIATION. RTC8b requires authorize() on a CONNECTING connection to halt the attempt in
        // flight and immediately start a new one with the fresh token. This SDK obtains the token
        // and then does nothing with it: ConnectionManager.OnAuthUpdated takes the
        // "Connection.State != Connected" branch and issues Connect(), and
        // ConnectionConnectingState.Connect() returns EmptyCommand.Instance
        // (ConnectionConnectingState.cs:29-32) - so the in-flight attempt is neither halted nor
        // replaced. Measured: the connection stays CONNECTING and no second transport is opened.
        // See Uts/deviations.md.
        [DeviationFact]
        public async Task RTC8b_AuthorizeWhileConnectingHaltsAttempt()
        {
            var authCallbackCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;

                // The first attempt is left hanging so authorize() arrives while CONNECTING.
                if (connectionAttemptCount > 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connect();
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 1,
                "the first connection attempt, which is left unanswered");

            var tokenDetails = await client.Auth.AuthorizeAsync();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            tokenDetails.Token.Should().Be("token-2");
            client.Connection.State.Should().Be(ConnectionState.Connected);
            authCallbackCount.Should().Be(2);
            connectionAttemptCount.Should().Be(2);
            mockWs.ConnectionAttempts[1].QueryParams["accessToken"].Should().Be("token-2");
        }

        // UTS: realtime/unit/RTC8b1/authorize-connecting-fails-on-failed-0
        //
        // DEVIATION, and a direct consequence of the RTC8b gap above: because authorize() never
        // restarts the connection attempt, the attempt never fails, so there is nothing for
        // authorize() to report and it returns normally. Recorded with RTC8b as one entry in
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RTC8b1_AuthorizeWhileConnectingFailsOnFailed()
        {
            var authCallbackCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;

                if (connectionAttemptCount == 1)
                {
                    // Left hanging, so authorize() lands while CONNECTING.
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClientAndClose(
                    ProtocolMessages.ErrorMessage(40101, "Invalid credentials", 401));
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };

                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 1,
                "the first connection attempt, which is left unanswered");

            Func<Task> act = () => client.Auth.AuthorizeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40101);

            client.Connection.State.Should().Be(ConnectionState.Failed);
        }

        // UTS: realtime/unit/RTC8c/authorize-disconnected-initiates-connection-0
        [Fact]
        public async Task RTC8c_AuthorizeFromInitializedInitiatesConnection()
        {
            var authCallbackCount = 0;
            var mockWs = ConnectingMock();

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            var states = UtsClients.RecordConnectionStates(client.Connection);

            var tokenDetails = await client.Auth.AuthorizeAsync();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            tokenDetails.Token.Should().Be("token-1");
            client.Connection.State.Should().Be(ConnectionState.Connected);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue();

            mockWs.ConnectionAttempts[0].QueryParams["accessToken"].Should().Be("token-1");
        }

        // UTS: realtime/unit/RTC8c/authorize-failed-initiates-connection-1
        //
        // DEVIATION, downstream of D21 - this test's premise cannot be reached. The connection does
        // go to FAILED on the first attempt's 40005, but then re-enters CONNECTING unprompted and
        // reconnects, so by the time authorize() is called the state is CONNECTED and the test is no
        // longer exercising RTC8c at all. Measured: "state when authorize is called = Connected,
        // attempts=2". It should pass as written once D21 is fixed, which is why the spec's version
        // is kept rather than adapted. The sibling RTC8c cases from INITIALIZED and CLOSED pass.
        // See Uts/deviations.md.
        [DeviationFact]
        public async Task RTC8c_AuthorizeFromFailedInitiatesConnection()
        {
            var authCallbackCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                conn.RespondWithSuccess();

                if (connectionAttemptCount == 1)
                {
                    conn.SendToClientAndClose(
                        ProtocolMessages.ErrorMessage(40005, "Invalid request", 400));
                }
                else
                {
                    conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };

                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var states = UtsClients.RecordConnectionStates(client.Connection);

            var tokenDetails = await client.Auth.AuthorizeAsync();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            tokenDetails.Token.Should().Be("token-2");
            client.Connection.State.Should().Be(ConnectionState.Connected);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue();

            mockWs.ConnectionAttempts[1].QueryParams["accessToken"].Should().Be("token-2");
        }

        // UTS: realtime/unit/RTC8c/authorize-closed-initiates-connection-2
        [Fact]
        public async Task RTC8c_AuthorizeFromClosedInitiatesConnection()
        {
            var authCallbackCount = 0;
            var mockWs = ConnectingMock();

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    authCallbackCount = authCallbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + authCallbackCount));
                };
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());
            await closed;

            var tokenDetails = await client.Auth.AuthorizeAsync();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            tokenDetails.Token.Should().Be("token-2");
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

        private static TokenDetails FreshToken(string token)
            => new TokenDetails(token) { Expires = DateTimeOffset.UtcNow.AddHours(1) };
    }
}
