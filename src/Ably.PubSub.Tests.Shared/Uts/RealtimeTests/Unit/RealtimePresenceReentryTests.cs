// RealtimePresence Automatic Re-entry Tests (RTP17a, RTP17e, RTP17g, RTP17g1, RTP17i)
// Derived from uts/realtime/unit/presence/realtime_presence_reentry.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key stand-in "fake.key:secret" as the spec sets it; fake timers are enabled where a reconnect is awaited (infra-driving: the retry after the disconnect fires when the fake clock is advanced past the disconnected retry timeout); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class RealtimePresenceReentryTests : UtsUnitTestBase
    {
        private int _connectionCount;

        public RealtimePresenceReentryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's server echo: every presence entry the client sent comes back as a PRESENCE event for the given connection.
        private void EchoPresence(MockWebSocket mockWs, string channelName, ProtocolMessage msg, string connectionId, string defaultClientId)
        {
            for (var idx = 0; idx < msg.Presence.Length; idx++)
            {
                var p = msg.Presence[idx];
                mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                    channelName,
                    new JArray(ProtocolMessages.PresenceEntry(
                        (int)p.Action,
                        p.ClientId ?? defaultClientId,
                        connectionId,
                        $"{connectionId}:{msg.MsgSerial}:{idx}",
                        Clock.NowMillis,
                        p.Data)),
                    new Dictionary<string, JToken> { ["connectionId"] = connectionId }));
            }
        }

        // Spec handler shape shared by the reconnecting tests: a new connection id per attempt, ATTACH answered, PRESENCE captured, ACKed and echoed.
        private MockWebSocket ReconnectingMock(string channelName, CaptureList<ProtocolMessage> capturedPresence, string defaultClientId)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    var count = Interlocked.Increment(ref _connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage($"conn-{count}"));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        capturedPresence.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));

                        // Server echoes the presence event back to the client.
                        // This populates the LocalPresenceMap (RTP17) which is keyed by
                        // server echoes, not by the client's own enter() calls.
                        EchoPresence(mockWs, channelName, msg, $"conn-{Volatile.Read(ref _connectionCount)}", defaultClientId);
                    }
                });
            return mockWs;
        }

        // Disconnect, then let the fake clock run the retry so the client reconnects and the channel re-attaches.
        private async Task DisconnectAndReconnect(PubSubRealtimeClient client, MockWebSocket mockWs, IRealtimeChannel channel, Action beforeReconnect)
        {
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            beforeReconnect?.Invoke();

            // Reconnect — triggers reattach with new ATTACHED (non-RESUMED)
            await AdvanceTime(15000);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
        }

        // UTS: realtime/unit/RTP17i/auto-reentry-on-attached-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP17i", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP17i")]
        public async Task RTP17i_AutoReentryOnAttached()
        {
            // Setup
            var channelName = "test-RTP17i-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();
            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = ReconnectingMock(channelName, capturedPresence, "my-client");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Enter presence
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("hello"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            capturedPresence.Should().HaveCount(1);

            // Simulate disconnect and reconnect (new connectionId)
            // Clear captured to track only re-entry messages
            await DisconnectAndReconnect(client, mockWs, channel, () => capturedPresence.Clear());

            // NOTE: the re-entry is sent when the new ATTACHED is processed, which can follow the state waits; the capture is polled before the spec's assertions
            await UtsWait.PollUntil(() => capturedPresence.Count >= 1, description: "re-entry PRESENCE captured");

            // Assertions
            // RTP17i: Automatic re-entry sends ENTER for the member
            capturedPresence.Count.Should().BeGreaterThanOrEqualTo(1);

            var reenter = capturedPresence.Snapshot().FirstOrDefault(m => m.Presence[0].Action == PresenceAction.Enter);
            reenter.Should().NotBeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP17g/reentry-publishes-enter-with-data-0
        [UtsFact(Skip = "UTS spec error RTP17g: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTP17g")]
        public Task RTP17g_ReentryPublishesEnterWithData()
        {
            // SPEC ERROR RTP17g: the setup builds the client with clientId "admin" and expects enterClient("alice") to succeed, relying on
            // server-side permission, but features RTP15f says an identified client with a valid clientId must get an error when the
            // clientId argument does not match its own. Fix the UTS spec first (e.g. key auth with no clientId) - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTP17g - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTP17g1/reentry-omits-id-new-connid-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP17i", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP17g1")]
        public async Task RTP17g1_ReentryOmitsIdNewConnid()
        {
            // Setup
            var channelName = "test-RTP17g1-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();
            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = ReconnectingMock(channelName, capturedPresence, "my-client");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("hello"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // First connection is conn-1
            Volatile.Read(ref _connectionCount).Should().Be(1);

            // Disconnect and reconnect — new connectionId (conn-2)
            await DisconnectAndReconnect(client, mockWs, channel, () => capturedPresence.Clear());
            Volatile.Read(ref _connectionCount).Should().Be(2);

            // NOTE: the re-entry is sent when the new ATTACHED is processed, which can follow the state waits; the capture is polled before the spec's assertions
            await UtsWait.PollUntil(() => capturedPresence.Count >= 1, description: "re-entry PRESENCE captured");

            // Assertions
            // Re-entry message should NOT have id set because connectionId changed
            var reentry = capturedPresence.Snapshot().FirstOrDefault(m => m.Action == ProtocolMessage.MessageAction.Presence);
            reentry.Should().NotBeNull();

            var reentryPresence = reentry.Presence[0];
            reentryPresence.Action.Should().Be(PresenceAction.Enter);

            // RTP17g1: id not set when connectionId changed
            reentryPresence.Id.Should().BeNull();
            ((string)reentryPresence.Data).Should().Be("hello");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP17i/no-reentry-with-resumed-flag-1
        [UtsFact]
        [Trait("spec", "RTP17i")]
        public async Task RTP17i_NoReentryWithResumedFlag()
        {
            // Setup
            var channelName = "test-RTP17i-resumed-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();
            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1", "key-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        capturedPresence.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));

                        // Server echoes the presence event back to populate LocalPresenceMap
                        EchoPresence(mockWs, channelName, msg, "conn-1", "my-client");
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("hello"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Clear captured
            await ProcessPendingEvents();
            capturedPresence.Clear();

            // Server sends ATTACHED with RESUMED flag while already attached
            // (e.g., after a brief transport-level reconnect that preserved the connection)
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["flags"] = (int)ProtocolMessage.Flag.Resumed }));
            await ProcessPendingEvents();

            // Assertions
            // No re-entry — RESUMED flag means the server still has our presence state
            capturedPresence.Should().HaveCount(0);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP17e/failed-reentry-emits-update-error-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP17i", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP17e")]
        public async Task RTP17e_FailedReentryEmitsUpdateError()
        {
            // Setup
            var channelName = "test-RTP17e-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();
            var connectionCount = 0;
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    var count = Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage($"conn-{count}"));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        if (Volatile.Read(ref connectionCount) == 1)
                        {
                            // First connection: ACK the enter and echo back the presence event
                            mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                            EchoPresence(mockWs, channelName, msg, "conn-1", "my-client");
                        }
                        else
                        {
                            // Second connection: NACK the re-entry
                            mockWs.SendToClient(ProtocolMessages.NackMessage(msg.MsgSerial, 40160, "Presence denied", 401, 1));
                        }
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("hello"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Listen for channel UPDATE events with the re-entry failure error code.
            // Note: The ATTACHED state change itself may also emit an UPDATE event
            // (e.g., when transitioning from ATTACHED to ATTACHED with resumed=false).
            // Filter for the specific 91004 error code to distinguish re-entry failure.
            var channelEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change =>
            {
                if (change.Error != null && change.Error.Code == 91004)
                {
                    channelEvents.Add(change);
                }
            });

            // Disconnect and reconnect — re-entry will be NACKed
            await DisconnectAndReconnect(client, mockWs, channel, null);

            // Wait for the re-entry NACK to be processed
            await UtsWait.PollUntil(() => channelEvents.Count >= 1, description: "channel UPDATE with 91004");

            // Assertions
            channelEvents.Count.Should().BeGreaterThanOrEqualTo(1);

            var updateEvent = channelEvents[0];
            updateEvent.Resumed.Should().BeTrue();
            updateEvent.Error.Should().NotBeNull();
            updateEvent.Error.Code.Should().Be(91004);
            updateEvent.Error.Message.Should().Contain("my-client");
            updateEvent.Error.Cause.Should().NotBeNull();
            updateEvent.Error.Cause.Code.Should().Be(40160);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP17a/server-publishes-without-subscribe-0
        [UtsFact]
        [Trait("spec", "RTP17a")]
        public async Task RTP17a_ServerPublishesWithoutSubscribe()
        {
            // Setup
            var channelName = "test-RTP17a-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Channel with presence capability but no subscribe capability
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                            channelName,
                            new Dictionary<string, JToken> { ["flags"] = (int)ProtocolMessage.Flag.Presence }));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        // ACK the enter
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));

                        // Server delivers the presence event back to the client
                        mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                            channelName,
                            new JArray(ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "my-client", "conn-1", "conn-1:0:0", 1000))));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Check public presence map
            // NOTE: the server's PRESENCE echo is delivered asynchronously after the ACK, so pending events are drained before reading the map
            await ProcessPendingEvents();
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("my-client");

            await CloseClient(client);
        }
    }
}
