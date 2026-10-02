using System;
using System.Linq;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Presence
{
    /// <summary>
    /// Derived from uts/realtime/unit/presence/presence_map.md in ably/specification.
    ///
    /// Spec points: RTP2, RTP2b1, RTP2b1a, RTP2b2, RTP2c, RTP2d1, RTP2d2, RTP2h1, RTP2h2a, RTP2h2b
    ///
    /// <para>
    /// The only file in the suite whose subject is an internal class: the spec names
    /// <c>PresenceMap</c> as the interface under test, and it is <c>internal</c> here, which the test
    /// assembly can reach. No client, no mocks.
    /// </para>
    ///
    /// <para>
    /// Three shape differences, none of them behavioural:
    /// <c>put</c> and <c>remove</c> return <c>bool</c> rather than "the message to emit, or null", so
    /// the spec's "IS NOT null" reads as <c>true</c> and its "IS null" as <c>false</c> - the same
    /// decision, reported differently. There is no <c>get(memberKey)</c>; the map exposes its
    /// <c>Members</c> dictionary internally, with a comment saying it is there for exactly this.
    /// And <c>values()</c> is the <c>Values</c> property.
    /// </para>
    ///
    /// <para>
    /// RTP2d1's "the emitted message keeps its original action" is asserted on the message the
    /// caller passed in, which is the same observable: <c>Put</c> shallow-clones before rewriting
    /// the action to PRESENT (<c>PresenceMap.cs:104-110</c>), so the caller's copy - the one that
    /// goes to subscribers - is untouched.
    /// </para>
    /// </summary>
    public class PresenceMapTests : UtsTestBase
    {
        public PresenceMapTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP2/basic-put-and-get-0
        [Fact]
        public void RTP2_BasicPutAndGet()
        {
            var map = NewMap();

            var result = map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            result.Should().BeTrue();

            var stored = Get(map, "client-1", "conn-1");
            stored.Should().NotBeNull();
            stored.ClientId.Should().Be("client-1");
            stored.ConnectionId.Should().Be("conn-1");
        }

        // UTS: realtime/unit/RTP2d2/enter-stored-as-present-0
        [Fact]
        public void RTP2d2_EnterStoredAsPresent()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered"));

            var stored = Get(map, "client-1", "conn-1");
            stored.Should().NotBeNull();
            stored.Action.Should().Be(PresenceAction.Present, "RTP2d2");
            stored.Data.Should().Be("entered");
        }

        // UTS: realtime/unit/RTP2d2/update-stored-as-present-1
        [Fact]
        public void RTP2d2_UpdateStoredAsPresent()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "initial"));
            map.Put(Member(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated"));

            var stored = Get(map, "client-1", "conn-1");
            stored.Action.Should().Be(PresenceAction.Present);
            stored.Data.Should().Be("updated");
        }

        // UTS: realtime/unit/RTP2d2/present-stored-as-present-2
        [Fact]
        public void RTP2d2_PresentStoredAsPresent()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Present, "client-1", "conn-1", "conn-1:0:0", 1000));

            var stored = Get(map, "client-1", "conn-1");
            stored.Should().NotBeNull();
            stored.Action.Should().Be(PresenceAction.Present);
        }

        // UTS: realtime/unit/RTP2d1/put-returns-original-action-0
        [Fact]
        public void RTP2d1_PutPreservesOriginalAction()
        {
            var map = NewMap();

            var enter = Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000);
            var emittedEnter = map.Put(enter);

            var update = Member(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated");
            var emittedUpdate = map.Put(update);

            emittedEnter.Should().BeTrue();
            enter.Action.Should().Be(
                PresenceAction.Enter,
                "RTP2d1 - the message that goes to subscribers keeps its own action");

            emittedUpdate.Should().BeTrue();
            update.Action.Should().Be(PresenceAction.Update);

            Get(map, "client-1", "conn-1").Action.Should().Be(
                PresenceAction.Present,
                "while the stored copy is PRESENT");
        }

        // UTS: realtime/unit/RTP2h1/leave-outside-sync-removes-0
        [Fact]
        public void RTP2h1_LeaveOutsideSyncRemoves()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            var emitted = map.Remove(
                Member(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000));

            emitted.Should().BeTrue("RTP2h1a - the LEAVE is emitted");
            Get(map, "client-1", "conn-1").Should().BeNull("RTP2h1b - and the member is deleted");
            map.Values.Should().BeEmpty();
        }

        // UTS: realtime/unit/RTP2h1/leave-nonexistent-returns-null-1
        //
        // DEVIATION. RTP2h opens with "if and only if there is a member with a matching memberKey
        // currently in the presence map", so a LEAVE for someone who is not there must do nothing
        // and emit nothing. PresenceMap.Remove (PresenceMap.cs:118) returns true in that case: with
        // no existing entry the newness check is skipped, TryRemove quietly does nothing, and the
        // `existingItem?.Action == Absent` guard is false, so the method reports a removal that did
        // not happen. See Uts/deviations.md.
        [DeviationFact]
        public void RTP2h1_LeaveNonexistentReturnsNull()
        {
            var map = NewMap();

            var emitted = map.Remove(
                Member(PresenceAction.Leave, "client-1", "conn-1", "conn-1:0:0", 1000));

            emitted.Should().BeFalse("there was nothing to remove");
        }

        // UTS: realtime/unit/RTP2h2a/leave-during-sync-stores-absent-0
        //
        // DEVIATION. RTP2h2a requires a LEAVE arriving while a SYNC is in progress to be "stored in
        // the presence map with the action set to ABSENT" rather than deleted, so that a later SYNC
        // message cannot resurrect the member. PresenceMap.Remove (PresenceMap.cs:118) has no sync
        // check at all: it deletes the entry outright. See Uts/deviations.md.
        [DeviationFact]
        public void RTP2h2a_LeaveDuringSyncStoresAbsent()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            map.StartSync();

            var emitted = map.Remove(
                Member(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000));

            emitted.Should().BeFalse("RTP2h2 - no LEAVE is emitted during a sync");

            var stored = Get(map, "client-1", "conn-1");
            stored.Should().NotBeNull("RTP2h2a - stored, not deleted");
            stored.Action.Should().Be(PresenceAction.Absent);
        }

        // UTS: realtime/unit/RTP2h2b/absent-deleted-on-endsync-0
        [Fact]
        public void RTP2h2b_AbsentDeletedOnEndSync()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();

            map.Put(Member(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));
            map.Remove(Member(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            map.EndSync();

            Get(map, "bob", "c2").Should().BeNull("bob is gone by the end of the sync");
            Get(map, "alice", "c1").Should().NotBeNull();
            Get(map, "alice", "c1").Action.Should().Be(PresenceAction.Present);
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP2b2/newness-by-msgserial-index-0
        [Fact]
        public void RTP2b2_NewnessByMsgSerialIndex()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:5:0", 1000, "first"));

            var stale = map.Put(
                Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:3:0", 3000, "stale"));

            var newer = map.Put(
                Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:7:0", 500, "newer"));

            stale.Should().BeFalse("RTP2a - an older msgSerial is stale whatever its timestamp says");
            newer.Should().BeTrue("a newer msgSerial wins even with an older timestamp");
            Get(map, "client-1", "conn-1").Data.Should().Be("newer");
        }

        // UTS: realtime/unit/RTP2b2/newness-by-index-same-serial-1
        [Fact]
        public void RTP2b2_NewnessByIndexWithSameSerial()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:5:3", 1000, "index-3"));

            var stale = map.Put(
                Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:5:1", 1000, "index-1"));

            var newer = map.Put(
                Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:5:5", 1000, "index-5"));

            stale.Should().BeFalse();
            newer.Should().BeTrue();
            Get(map, "client-1", "conn-1").Data.Should().Be("index-5");
        }

        // UTS: realtime/unit/RTP2b1/newness-by-timestamp-0
        [Fact]
        public void RTP2b1_NewnessByTimestampForSynthesizedLeave()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered"));

            // The id does not start with the connectionId, which is what makes it synthesized and
            // sends the comparison to the timestamp.
            var synthLeave = map.Remove(
                Member(PresenceAction.Leave, "client-1", "conn-1", "synthesized-leave-id", 2000));

            synthLeave.Should().BeTrue("2000 is later than 1000");
            Get(map, "client-1", "conn-1").Should().BeNull();
        }

        // UTS: realtime/unit/RTP2b1/older-synth-leave-rejected-1
        [Fact]
        public void RTP2b1_OlderSynthesizedLeaveRejected()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 5000, "entered"));

            var result = map.Remove(
                Member(PresenceAction.Leave, "client-1", "conn-1", "synthesized-leave-id", 3000));

            result.Should().BeFalse("the member on the map is newer");
            Get(map, "client-1", "conn-1").Should().NotBeNull();
            Get(map, "client-1", "conn-1").Data.Should().Be("entered");
        }

        // UTS: realtime/unit/RTP2b1a/equal-timestamps-incoming-wins-0
        [Fact]
        public void RTP2b1a_EqualTimestampsIncomingWins()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "client-1", "conn-1", "synthesized-id-1", 1000, "first"));

            var result = map.Put(
                Member(PresenceAction.Update, "client-1", "conn-1", "synthesized-id-2", 1000, "second"));

            result.Should().BeTrue("RTP2b1a - a tie goes to the incoming message");
            Get(map, "client-1", "conn-1").Data.Should().Be("second");
        }

        // UTS: realtime/unit/RTP2c/sync-uses-same-newness-0
        [Fact]
        public void RTP2c_SyncUsesSameNewnessComparison()
        {
            var map = NewMap();

            map.StartSync();

            map.Put(Member(PresenceAction.Present, "client-1", "conn-1", "conn-1:5:0", 1000, "sync-first"));

            var stale = map.Put(
                Member(PresenceAction.Present, "client-1", "conn-1", "conn-1:3:0", 2000, "sync-stale"));

            var newer = map.Put(
                Member(PresenceAction.Present, "client-1", "conn-1", "conn-1:8:0", 500, "sync-newer"));

            stale.Should().BeFalse();
            newer.Should().BeTrue();
            Get(map, "client-1", "conn-1").Data.Should().Be("sync-newer");
        }

        // UTS: realtime/unit/RTP2/multiple-members-coexist-1
        [Fact]
        public void RTP2_MultipleMembersCoexist()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "alice", "c3", "c3:0:0", 100));

            map.Values.Should().HaveCount(3, "the memberKey pairs connectionId with clientId");
            Get(map, "alice", "c1").Should().NotBeNull();
            Get(map, "bob", "c2").Should().NotBeNull();
            Get(map, "alice", "c3").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP2/values-excludes-absent-2
        //
        // DEVIATION, the same cause as RTP2h2a: nothing in this SDK ever stores a member as ABSENT,
        // so the state this test is about cannot be reached. `Values` does filter ABSENT out
        // (PresenceMap.cs:77), and EndSync does delete ABSENT members (PresenceMap.cs:174) - both
        // correct, and both unreachable. See Uts/deviations.md.
        [DeviationFact]
        public void RTP2_ValuesExcludesAbsent()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Member(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            map.StartSync();
            map.Remove(Member(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            var stored = Get(map, "bob", "c2");
            stored.Should().NotBeNull("RTP2h2a - bob is marked ABSENT rather than deleted");
            stored.Action.Should().Be(PresenceAction.Absent);

            var members = map.Values;
            members.Should().HaveCount(1, "values() excludes ABSENT members");
            members[0].ClientId.Should().Be("alice");
        }

        // UTS: realtime/unit/RTP2/clear-resets-state-3
        //
        // DEVIATION, and a narrow one: the members are cleared correctly, the sync flag is not.
        // PresenceMap.Clear (PresenceMap.cs:214) empties `_members` and `_beforeSyncMembers` and
        // leaves `SyncInProgress` as it found it, so a map cleared mid-sync still believes a sync is
        // running. Recorded with the other two PresenceMap findings in Uts/deviations.md.
        [DeviationFact]
        public void RTP2_ClearResetsState()
        {
            var map = NewMap();

            map.Put(Member(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.StartSync();

            map.Clear();

            map.Values.Should().BeEmpty();
            Get(map, "alice", "c1").Should().BeNull();
            map.SyncInProgress.Should().BeFalse();
        }

        private static PresenceMap NewMap() => new PresenceMap("test-channel", InternalLogger.Create());

        /// <summary>
        /// The spec's <c>map.get(memberKey)</c>. The spec writes its keys as
        /// <c>connectionId:clientId</c> and this SDK builds them the other way round -
        /// <c>PresenceMessage.MemberKey</c> is <c>$"{ClientId}:{ConnectionId}"</c> - which is already
        /// recorded as D6 in Uts/deviations.md and covered by its own test. Looking members up
        /// through the map's own <c>GetKey</c> keeps these tests about the map's behaviour instead of
        /// re-failing on the key order.
        /// </summary>
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
