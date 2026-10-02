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
    /// Derived from uts/realtime/unit/presence/local_presence_map.md in ably/specification.
    ///
    /// Spec points: RTP17, RTP17b, RTP17h, RTP2d2
    ///
    /// <para>
    /// The spec's <c>LocalPresenceMap</c> is <c>InternalPresenceMap</c> here - a <c>PresenceMap</c>
    /// whose only override is <c>GetKey</c>, returning the clientId alone. That is RTP17h, and the
    /// first test checks it directly.
    /// </para>
    ///
    /// <para>
    /// The synthesized-LEAVE filter is the one case this file cannot test at the map level, and the
    /// spec says so: "may be implemented either inside the presence map's <c>remove()</c> method, or
    /// at the calling level (e.g., in RealtimePresence) ... the level at which this is enforced is
    /// implementation-dependent". This SDK enforces it at the calling level -
    /// <c>Presence.OnPresence</c> guards the internal map with <c>!message.IsServerSynthesized()</c>
    /// (<c>Presence.cs:593</c>) - so that test drives a channel.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class LocalPresenceMapTests : UtsTestBase
    {
        public LocalPresenceMapTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP17h/keyed-by-clientid-0
        [Fact]
        public void RTP17h_KeyedByClientIdNotMemberKey()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "user-1", "conn-A", "conn-A:0:0", 1000, "first"));
            map.Put(Member(PresenceAction.Enter, "user-1", "conn-B", "conn-B:0:0", 2000, "second"));

            map.Values.Should().HaveCount(
                1,
                "RTP17h - keyed by clientId alone, so the second entry replaces the first");
            Get(map, "user-1").Should().NotBeNull();
            Get(map, "user-1").Data.Should().Be("second");
            Get(map, "user-1").ConnectionId.Should().Be("conn-B");
        }

        // UTS: realtime/unit/RTP17b/enter-adds-to-map-0
        [Fact]
        public void RTP17b_EnterAddsToMap()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "hello"));

            Get(map, "client-1").Should().NotBeNull();
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present, "RTP2d2");
            Get(map, "client-1").Data.Should().Be("hello");
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17b/update-adds-to-map-1
        [Fact]
        public void RTP17b_UpdateWithNoPriorEntryAddsToMap()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Update, "client-1", "conn-1", "conn-1:0:0", 1000, "from-update"));

            Get(map, "client-1").Should().NotBeNull();
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present, "RTP2d2");
            Get(map, "client-1").Data.Should().Be("from-update");
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17b/enter-overwrites-enter-2
        [Fact]
        public void RTP17b_EnterAfterEnterOverwrites()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "first"));
            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:1:0", 2000, "second"));

            map.Values.Should().HaveCount(1);
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("second");
        }

        // UTS: realtime/unit/RTP17b/update-overwrites-enter-3
        [Fact]
        public void RTP17b_UpdateAfterEnterOverwrites()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered"));
            map.Put(Member(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated"));

            map.Values.Should().HaveCount(1);
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("updated");
        }

        // UTS: realtime/unit/RTP17b/present-adds-to-map-4
        [Fact]
        public void RTP17b_PresentAddsToMap()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Present, "client-1", "conn-1", "conn-1:0:0", 1000, "present"));

            Get(map, "client-1").Should().NotBeNull();
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("present");
        }

        // UTS: realtime/unit/RTP17b/non-synthesized-leave-removes-5
        [Fact]
        public void RTP17b_NonSynthesizedLeaveRemoves()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));
            Get(map, "client-1").Should().NotBeNull();

            // The connectionId is an initial substring of the id, so this is a real server LEAVE.
            var result = map.Remove(Member(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000));

            result.Should().BeTrue();
            Get(map, "client-1").Should().BeNull();
            map.Values.Should().BeEmpty();
        }

        // UTS: realtime/unit/RTP17b/synthesized-leave-ignored-6
        //
        // Driven through a channel, because this SDK applies the synthesized-LEAVE filter at the
        // calling level rather than inside the map - which the spec explicitly allows. See the
        // class note.
        [Fact]
        public async Task RTP17b_SynthesizedLeaveIgnored()
        {
            const string ChannelName = "test-RTP17b";

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("conn-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "client-1");

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

            var received = new List<PresenceMessage>();
            channel.Presence.Subscribe(msg => received.Add(msg));

            // The member enters on this connection, so it reaches the internal map.
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray
            {
                ProtocolMessages.PresenceEntry(2, "client-1", "conn-1", "conn-1:0:0", 1000, "hello"),
            }));

            await UtsClients.PollUntil(() => received.Count >= 1, "the ENTER");

            var internalMap = ((Ably.PubSub.Realtime.Presence)channel.Presence).InternalMembersMap;
            internalMap.Members.Should().ContainKey("client-1");

            received.Clear();

            // A synthesized LEAVE: the id does not start with the connectionId.
            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray
            {
                ProtocolMessages.PresenceEntry(3, "client-1", "conn-1", "synthesized-leave-id", 2000),
            }));

            await UtsClients.PollUntil(() => received.Count >= 1, "the LEAVE reached subscribers");

            internalMap.Members.Should().ContainKey(
                "client-1",
                "RTP17b - a synthesized LEAVE is not applied to the local presence map");
        }

        // UTS: realtime/unit/RTP17/multiple-clientids-coexist-0
        [Fact]
        public void RTP17_MultipleClientIdsCoexist()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100, "alice-data"));
            map.Put(Member(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100, "bob-data"));
            map.Put(Member(PresenceAction.Enter, "carol", "conn-1", "conn-1:0:2", 100, "carol-data"));

            map.Values.Should().HaveCount(3, "one connection can hold several clientIds via enterClient");
            Get(map, "alice").Data.Should().Be("alice-data");
            Get(map, "bob").Data.Should().Be("bob-data");
            Get(map, "carol").Data.Should().Be("carol-data");
        }

        // UTS: realtime/unit/RTP17/remove-one-of-multiple-1
        [Fact]
        public void RTP17_RemoveOneOfMultipleMembers()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100));

            map.Remove(Member(PresenceAction.Leave, "alice", "conn-1", "conn-1:1:0", 200));

            Get(map, "alice").Should().BeNull();
            Get(map, "bob").Should().NotBeNull();
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17/clear-resets-state-2
        [Fact]
        public void RTP17_ClearResetsState()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100));
            map.Values.Should().HaveCount(2);

            map.Clear();

            map.Values.Should().BeEmpty();
            Get(map, "alice").Should().BeNull();
            Get(map, "bob").Should().BeNull();
        }

        // UTS: realtime/unit/RTP17/get-null-unknown-clientid-3
        [Fact]
        public void RTP17_GetReturnsNullForUnknownClientId()
        {
            var map = NewMap();

            Get(map, "nonexistent").Should().BeNull();
        }

        // UTS: realtime/unit/RTP17/remove-unknown-noop-4
        [Fact]
        public void RTP17_RemoveForUnknownClientIdIsNoOp()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));

            map.Remove(Member(PresenceAction.Leave, "nonexistent", "conn-1", "conn-1:1:0", 200));

            Get(map, "alice").Should().NotBeNull("the member that was there is unaffected");
            map.Values.Should().HaveCount(1);
        }

        private static PresenceMap NewMap()
            => new InternalPresenceMap("test-channel", InternalLogger.Create());

        /// <summary>The spec's <c>map.get(clientId)</c> - RTP17h's key is the clientId alone.</summary>
        private static PresenceMessage Get(PresenceMap map, string clientId)
            => map.Members.TryGetValue(clientId, out var member) ? member : null;

        private static PresenceMessage Member(
            PresenceAction action,
            string clientId,
            string connectionId,
            string id,
            long timestamp,
            object data = null)
            => new PresenceMessage(action, clientId, data)
            {
                ConnectionId = connectionId,
                Id = id,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
            };
    }
}
