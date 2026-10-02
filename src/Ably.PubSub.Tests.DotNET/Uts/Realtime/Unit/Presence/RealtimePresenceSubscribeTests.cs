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

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Presence
{
    /// <summary>
    /// Derived from uts/realtime/unit/presence/realtime_presence_subscribe.md in ably/specification.
    ///
    /// Spec points: RTP6, RTP6a, RTP6b, RTP6d, RTP6e, RTP7a, RTP7b, RTP7c, RTP2d2
    ///
    /// <para>
    /// <c>realtime/unit/RTP6e/subscribe-no-attach-option-0</c> is not translated: there is no
    /// <c>attachOnSubscribe</c> channel option in this SDK. See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// The spec's "subscribe to multiple actions in one call" is two <c>Subscribe(action, handler)</c>
    /// calls with the same handler here - <c>Presence.Subscribe</c> takes one action, not a set.
    /// The behaviour asserted is the same: that handler sees those two actions and not the third.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceSubscribeTests : UtsTestBase
    {
        private const string ChannelName = "test-RTP6";

        public RealtimePresenceSubscribeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP6a/subscribe-all-presence-events-0
        [Fact]
        public async Task RTP6a_SubscribeAllPresenceEvents()
        {
            var (mockWs, channel) = await AttachedChannel();

            var received = new List<PresenceMessage>();
            channel.Presence.Subscribe(msg => received.Add(msg));

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100, "hello"));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(4, "alice", "c1", "c1:0:1", 101, "updated"));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(3, "alice", "c1", "c1:0:2", 102));

            await UtsClients.PollUntil(() => received.Count >= 3, "all three presence events");

            received.Should().HaveCount(3);
            received[0].Action.Should().Be(PresenceAction.Enter);
            received[0].ClientId.Should().Be("alice");
            received[1].Action.Should().Be(PresenceAction.Update);
            received[1].Data.Should().Be("updated");
            received[2].Action.Should().Be(PresenceAction.Leave);
        }

        // UTS: realtime/unit/RTP6b/subscribe-filtered-by-action-0
        [Fact]
        public async Task RTP6b_SubscribeFilteredByAction()
        {
            var (mockWs, channel) = await AttachedChannel();

            var enterEvents = new List<PresenceMessage>();
            var leaveEvents = new List<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Enter, msg => enterEvents.Add(msg));
            channel.Presence.Subscribe(PresenceAction.Leave, msg => leaveEvents.Add(msg));

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(4, "bob", "c2", "c2:0:0", 101));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(3, "carol", "c3", "c3:0:0", 102));

            await UtsClients.PollUntil(
                () => enterEvents.Count >= 1 && leaveEvents.Count >= 1,
                "one ENTER and one LEAVE");

            enterEvents.Should().HaveCount(1);
            enterEvents[0].Action.Should().Be(PresenceAction.Enter);

            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].Action.Should().Be(PresenceAction.Leave);

            enterEvents.Should().NotContain(msg => msg.Action == PresenceAction.Update);
            leaveEvents.Should().NotContain(msg => msg.Action == PresenceAction.Update);
        }

        // UTS: realtime/unit/RTP6b/subscribe-filtered-multiple-actions-1
        [Fact]
        public async Task RTP6b_SubscribeFilteredMultipleActions()
        {
            var (mockWs, channel) = await AttachedChannel();

            var enterLeaveEvents = new List<PresenceMessage>();
            void Handler(PresenceMessage msg) => enterLeaveEvents.Add(msg);

            channel.Presence.Subscribe(PresenceAction.Enter, Handler);
            channel.Presence.Subscribe(PresenceAction.Leave, Handler);

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(4, "bob", "c2", "c2:0:0", 101));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(3, "carol", "c3", "c3:0:0", 102));

            await UtsClients.PollUntil(() => enterLeaveEvents.Count >= 2, "the ENTER and the LEAVE");

            enterLeaveEvents.Should().HaveCount(2, "the UPDATE is filtered out");
            enterLeaveEvents[0].Action.Should().Be(PresenceAction.Enter);
            enterLeaveEvents[1].Action.Should().Be(PresenceAction.Leave);
        }

        // UTS: realtime/unit/RTP6d/subscribe-implicitly-attaches-0
        [Fact]
        public async Task RTP6d_SubscribeImplicitlyAttaches()
        {
            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Presence.Subscribe(msg => { });
            await attached;

            attachCount.Should().Be(1);
            channel.State.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTP7c/unsubscribe-all-listeners-0
        [Fact]
        public async Task RTP7c_UnsubscribeAllListeners()
        {
            var (mockWs, channel) = await AttachedChannel();

            var eventsA = new List<PresenceMessage>();
            var eventsB = new List<PresenceMessage>();
            channel.Presence.Subscribe(msg => eventsA.Add(msg));
            channel.Presence.Subscribe(msg => eventsB.Add(msg));

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100));

            await UtsClients.PollUntil(
                () => eventsA.Count >= 1 && eventsB.Count >= 1,
                "both listeners saw the first event");

            channel.Presence.Unsubscribe();

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(3, "alice", "c1", "c1:0:1", 101));

            // Nothing to wait for, so wait for the channel to have processed it instead: a second
            // presence message delivered and then a get() that round-trips the workflow.
            await channel.Presence.GetAsync(waitForSync: false);

            eventsA.Should().HaveCount(1, "no events after unsubscribe");
            eventsB.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP7a/unsubscribe-specific-listener-0
        [Fact]
        public async Task RTP7a_UnsubscribeSpecificListener()
        {
            var (mockWs, channel) = await AttachedChannel();

            var eventsA = new List<PresenceMessage>();
            var eventsB = new List<PresenceMessage>();
            void ListenerA(PresenceMessage msg) => eventsA.Add(msg);
            void ListenerB(PresenceMessage msg) => eventsB.Add(msg);

            channel.Presence.Subscribe(ListenerA);
            channel.Presence.Subscribe(ListenerB);

            channel.Presence.Unsubscribe(ListenerA);

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100));

            await UtsClients.PollUntil(() => eventsB.Count >= 1, "the remaining listener fired");

            eventsA.Should().BeEmpty("unsubscribed");
            eventsB.Should().HaveCount(1, "still subscribed");
        }

        // UTS: realtime/unit/RTP7b/unsubscribe-for-specific-action-0
        [Fact]
        public async Task RTP7b_UnsubscribeForSpecificAction()
        {
            var (mockWs, channel) = await AttachedChannel();

            var received = new List<PresenceMessage>();
            void Listener(PresenceMessage msg) => received.Add(msg);

            channel.Presence.Subscribe(PresenceAction.Enter, Listener);
            channel.Presence.Subscribe(PresenceAction.Leave, Listener);

            channel.Presence.Unsubscribe(PresenceAction.Enter, Listener);

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100));
            SendPresence(mockWs, ProtocolMessages.PresenceEntry(3, "alice", "c1", "c1:0:1", 101));

            await UtsClients.PollUntil(() => received.Count >= 1, "the LEAVE");

            received.Should().HaveCount(1, "the ENTER subscription was removed");
            received[0].Action.Should().Be(PresenceAction.Leave);
        }

        // UTS: realtime/unit/RTP6/presence-events-update-map-0
        [Fact]
        public async Task RTP6_PresenceEventsUpdateMap()
        {
            var (mockWs, channel) = await AttachedChannel();

            var seen = new List<PresenceMessage>();
            channel.Presence.Subscribe(msg => seen.Add(msg));

            SendPresence(mockWs, ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100, "hello"));

            // The subscriber fires once the message has been applied, so it is the signal that the
            // presence map has it too.
            await UtsClients.PollUntil(() => seen.Count >= 1, "the member reached the presence map");

            var members = (await channel.Presence.GetAsync(waitForSync: false)).ToList();

            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");
            members[0].Data.Should().Be("hello");
            members[0].Action.Should().Be(
                PresenceAction.Present,
                "RTP2d2 - a member in the map is stored as PRESENT whatever brought it there");
        }

        // UTS: realtime/unit/RTP6/multiple-presence-in-single-message-1
        [Fact]
        public async Task RTP6_MultiplePresenceInSingleMessage()
        {
            var (mockWs, channel) = await AttachedChannel();

            var received = new List<PresenceMessage>();
            channel.Presence.Subscribe(msg => received.Add(msg));

            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray
            {
                ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100),
                ProtocolMessages.PresenceEntry(2, "bob", "c2", "c2:0:0", 100),
                ProtocolMessages.PresenceEntry(2, "carol", "c3", "c3:0:0", 100),
            }));

            await UtsClients.PollUntil(() => received.Count >= 3, "all three members");

            received.Should().HaveCount(3);
            received[0].ClientId.Should().Be("alice");
            received[1].ClientId.Should().Be("bob");
            received[2].ClientId.Should().Be("carol");
        }

        private static void SendPresence(MockWebSocket mockWs, JObject entry)
            => mockWs.SendToClient(
                ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray { entry }));

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            return (mockWs, channel);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
