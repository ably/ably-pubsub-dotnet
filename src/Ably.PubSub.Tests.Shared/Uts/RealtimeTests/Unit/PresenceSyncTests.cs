// Presence Sync Tests (RTP18, RTP18a, RTP18b, RTP18c, RTP19, RTP19a)
// Derived from uts/realtime/unit/presence/presence_sync.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: white-box: drives the internal PresenceMap directly (realtime-mapping.md section 14); shape tags S-1 (put returns bool), S-2 (remove returns bool), S-3 (get is a lookup in the internal Members dictionary), S-4 (EndSync returns the removed members as stored, not synthesised LEAVEs)

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
    public class PresenceSyncTests : UtsUnitTestBase
    {
        public PresenceSyncTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The specs' PresenceMap()
        private static PresenceMap NewMap()
        {
            return new PresenceMap("test", InternalLogger.Create(LogLevel.Debug, new CapturingLogSink()));
        }

        // The specs' PresenceMessage(action:, clientId:, connectionId:, id:, timestamp:, data:); timestamp is epoch milliseconds
        private static PresenceMessage Msg(PresenceAction action, string clientId, string connectionId, string id, long timestamp, object data = null)
        {
            return new PresenceMessage
            {
                Action = action,
                ClientId = clientId,
                ConnectionId = connectionId,
                Id = id,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                Data = data,
            };
        }

        // S-3: the specs' map.get(memberKey), looked up in the internal Members dictionary by the SDK's own member key
        private static PresenceMessage Get(PresenceMap map, string connectionId, string clientId)
        {
            var key = new PresenceMessage { ConnectionId = connectionId, ClientId = clientId }.MemberKey;
            return map.Members.TryGetValue(key, out var member) ? member : null;
        }

        // UTS: realtime/unit/RTP18a/startsync-sets-flag-0
        [Fact]
        [Trait("spec", "RTP18a")]
        public void RTP18a_StartsyncSetsFlag()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.SyncInProgress.Should().BeFalse();

            map.StartSync();

            // Assertions
            map.SyncInProgress.Should().BeTrue();
        }

        // UTS: realtime/unit/RTP18b/endsync-clears-flag-0
        [Fact]
        [Trait("spec", "RTP18b")]
        public void RTP18b_EndsyncClearsFlag()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.StartSync();
            map.SyncInProgress.Should().BeTrue();

            map.EndSync();

            // Assertions
            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19/stale-members-leave-after-sync-0
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_StaleMembersLeaveAfterSync()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with two members
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.Values.Should().HaveCount(2);

            // Start sync — only alice appears in the sync data
            map.StartSync();
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // End sync — bob was not updated, gets removed
            var leaveEvents = map.EndSync();

            // Assertions
            // Bob gets a synthesized LEAVE
            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].ClientId.Should().Be("bob");

            // NOTE: S-4: EndSync returns the removed member as stored (action PRESENT); the synthesised LEAVE (RTP19) is built in Presence.EndSync, so no assertion on the action is emitted here
            // ASSERT leave_events[0].action == LEAVE

            // Only alice remains
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c2", "bob").Should().BeNull();
        }

        // UTS: realtime/unit/RTP19/synth-leave-null-id-timestamp-1
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_SynthLeaveNullIdTimestamp()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100, "bob-data"));

            var beforeTime = DateTimeOffset.UtcNow;

            map.StartSync();

            // No messages for bob during sync
            var leaveEvents = map.EndSync();

            var afterTime = DateTimeOffset.UtcNow;

            // Assertions
            leaveEvents.Should().HaveCount(1);

            var leave = leaveEvents[0];

            // NOTE: S-4: the removed member is returned as stored; the LEAVE action, null id and current timestamp (RTP19) are set in Presence.EndSync, so no assertion on them is emitted here
            // ASSERT leave.action == LEAVE
            leave.ClientId.Should().Be("bob");
            leave.ConnectionId.Should().Be("c2");

            // Original attributes preserved
            leave.Data.Should().Be("bob-data");

            // RTP19: id set to null
            // ASSERT leave.id IS null
            // RTP19: timestamp set to current time
            // ASSERT leave.timestamp >= before_time
            // ASSERT leave.timestamp <= after_time
        }

        // UTS: realtime/unit/RTP19/updated-members-survive-sync-2
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_UpdatedMembersSurviveSync()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with three members
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "carol", "c3", "c3:0:0", 100));

            map.StartSync();

            // Alice arrives via SYNC (PRESENT action)
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // Bob arrives via PRESENCE during sync (UPDATE action)
            map.Put(Msg(PresenceAction.Update, "bob", "c2", "c2:1:0", 200, "new-data"));

            // Carol does NOT appear during sync
            var leaveEvents = map.EndSync();

            // Assertions
            // Only carol is stale
            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].ClientId.Should().Be("carol");

            // Alice and bob survive
            map.Values.Should().HaveCount(2);
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c2", "bob").Should().NotBeNull();
            Get(map, "c2", "bob").Data.Should().Be("new-data");
        }

        // UTS: realtime/unit/RTP18a/new-sync-discards-previous-1
        [Fact]
        [Trait("spec", "RTP18a")]
        public void RTP18a_NewSyncDiscardsPrevious()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            // First sync starts — only alice appears
            map.StartSync();
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // Before first sync ends, a NEW sync starts (new sequence identifier)
            // This discards the previous sync — bob is no longer marked as residual from the first sync
            map.StartSync();

            // In the new sync, both alice and bob appear
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:2:0", 300));
            map.Put(Msg(PresenceAction.Present, "bob", "c2", "c2:1:0", 300));

            var leaveEvents = map.EndSync();

            // Assertions
            // No stale members — both were seen in the new sync
            leaveEvents.Should().HaveCount(0);

            map.Values.Should().HaveCount(2);
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c2", "bob").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP18c/single-message-sync-0
        [Fact]
        [Trait("spec", "RTP18c")]
        public void RTP18c_SingleMessageSync()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with alice and bob
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            // Single-message sync: start, put one member, end immediately
            map.StartSync();
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));
            var leaveEvents = map.EndSync();

            // Assertions
            // Bob was not in the sync — gets LEAVE
            leaveEvents.Should().HaveCount(1);
            leaveEvents[0].ClientId.Should().Be("bob");

            // NOTE: S-4: the removed member is returned as stored; the synthesised LEAVE action (RTP19) is built in Presence.EndSync
            // ASSERT leave_events[0].action == LEAVE
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();
            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19a/no-has-presence-clears-members-0
        [Fact]
        [Trait("spec", "RTP19a")]
        public void RTP19a_NoHasPresenceClearsMembers()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with members
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100, "a"));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100, "b"));
            map.Put(Msg(PresenceAction.Enter, "carol", "c3", "c3:0:0", 100, "c"));

            // No HAS_PRESENCE: immediate sync with no members
            map.StartSync();
            var leaveEvents = map.EndSync();

            // Assertions
            // All members get LEAVE events
            leaveEvents.Should().HaveCount(3);

            // Verify each leave preserves original attributes
            var aliceLeave = leaveEvents.FirstOrDefault(e => e.ClientId == "alice");
            var bobLeave = leaveEvents.FirstOrDefault(e => e.ClientId == "bob");
            var carolLeave = leaveEvents.FirstOrDefault(e => e.ClientId == "carol");

            // NOTE: S-4: the removed members are returned as stored; the LEAVE action and null id (RTP19) are set in Presence.EndSync, so the action and id lines below are kept as comments
            aliceLeave.Should().NotBeNull();

            // ASSERT alice_leave.action == LEAVE
            aliceLeave.Data.Should().Be("a");

            // ASSERT alice_leave.id IS null
            bobLeave.Should().NotBeNull();

            // ASSERT bob_leave.action == LEAVE
            bobLeave.Data.Should().Be("b");

            // ASSERT bob_leave.id IS null
            carolLeave.Should().NotBeNull();

            // ASSERT carol_leave.action == LEAVE
            carolLeave.Data.Should().Be("c");

            // ASSERT carol_leave.id IS null

            // Map is empty
            map.Values.Should().HaveCount(0);
        }

        // UTS: realtime/unit/RTP2h2a/leave-during-sync-absent-cleanup-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP2h2a")]
        [Trait("spec", "RTP2h2a")]
        public void RTP2h2a_LeaveDuringSyncAbsentCleanup()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();

            // Alice appears in sync
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // Bob sends LEAVE during sync — stored as ABSENT, not emitted yet
            // NOTE: S-2: null (nothing emitted) is false
            var leaveResult = map.Remove(Msg(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            // Verify bob is ABSENT but still in map
            leaveResult.Should().BeFalse();
            Get(map, "c2", "bob").Should().NotBeNull();
            Get(map, "c2", "bob").Action.Should().Be(PresenceAction.Absent);

            // End sync
            var leaveEvents = map.EndSync();

            // Assertions
            // Bob's ABSENT entry is cleaned up on endSync (RTP2h2b) — no synthesized
            // LEAVE event is emitted for bob because he was explicitly marked ABSENT
            // via a LEAVE message (not stale-by-absence-from-sync). ABSENT members
            // are simply deleted on endSync without generating LEAVE events.
            // Synthesized LEAVE events (RTP19) are only for PRESENT members that
            // were not updated during sync (residuals).
            leaveEvents.Should().HaveCount(0);
            Get(map, "c2", "bob").Should().BeNull();

            // Alice survives
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP19/empty-map-sync-no-leaves-3
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_EmptyMapSyncNoLeaves()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.StartSync();
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:0:0", 100));
            var leaveEvents = map.EndSync();

            // Assertions
            leaveEvents.Should().HaveCount(0);
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP18/endsync-without-startsync-noop-0
        [Fact]
        [Trait("spec", "RTP18")]
        public void RTP18_EndsyncWithoutStartsyncNoop()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));

            // endSync without startSync
            var leaveEvents = map.EndSync();

            // Assertions
            leaveEvents.Should().HaveCount(0);
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();
            map.SyncInProgress.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP19/stale-sync-removes-from-residuals-4
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_StaleSyncRemovesFromResiduals()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with a member via ENTER
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:5:0", 500, "original"));

            // Start sync
            map.StartSync();

            // SYNC message arrives with OLDER id (stale — same connectionId, lower msgSerial)
            var result = map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:3:0", 300, "stale"));

            var leaveEvents = map.EndSync();

            // Assertions
            // The stale put was rejected (returns null)
            result.Should().BeFalse();

            // But alice must NOT be evicted — she was "seen" during sync
            leaveEvents.Should().HaveCount(0);
            map.Values.Should().HaveCount(1);
            Get(map, "c1", "alice").Should().NotBeNull();

            // Original data is preserved (stale message did not overwrite)
            Get(map, "c1", "alice").Data.Should().Be("original");
        }

        // UTS: realtime/unit/RTP19/presence-echoes-then-sync-preserves-5
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_PresenceEchoesThenSyncPreserves()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Simulate server echoing PRESENCE events for 3 members
            map.Put(Msg(PresenceAction.Enter, "user-0", "c1", "c1:0:0", 100, "data-0"));
            map.Put(Msg(PresenceAction.Enter, "user-1", "c1", "c1:1:0", 100, "data-1"));
            map.Put(Msg(PresenceAction.Enter, "user-2", "c1", "c1:2:0", 100, "data-2"));

            map.Values.Should().HaveCount(3);

            // Server starts SYNC — members already exist from PRESENCE echoes
            map.StartSync();

            // SYNC messages arrive with the SAME ids as the PRESENCE echoes (stale)
            map.Put(Msg(PresenceAction.Present, "user-0", "c1", "c1:0:0", 100, "data-0"));
            map.Put(Msg(PresenceAction.Present, "user-1", "c1", "c1:1:0", 100, "data-1"));
            map.Put(Msg(PresenceAction.Present, "user-2", "c1", "c1:2:0", 100, "data-2"));

            var leaveEvents = map.EndSync();

            // Assertions
            // No members evicted — all were seen during sync despite stale ids
            leaveEvents.Should().HaveCount(0);
            map.Values.Should().HaveCount(3);

            for (var i = 0; i <= 2; i++)
            {
                var member = Get(map, "c1", $"user-{i}");
                member.Should().NotBeNull();
                member.Data.Should().Be($"data-{i}");
            }
        }

        // UTS: realtime/unit/RTP19/new-member-during-sync-survives-6
        [Fact]
        [Trait("spec", "RTP19")]
        public void RTP19_NewMemberDuringSyncSurvives()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Pre-populate with alice only
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));

            map.StartSync();

            // Alice appears in sync
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // Bob is NEW — entered via PRESENCE message during sync (not from SYNC data)
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 200));

            var leaveEvents = map.EndSync();

            // Assertions
            // No leave events — both alice and bob are current
            leaveEvents.Should().HaveCount(0);
            map.Values.Should().HaveCount(2);
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c2", "bob").Should().NotBeNull();
        }
    }
}
