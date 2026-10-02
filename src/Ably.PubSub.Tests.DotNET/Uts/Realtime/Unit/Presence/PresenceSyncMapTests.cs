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
    /// Derived from uts/realtime/unit/presence/presence_sync.md in ably/specification.
    ///
    /// Spec points: RTP18, RTP18a, RTP18b, RTP18c, RTP19, RTP19a, RTP2h2a
    ///
    /// <para>
    /// The second file to test <c>PresenceMap</c> directly - see <see cref="PresenceMapTests"/> for
    /// the interface mapping, which applies here too. This one is about the sync lifecycle: what
    /// <c>startSync</c>/<c>endSync</c> do to the flag, and which members survive a sync.
    /// </para>
    ///
    /// <para>
    /// Named PresenceSyncMapTests to keep it apart from the integration-tier
    /// <c>Realtime/Integration/Presence/PresenceSyncTests</c>, which covers the same spec area
    /// against a live sandbox.
    /// </para>
    ///
    /// <para>
    /// One layering difference worth knowing before reading the assertions. The spec has
    /// <c>endSync()</c> return "synthesized LEAVE events", shaped per RTP19 with the action set to
    /// LEAVE, the id nulled and the timestamp set to now. This SDK splits that in two:
    /// <c>PresenceMap.EndSync</c> decides *which* members are residual and returns them exactly as
    /// stored, and <c>Presence.EndSync</c> (<c>Presence.cs:637-643</c>) then rewrites each one into
    /// the RTP19 shape before publishing it. Both halves are present and correct - this is not a
    /// deviation - so the map-level tests here assert the selection, and
    /// <see cref="RTP19_SynthesizedLeaveHasNullIdAndCurrentTimestamp"/> drives a channel so it can
    /// assert the shape a subscriber actually receives, which is what RTP19 is really about.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class PresenceSyncMapTests : UtsTestBase
    {
        public PresenceSyncMapTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP18a/startsync-sets-flag-0
        [Fact]
        public void RTP18a_StartSyncSetsFlag()
        {
            var map = NewMap();

            map.SyncInProgress.Should().BeFalse();

            map.StartSync();

            map.SyncInProgress.Should().BeTrue();
        }

        // UTS: realtime/unit/RTP18b/endsync-clears-flag-0
        [Fact]
        public void RTP18b_EndSyncClearsFlag()
        {
            var map = NewMap();

            map.StartSync();
            map.SyncInProgress.Should().BeTrue();

            map.EndSync();

            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19/stale-members-leave-after-sync-0
        [Fact]
        public void RTP19_StaleMembersLeaveAfterSync()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));
            map.Values.Should().HaveCount(2);

            map.StartSync();
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            var leaveEvents = map.EndSync();

            leaveEvents.Should().HaveCount(1, "bob was never mentioned in the sync");
            leaveEvents[0].ClientId.Should().Be("bob");

            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
            Get(map, "bob", "c2").Should().BeNull();
        }

        // UTS: realtime/unit/RTP19/synth-leave-null-id-timestamp-1
        //
        // Driven through a channel rather than the map, because the RTP19 rewrite - action to
        // LEAVE, id to null, timestamp to now - is what Presence.EndSync does to the residuals the
        // map hands it, and a subscriber is where it becomes observable. See the class note.
        [Fact]
        public async Task RTP19_SynthesizedLeaveHasNullIdAndCurrentTimestamp()
        {
            const string ChannelName = "test-RTP19";

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

            // The sync names nobody, so bob - who entered before it - is residual.
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

            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray
            {
                ProtocolMessages.PresenceEntry(2, "bob", "c2", "c2:0:0", 100, "bob-data"),
            }));

            await UtsClients.PollUntil(() => received.Count >= 1, "bob entered");
            received.Clear();

            var beforeTime = DateTimeOffset.UtcNow;

            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray()));

            await UtsClients.PollUntil(() => received.Count >= 1, "the synthesized LEAVE for bob");

            var afterTime = DateTimeOffset.UtcNow;

            var leave = received[0];
            leave.Action.Should().Be(PresenceAction.Leave);
            leave.ClientId.Should().Be("bob");
            leave.ConnectionId.Should().Be("c2");
            leave.Data.Should().Be("bob-data", "the original attributes are preserved");
            leave.Id.Should().BeNull("RTP19 - the synthesized leave has no id");
            leave.Timestamp.Should().NotBeNull();
            leave.Timestamp.Value.Should().BeOnOrAfter(beforeTime.AddSeconds(-1));
            leave.Timestamp.Value.Should().BeOnOrBefore(afterTime.AddSeconds(1));
        }

        // UTS: realtime/unit/RTP19/updated-members-survive-sync-2
        [Fact]
        public void RTP19_MembersUpdatedDuringSyncSurvive()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "carol", "c3", "c3:0:0", 100));

            map.StartSync();

            // Alice arrives in the sync itself, bob in a PRESENCE message during it.
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));
            map.Put(Member(PresenceAction.Update, "bob", "c2", "c2:1:0", 200, "new-data"));

            var leaveEvents = map.EndSync();

            leaveEvents.Should().HaveCount(1, "only carol went unmentioned");
            leaveEvents[0].ClientId.Should().Be("carol");

            map.Values.Should().HaveCount(2);
            Get(map, "alice", "c1").Should().NotBeNull();
            Get(map, "bob", "c2").Should().NotBeNull();
            Get(map, "bob", "c2").Data.Should().Be("new-data");
        }

        // UTS: realtime/unit/RTP18a/new-sync-discards-previous-1
        [Fact]
        public void RTP18a_NewSyncDiscardsPrevious()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // A new sequence identifier arrives before the first sync finished, so the first one's
            // residual bookkeeping is thrown away.
            map.StartSync();
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:2:0", 300));
            map.Put(Member(PresenceAction.Present, "bob", "c2", "c2:1:0", 300));

            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty("both members appeared in the sync that actually finished");
        }

        // UTS: realtime/unit/RTP18c/single-message-sync-0
        [Fact]
        public void RTP18c_SingleMessageSync()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));
            var leaveEvents = map.EndSync();

            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].ClientId.Should().Be("bob");

            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19a/no-has-presence-clears-members-0
        [Fact]
        public void RTP19a_NoHasPresenceClearsMembers()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100, "a"));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100, "b"));
            map.Put(Member(PresenceAction.Enter, "carol", "c3", "c3:0:0", 100, "c"));

            // An ATTACHED with no HAS_PRESENCE is, at this level, a sync that mentions nobody.
            map.StartSync();
            var leaveEvents = map.EndSync();

            leaveEvents.Should().HaveCount(3);

            AssertResidual(leaveEvents, "alice", "a");
            AssertResidual(leaveEvents, "bob", "b");
            AssertResidual(leaveEvents, "carol", "c");

            map.Values.Should().BeEmpty();
        }

        // UTS: realtime/unit/RTP2h2a/leave-during-sync-absent-cleanup-0
        //
        // DEVIATION, the same missing ABSENT handling as D29 - see
        // PresenceMapTests.RTP2h2a_LeaveDuringSyncStoresAbsent. This case is the interaction
        // between the two halves: a LEAVE during a sync should mark the member ABSENT and emit
        // nothing, and endSync should then delete it silently, with no synthesized LEAVE (that is
        // reserved for members the sync simply never mentioned).
        [DeviationFact]
        public void RTP2h2a_LeaveDuringSyncAbsentCleanup()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();

            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            var leaveResult = map.Remove(Member(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            leaveResult.Should().BeFalse("nothing is emitted for a LEAVE during a sync");
            Get(map, "bob", "c2").Should().NotBeNull("RTP2h2a - marked ABSENT, not deleted");
            Get(map, "bob", "c2").Action.Should().Be(PresenceAction.Absent);

            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty(
                "RTP2h2b deletes ABSENT members silently - a synthesized LEAVE is only for members "
                + "the sync never mentioned");
            Get(map, "bob", "c2").Should().BeNull();

            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP19/empty-map-sync-no-leaves-3
        [Fact]
        public void RTP19_EmptyMapSyncProducesNoLeaves()
        {
            var map = NewMap();

            map.StartSync();
            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:0:0", 100));
            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty();
            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP18/endsync-without-startsync-noop-0
        [Fact]
        public void RTP18_EndSyncWithoutStartSyncIsNoOp()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));

            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty();
            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19/stale-sync-removes-from-residuals-4
        [Fact]
        public void RTP19_StaleSyncMessageStillRemovesFromResiduals()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:5:0", 500, "original"));

            map.StartSync();

            // Older msgSerial, so the put is rejected - but alice has still been *seen*.
            var result = map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:3:0", 300, "stale"));

            var leaveEvents = map.EndSync();

            result.Should().BeFalse("the stale message does not overwrite");
            leaveEvents.Should().BeEmpty(
                "the residual bookkeeping has to happen before the newness check, or a stale SYNC "
                + "entry would evict a member who is still there");
            map.Values.Should().HaveCount(1);
            Get(map, "alice", "c1").Should().NotBeNull();
            Get(map, "alice", "c1").Data.Should().Be("original");
        }

        // UTS: realtime/unit/RTP19/presence-echoes-then-sync-preserves-5
        [Fact]
        public void RTP19_PresenceEchoesThenSyncPreservesAllMembers()
        {
            var map = NewMap();

            for (var i = 0; i < 3; i++)
            {
                map.Put(Member(PresenceAction.Enter, $"user-{i}", "c1", $"c1:{i}:0", 100, $"data-{i}"));
            }

            map.Values.Should().HaveCount(3);

            map.StartSync();

            // The SYNC repeats the same ids the echoes carried, so every put is stale.
            for (var i = 0; i < 3; i++)
            {
                map.Put(Member(PresenceAction.Present, $"user-{i}", "c1", $"c1:{i}:0", 100, $"data-{i}"));
            }

            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty("every member was seen, stale ids notwithstanding");
            map.Values.Should().HaveCount(3);

            for (var i = 0; i < 3; i++)
            {
                var member = Get(map, $"user-{i}", "c1");
                member.Should().NotBeNull();
                member.Data.Should().Be($"data-{i}");
            }
        }

        // UTS: realtime/unit/RTP19/new-member-during-sync-survives-6
        [Fact]
        public void RTP19_NewMemberDuringSyncSurvives()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));

            map.StartSync();

            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));
            map.Put(Member(PresenceAction.Enter, "dave", "c4", "c4:0:0", 200));

            var leaveEvents = map.EndSync();

            leaveEvents.Should().BeEmpty();
            map.Values.Should().HaveCount(2);
            Get(map, "dave", "c4").Should().NotBeNull("a member who joined during the sync is not stale");
        }

        /// <summary>
        /// One residual member, identified and with its attributes carried through. The RTP19
        /// rewrite to a LEAVE happens a layer up - see the class note - so the action, id and
        /// timestamp are asserted in
        /// <see cref="RTP19_SynthesizedLeaveHasNullIdAndCurrentTimestamp"/> instead.
        /// </summary>
        private static void AssertResidual(PresenceMessage[] leaveEvents, string clientId, string data)
        {
            var residual = leaveEvents.FirstOrDefault(e => e.ClientId == clientId);
            residual.Should().NotBeNull();
            residual.Data.Should().Be(data);
        }

        private static PresenceMap NewMap() => new PresenceMap("test-channel", InternalLogger.Create());

        /// <summary>See the note on <see cref="PresenceMapTests"/>'s equivalent: the key order is D6.</summary>
        private static PresenceMessage Get(PresenceMap map, string clientId, string connectionId)
        {
            var key = map.GetKey(new PresenceMessage
            {
                ClientId = clientId,
                ConnectionId = connectionId,
            });

            return map.Members.TryGetValue(key, out var member) ? member : null;
        }

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
