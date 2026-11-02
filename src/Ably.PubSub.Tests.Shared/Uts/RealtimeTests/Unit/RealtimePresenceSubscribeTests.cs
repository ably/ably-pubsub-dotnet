// RealtimePresence Subscribe/Unsubscribe Tests (RTP6, RTP7)
// Derived from uts/realtime/unit/presence/realtime_presence_subscribe.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key stand-in "fake.key:secret" as the spec sets it; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class RealtimePresenceSubscribeTests : UtsUnitTestBase
    {
        public RealtimePresenceSubscribeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's handler: ATTACH answered with ATTACHED (optionally HAS_PRESENCE); the attach count is exposed for the implicit-attach tests.
        private static MockWebSocket AttachingMock(string channelName, bool hasPresence, Action onAttach = null)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        onAttach?.Invoke();
                        mockWs.SendToClient(hasPresence ? ProtocolMessages.AttachedWithPresenceMessage(channelName) : ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            return mockWs;
        }

        private static JObject PresenceEvent(string channelName, int action, string clientId, string connectionId, string id, long timestamp, object data = null)
            => ProtocolMessages.PresenceProtocolMessage(channelName, new JArray(ProtocolMessages.PresenceEntry(action, clientId, connectionId, id, timestamp, data)));

        // UTS: realtime/unit/RTP6a/subscribe-all-presence-events-0
        [UtsFact]
        [Trait("spec", "RTP6a")]
        public async Task RTP6a_SubscribeAllPresenceEvents()
        {
            // Setup
            var channelName = "test-RTP6a-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, true);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedEvents = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(message => receivedEvents.Add(message));

            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Server delivers ENTER, UPDATE, and LEAVE events
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000));
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Update, "alice", "c1", "c1:1:0", 2000, "updated"));
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Leave, "alice", "c1", "c1:2:0", 3000));
            await ProcessPendingEvents();

            // Assertions
            receivedEvents.Should().HaveCount(3);
            receivedEvents[0].Action.Should().Be(PresenceAction.Enter);
            receivedEvents[0].ClientId.Should().Be("alice");
            receivedEvents[1].Action.Should().Be(PresenceAction.Update);
            ((string)receivedEvents[1].Data).Should().Be("updated");
            receivedEvents[2].Action.Should().Be(PresenceAction.Leave);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6b/subscribe-filtered-by-action-0
        [UtsFact]
        [Trait("spec", "RTP6b")]
        public async Task RTP6b_SubscribeFilteredByAction()
        {
            // Setup
            var channelName = "test-RTP6b-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterEvents = new CaptureList<PresenceMessage>();
            var leaveEvents = new CaptureList<PresenceMessage>();

            channel.Presence.Subscribe(PresenceAction.Enter, message => enterEvents.Add(message));
            channel.Presence.Subscribe(PresenceAction.Leave, message => leaveEvents.Add(message));

            // Server delivers all three action types
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Update, "alice", "c1", "c1:1:0", 2000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Leave, "alice", "c1", "c1:2:0", 3000))));
            await ProcessPendingEvents();

            // Assertions
            // ENTER listener only gets ENTER events
            enterEvents.Should().HaveCount(1);
            enterEvents[0].Action.Should().Be(PresenceAction.Enter);

            // LEAVE listener only gets LEAVE events
            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].Action.Should().Be(PresenceAction.Leave);

            // Neither listener receives UPDATE
            enterEvents.Snapshot().Should().NotContain(e => e.Action == PresenceAction.Update);
            leaveEvents.Snapshot().Should().NotContain(e => e.Action == PresenceAction.Update);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6b/subscribe-filtered-multiple-actions-1
        [UtsFact]
        [Trait("spec", "RTP6b")]
        public async Task RTP6b_SubscribeFilteredMultipleActions()
        {
            // Setup
            var channelName = "test-RTP6b-multi-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterLeaveEvents = new CaptureList<PresenceMessage>();

            // NOTE: Presence.Subscribe takes a single action, so the spec's actions: [ENTER, LEAVE] is one subscription per action sharing one handler
            Action<PresenceMessage> handler = message => enterLeaveEvents.Add(message);
            channel.Presence.Subscribe(PresenceAction.Enter, handler);
            channel.Presence.Subscribe(PresenceAction.Leave, handler);

            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Update, "alice", "c1", "c1:1:0", 2000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Leave, "alice", "c1", "c1:2:0", 3000))));
            await ProcessPendingEvents();

            // Assertions
            // Only ENTER and LEAVE events received — UPDATE filtered out
            enterLeaveEvents.Should().HaveCount(2);
            enterLeaveEvents[0].Action.Should().Be(PresenceAction.Enter);
            enterLeaveEvents[1].Action.Should().Be(PresenceAction.Leave);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6d/subscribe-implicitly-attaches-0
        [UtsFact]
        [Trait("spec", "RTP6d")]
        public async Task RTP6d_SubscribeImplicitlyAttaches()
        {
            // Setup
            var channelName = "test-RTP6d-" + Guid.NewGuid().ToString("N");

            var attachCount = 0;
            var mockWs = AttachingMock(channelName, false, () => Interlocked.Increment(ref attachCount));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            // Subscribe without explicitly attaching — should trigger implicit attach
            channel.Presence.Subscribe(message => { });

            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            Volatile.Read(ref attachCount).Should().Be(1);
            channel.State.Should().Be(ChannelState.Attached);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6e/subscribe-no-attach-option-0
        [UtsFact(Skip = "Missing SDK public API: ChannelOptions#attachOnSubscribe (TB4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTP6e")]
        public async Task RTP6e_SubscribeNoAttachOption()
        {
            // NOTE: ChannelOptions has no attachOnSubscribe, and this test subscribes before attaching and relies on the option (realtime-mapping.md section 12, Missing APIs)
            // channel_name = "test-RTP6e-${random_id()}"
            // attach_count = 0
            // mock_ws = MockWebSocket(onConnectionAttempt: ..., onMessageFromClient: IF msg.action == ATTACH: attach_count++)
            // client = Realtime(options: ClientOptions(key: "fake.key:secret", autoConnect: false))
            // channel = client.channels.get(channel_name, options: ChannelOptions(attachOnSubscribe: false))
            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // ASSERT channel.state == ChannelState.initialized
            // channel.presence.subscribe((event) => {})
            // Channel stays in INITIALIZED — no implicit attach
            // ASSERT channel.state == ChannelState.initialized
            // ASSERT attach_count == 0
            // CLOSE_CLIENT(client)
            await Task.CompletedTask;
        }

        // UTS: realtime/unit/RTP7c/unsubscribe-all-listeners-0
        [UtsFact]
        [Trait("spec", "RTP7c")]
        public async Task RTP7c_UnsubscribeAllListeners()
        {
            // Setup
            var channelName = "test-RTP7c-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var eventsA = new CaptureList<PresenceMessage>();
            var eventsB = new CaptureList<PresenceMessage>();

            channel.Presence.Subscribe(message => eventsA.Add(message));
            channel.Presence.Subscribe(message => eventsB.Add(message));

            // Deliver first event — both listeners receive it
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000));
            await ProcessPendingEvents();

            eventsA.Should().HaveCount(1);
            eventsB.Should().HaveCount(1);

            // Unsubscribe all
            channel.Presence.Unsubscribe();

            // Deliver second event — no listeners receive it
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Enter, "bob", "c2", "c2:0:0", 2000));
            await ProcessPendingEvents();

            // Assertions
            // No new events after unsubscribe
            eventsA.Should().HaveCount(1);
            eventsB.Should().HaveCount(1);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP7a/unsubscribe-specific-listener-0
        [UtsFact]
        [Trait("spec", "RTP7a")]
        public async Task RTP7a_UnsubscribeSpecificListener()
        {
            // Setup
            var channelName = "test-RTP7a-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var eventsA = new CaptureList<PresenceMessage>();
            var eventsB = new CaptureList<PresenceMessage>();

            Action<PresenceMessage> listenerA = message => eventsA.Add(message);
            Action<PresenceMessage> listenerB = message => eventsB.Add(message);

            channel.Presence.Subscribe(listenerA);
            channel.Presence.Subscribe(listenerB);

            // Unsubscribe only listener_a
            channel.Presence.Unsubscribe(listenerA);

            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000));
            await ProcessPendingEvents();

            // Assertions
            // Unsubscribed — no events
            eventsA.Should().HaveCount(0);

            // Still subscribed — receives event
            eventsB.Should().HaveCount(1);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP7b/unsubscribe-for-specific-action-0
        [UtsFact]
        [Trait("spec", "RTP7b")]
        public async Task RTP7b_UnsubscribeForSpecificAction()
        {
            // Setup
            var channelName = "test-RTP7b-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var received = new CaptureList<PresenceMessage>();
            Action<PresenceMessage> listener = message => received.Add(message);

            // Subscribe to both ENTER and LEAVE
            channel.Presence.Subscribe(PresenceAction.Enter, listener);
            channel.Presence.Subscribe(PresenceAction.Leave, listener);

            // Unsubscribe only for ENTER
            channel.Presence.Unsubscribe(PresenceAction.Enter, listener);

            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Leave, "alice", "c1", "c1:1:0", 2000))));
            await ProcessPendingEvents();

            // Assertions
            // Only LEAVE received — ENTER subscription was removed
            received.Should().HaveCount(1);
            received[0].Action.Should().Be(PresenceAction.Leave);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6/presence-events-update-map-0
        [UtsFact]
        [Trait("spec", "RTP6")]
        public async Task RTP6_PresenceEventsUpdateMap()
        {
            // Setup
            var channelName = "test-RTP6-map-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            channel.Presence.Subscribe(message => { });

            // Server delivers ENTER
            mockWs.SendToClient(PresenceEvent(channelName, (int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000, "hello"));
            await ProcessPendingEvents();

            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");
            ((string)members[0].Data).Should().Be("hello");

            // Stored as PRESENT per RTP2d2
            members[0].Action.Should().Be(PresenceAction.Present);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP6/multiple-presence-in-single-message-1
        [UtsFact]
        [Trait("spec", "RTP6")]
        public async Task RTP6_MultiplePresenceInSingleMessage()
        {
            // Setup
            var channelName = "test-RTP6-batch-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var received = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(message => received.Add(message));

            // Server delivers multiple presence events in one ProtocolMessage
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice", "c1", "c1:0:0", 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "bob", "c2", "c2:0:0", 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "carol", "c3", "c3:0:0", 1000))));
            await ProcessPendingEvents();

            // Assertions
            received.Should().HaveCount(3);
            received[0].ClientId.Should().Be("alice");
            received[1].ClientId.Should().Be("bob");
            received[2].ClientId.Should().Be("carol");

            await CloseClient(client);
        }
    }
}
