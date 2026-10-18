// Realtime Authorize Tests (RTC8, RTC8a, RTC8a1, RTC8a2, RTC8a3, RTC8b, RTC8b1, RTC8c)
// Derived from uts/realtime/unit/auth/realtime_authorize.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving) in RTC8c/authorize-closed-initiates-connection-2; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class RealtimeAuthorizeTests : UtsUnitTestBase
    {
        public RealtimeAuthorizeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC8a/authorize-connected-sends-auth-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8a1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8a")]
        public async Task RTC8a_AuthorizeConnectedSendsAuth()
        {
            // Setup
            var authCallbackCount = 0;
            var capturedAuthMessages = new CaptureList<ProtocolMessage>();

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
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Track state changes during reauth
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // When client sends AUTH, record it and respond with new CONNECTED
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    capturedAuthMessages.Add(msg);
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key-2", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });

            var tokenDetails = await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // authCallback was called twice (initial connect + authorize)
            Volatile.Read(ref authCallbackCount).Should().Be(2);

            // An AUTH protocol message was sent
            capturedAuthMessages.Count.Should().Be(1);

            // AUTH message contains the new token
            capturedAuthMessages[0].Auth.Should().NotBeNull();
            capturedAuthMessages[0].Auth.AccessToken.Should().Be("token-2");

            // authorize() resolved with the new token
            tokenDetails.Token.Should().Be("token-2");

            // UPDATE events are emitted but are not state transitions (current == previous == connected)
            var stateTransitions = stateChanges.Snapshot().Where(c => c.Current != c.Previous).ToList();
            stateTransitions.Should().HaveCount(0);
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8a1/successful-reauth-update-event-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8a1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8a1")]
        public async Task RTC8a1_SuccessfulReauthUpdateEvent()
        {
            // Setup
            var authCallbackCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-1", "connection-key-1", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Track events
            var updateEvents = new CaptureList<ConnectionStateChange>();
            var connectedEvents = new CaptureList<ConnectionStateChange>();
            var stateChanges = new CaptureList<ConnectionStateChange>();

            client.Connection.On(ConnectionEvent.Update, change => updateEvents.Add(change));
            client.Connection.On(ConnectionEvent.Connected, change => connectedEvents.Add(change));
            client.Connection.On(change => stateChanges.Add(change));

            // When client sends AUTH, respond with new CONNECTED (updated details)
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-2", "connection-key-2", connectionStateTtl: 180000, maxIdleInterval: 20000));
                }
            });

            await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // UPDATE event was emitted
            updateEvents.Count.Should().Be(1);
            updateEvents[0].Previous.Should().Be(ConnectionState.Connected);
            updateEvents[0].Current.Should().Be(ConnectionState.Connected);

            // No additional CONNECTED state event was emitted
            connectedEvents.Count.Should().Be(0);

            // UPDATE events are emitted but are not state transitions (current == previous == connected)
            var stateTransitions = stateChanges.Snapshot().Where(c => c.Current != c.Previous).ToList();
            stateTransitions.Should().HaveCount(0);

            // Connection details were updated (RTN21)
            // Note: Whether connection.id is updated from the reauth CONNECTED message
            // is implementation-dependent. Some SDKs only set connection.id during initial
            // transport activation. connection.key (via connectionDetails) MUST be updated
            // per RTN21.
            client.Connection.Id.Should().Be("connection-id-2");
            client.Connection.Key.Should().Be("connection-key-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8a1/capability-downgrade-channel-failed-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8a1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8a1")]
        public async Task RTC8a1_CapabilityDowngradeChannelFailed()
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
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach a channel
            var channel = client.Channels.Get("private-channel");

            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach && msg.Channel == "private-channel")
                {
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage("private-channel", new Dictionary<string, JToken> { ["flags"] = 0 }));
                }
            });

            _ = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Track channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // When client sends AUTH, respond with CONNECTED then channel-level ERROR
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    // Reauth succeeds at connection level
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key-2", connectionStateTtl: 120000, maxIdleInterval: 15000));

                    // Then server sends channel-level ERROR (capability downgrade)
                    // Implementation note: The channel-level ERROR must be delivered AFTER the
                    // CONNECTED message has been fully processed. Implementations may need to
                    // defer the ERROR delivery (e.g., via microtask/nextTick) to ensure ordering.
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.ChannelErrorMessage("private-channel", 40160, "Channel denied access based on given capability", 401));
                }
            });

            // Call authorize (to downgrade capabilities)
            await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");
            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            // Channel entered FAILED state
            channel.State.Should().Be(ChannelState.Failed);

            // Channel state change event includes the error reason
            var failedChanges = channelStateChanges.Snapshot().Where(c => c.Current == ChannelState.Failed).ToList();
            failedChanges.Should().HaveCount(1);
            failedChanges[0].Error.Should().NotBeNull();
            failedChanges[0].Error.Code.Should().Be(40160);
            ((int?)failedChanges[0].Error.StatusCode).Should().Be(401);

            // Connection remains CONNECTED (channel-level ERROR doesn't close connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8a2/failed-reauth-connection-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8a2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8a2")]
        public async Task RTC8a2_FailedReauthConnectionFailed()
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
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Track state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // When client sends AUTH, respond with connection-level ERROR (incompatible clientId)
            mockWs.OnClientMessage(msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Auth)
                {
                    mockWs.ActiveConnection.SendToClientAndClose(ProtocolMessages.ErrorMessage(40012, "Incompatible clientId", 400));
                }
            });

            Func<Task> act = () => UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(15), "AuthorizeAsync");
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40012);

            // Assertions
            // Connection transitioned to FAILED
            client.Connection.State.Should().Be(ConnectionState.Failed);

            // Error reason is set on the connection
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40012);

            // State changes include FAILED
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Failed });
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8a3/authorize-completes-after-response-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8a1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8a3")]
        public async Task RTC8a3_AuthorizeCompletesAfterResponse()
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
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start authorize — do NOT await
            // NOTE: the spec's authorize_completed flag is read as the held task's IsCompleted
            var authorizeFuture = client.Auth.AuthorizeAsync();

            // Wait for the client to send the AUTH message (confirms token was obtained
            // and AUTH was sent, but server hasn't responded yet)
            var authMsg = await mockWs.AwaitClientMessage(ProtocolMessages.Auth);

            // authorize() should NOT have completed yet (server hasn't responded)
            authorizeFuture.IsCompleted.Should().BeFalse();

            // Now send the server response
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key-2", connectionStateTtl: 120000, maxIdleInterval: 15000));

            // Now await completion
            var tokenDetails = await UtsWait.WithTimeout(authorizeFuture, TimeSpan.FromSeconds(10), "authorizeFuture");

            // Assertions
            // authorize() completed after server response
            authorizeFuture.IsCompleted.Should().BeTrue();
            tokenDetails.Token.Should().Be("token-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8b/authorize-connecting-halts-attempt-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8b")]
        public async Task RTC8b_AuthorizeConnectingHaltsAttempt()
        {
            // Setup
            var authCallbackCount = 0;
            var capturedWsUrls = new CaptureList<RecordedUrl>();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                capturedWsUrls.Add(conn.Url);

                if (attempt == 1)
                {
                    // First attempt: respond with success but delay CONNECTED
                    // (simulating CONNECTING state)
                    conn.RespondWithSuccess();

                    // Don't send CONNECTED — client stays in CONNECTING
                }
                else
                {
                    // Second attempt (after authorize): complete normally
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // Start connection — will enter CONNECTING
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Call authorize while CONNECTING
            var tokenDetails = await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // authorize() completed successfully
            tokenDetails.Token.Should().Be("token-2");

            // Connection is now CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // authCallback was called twice (initial + authorize)
            Volatile.Read(ref authCallbackCount).Should().Be(2);

            // Two connection attempts were made
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            // Second attempt used the new token
            capturedWsUrls[1].QueryParams["accessToken"].Should().Be("token-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8b1/authorize-connecting-fails-on-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8b1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8b1")]
        public async Task RTC8b1_AuthorizeConnectingFailsOnFailed()
        {
            // Setup
            var authCallbackCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);

                if (attempt == 1)
                {
                    // First attempt: keep in CONNECTING
                    conn.RespondWithSuccess();
                }
                else
                {
                    // Second attempt (after authorize): fail with fatal error
                    conn.RespondWithSuccess();
                    conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40101, "Invalid credentials", 401));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Call authorize while CONNECTING — should fail
            Func<Task> act = () => UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(15), "AuthorizeAsync");
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40101);

            // Assertions
            // Connection is in FAILED state
            client.Connection.State.Should().Be(ConnectionState.Failed);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8c/authorize-disconnected-initiates-connection-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8b1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8c")]
        public async Task RTC8c_AuthorizeDisconnectedInitiatesConnection()
        {
            // Setup
            var authCallbackCount = 0;
            var capturedWsUrls = new CaptureList<RecordedUrl>();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Increment(ref connectionAttemptCount);
                capturedWsUrls.Add(conn.Url);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Client starts in INITIALIZED (autoConnect: false, connect() not called)
            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // Verify client is not connected
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // Track state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Call authorize from non-connected state
            var tokenDetails = await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // authorize() completed successfully
            tokenDetails.Token.Should().Be("token-1");

            // Connection is now CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // State transitions included CONNECTING
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });

            // Connection used the token from authorize
            capturedWsUrls[0].QueryParams["accessToken"].Should().Be("token-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8c/authorize-failed-initiates-connection-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8c")]
        public async Task RTC8c_AuthorizeFailedInitiatesConnection()
        {
            // Setup
            var authCallbackCount = 0;
            var capturedWsUrls = new CaptureList<RecordedUrl>();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                capturedWsUrls.Add(conn.Url);
                conn.RespondWithSuccess();

                if (attempt == 1)
                {
                    // First attempt: fail with fatal error
                    conn.SendToClientAndClose(ProtocolMessages.ErrorMessage(40101, "Invalid credentials", 401));
                }
                else
                {
                    // Second attempt (after authorize): succeed
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // Connect — will fail
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Track state changes from FAILED onwards
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Call authorize from FAILED state
            var tokenDetails = await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // authorize() completed successfully
            tokenDetails.Token.Should().Be("token-2");

            // Connection recovered to CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // State transitions went through CONNECTING
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });

            // Second connection used the new token
            capturedWsUrls[1].QueryParams["accessToken"].Should().Be("token-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC8c/authorize-closed-initiates-connection-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTC8b1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTC8c")]
        public async Task RTC8c_AuthorizeClosedInitiatesConnection()
        {
            // Setup
            var authCallbackCount = 0;
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id-" + attempt, "connection-key-" + attempt, connectionStateTtl: 120000, maxIdleInterval: 15000));
            });

            // Disclosures: the mock answers CLOSE with CLOSED (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    var count = Interlocked.Increment(ref authCallbackCount);
                    return Task.FromResult<object>(new TokenDetails("token-" + count) { Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000) });
                },
                AutoConnect = false,
            });

            // Test Steps
            // Connect, then close
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Call authorize from CLOSED state
            var tokenDetails = await UtsWait.WithTimeout(client.Auth.AuthorizeAsync(), TimeSpan.FromSeconds(10), "AuthorizeAsync");

            // Assertions
            // authorize() completed successfully
            tokenDetails.Token.Should().Be("token-2");

            // Connection is now CONNECTED again
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }
    }
}
