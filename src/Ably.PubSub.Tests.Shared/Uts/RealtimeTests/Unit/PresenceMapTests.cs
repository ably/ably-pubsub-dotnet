// PresenceMap Tests (RTP2, RTP2a, RTP2b, RTP2b1, RTP2b1a, RTP2b2, RTP2c, RTP2d, RTP2d1, RTP2d2, RTP2h, RTP2h1, RTP2h1a, RTP2h1b, RTP2h2, RTP2h2a, RTP2h2b)
// Derived from uts/realtime/unit/presence/presence_map.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: white-box: drives the internal PresenceMap directly (realtime-mapping.md section 14); shape tags S-1 (put returns bool), S-2 (remove returns bool), S-3 (get is a lookup in the internal Members dictionary)

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
    public class PresenceMapTests : UtsUnitTestBase
    {
        public PresenceMapTests(ITestOutputHelper output)
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

        // UTS: realtime/unit/RTP2/basic-put-and-get-0
        [Fact]
        [Trait("spec", "RTP2")]
        public void RTP2_BasicPutAndGet()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            var msg = Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000);

            // NOTE: S-1: put returns true when the message is applied (the spec returns the message to emit)
            var result = map.Put(msg);

            // Assertions
            result.Should().BeTrue();
            Get(map, "conn-1", "client-1").Should().NotBeNull();
            Get(map, "conn-1", "client-1").ClientId.Should().Be("client-1");
            Get(map, "conn-1", "client-1").ConnectionId.Should().Be("conn-1");
        }

        // UTS: realtime/unit/RTP2d2/enter-stored-as-present-0
        [Fact]
        [Trait("spec", "RTP2d2")]
        public void RTP2d2_EnterStoredAsPresent()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            var enterMsg = Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered");
            map.Put(enterMsg);

            // Assertions
            var stored = Get(map, "conn-1", "client-1");
            stored.Should().NotBeNull();

            // RTP2d2: stored as PRESENT regardless of original action
            stored.Action.Should().Be(PresenceAction.Present);
            stored.Data.Should().Be("entered");
        }

        // UTS: realtime/unit/RTP2d2/update-stored-as-present-1
        [Fact]
        [Trait("spec", "RTP2d2")]
        public void RTP2d2_UpdateStoredAsPresent()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // First enter
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "initial"));

            // Then update
            map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated"));

            // Assertions
            var stored = Get(map, "conn-1", "client-1");
            stored.Action.Should().Be(PresenceAction.Present);
            stored.Data.Should().Be("updated");
        }

        // UTS: realtime/unit/RTP2d2/present-stored-as-present-2
        [Fact]
        [Trait("spec", "RTP2d2")]
        public void RTP2d2_PresentStoredAsPresent()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Present, "client-1", "conn-1", "conn-1:0:0", 1000));

            // Assertions
            var stored = Get(map, "conn-1", "client-1");
            stored.Should().NotBeNull();
            stored.Action.Should().Be(PresenceAction.Present);
        }

        // UTS: realtime/unit/RTP2d1/put-returns-original-action-0
        [Fact]
        [Trait("spec", "RTP2d1")]
        public void RTP2d1_PutReturnsOriginalAction()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // NOTE: S-1: put returns true when applied; the message to emit is the input message itself (the SDK publishes it with its original action in Presence.OnPresence)
            var enterMsg = Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000);
            var emittedEnter = map.Put(enterMsg);

            var updateMsg = Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated");
            var emittedUpdate = map.Put(updateMsg);

            // Assertions
            emittedEnter.Should().BeTrue();

            // Original action preserved for emission
            // NOTE: S-1: the original action is asserted on the input message; the map stores PRESENT copies and returns no message
            enterMsg.Action.Should().Be(PresenceAction.Enter);

            emittedUpdate.Should().BeTrue();

            // Original action preserved for emission
            updateMsg.Action.Should().Be(PresenceAction.Update);
        }

        // UTS: realtime/unit/RTP2h1/leave-outside-sync-removes-0
        [Fact]
        [Trait("spec", "RTP2h1")]
        public void RTP2h1_LeaveOutsideSyncRemoves()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add a member
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            // Remove the member
            // NOTE: S-2: remove returns true when the LEAVE is to be emitted; the LEAVE's fields are asserted from the input message
            var leaveMsg = Msg(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000);
            var emitted = map.Remove(leaveMsg);

            // Assertions
            // RTP2h1a: Emit LEAVE to subscribers
            emitted.Should().BeTrue();
            leaveMsg.Action.Should().Be(PresenceAction.Leave);

            // RTP2h1b: Delete from presence map
            Get(map, "conn-1", "client-1").Should().BeNull();
            map.Values.Should().HaveCount(0);
        }

        // UTS: realtime/unit/RTP2h1/leave-nonexistent-returns-null-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTP2h")]
        [Trait("spec", "RTP2h1")]
        public void RTP2h1_LeaveNonexistentReturnsNull()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // NOTE: S-2: null (nothing to emit) is false
            var emitted = map.Remove(Msg(PresenceAction.Leave, "unknown", "conn-x", "conn-x:0:0", 1000));

            // Assertions
            emitted.Should().BeFalse();
        }

        // UTS: realtime/unit/RTP2h2a/leave-during-sync-stores-absent-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP2h2a")]
        [Trait("spec", "RTP2h2a")]
        public void RTP2h2a_LeaveDuringSyncStoresAbsent()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add a member
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            // Start sync
            map.StartSync();

            // LEAVE during sync
            // NOTE: S-2: null (nothing emitted) is false
            var emitted = map.Remove(Msg(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000));

            // Assertions
            // No LEAVE emitted during sync
            emitted.Should().BeFalse();

            // Member is stored as ABSENT (not deleted)
            var stored = Get(map, "conn-1", "client-1");
            stored.Should().NotBeNull();
            stored.Action.Should().Be(PresenceAction.Absent);
        }

        // UTS: realtime/unit/RTP2h2b/absent-deleted-on-endsync-0
        [Fact]
        [Trait("spec", "RTP2h2b")]
        public void RTP2h2b_AbsentDeletedOnEndsync()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add two members
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            // Start sync
            map.StartSync();

            // Alice gets updated during sync (still present)
            map.Put(Msg(PresenceAction.Present, "alice", "c1", "c1:1:0", 200));

            // Bob sends LEAVE during sync (stored as ABSENT)
            map.Remove(Msg(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            // End sync
            // NOTE: S-4: the removed members are returned as stored (not synthesised LEAVEs)
            var leaveEvents = map.EndSync();

            // Assertions
            // Bob's ABSENT entry was deleted
            Get(map, "c2", "bob").Should().BeNull();

            // Alice remains
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c1", "alice").Action.Should().Be(PresenceAction.Present);

            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP2b2/newness-by-msgserial-index-0
        [Fact(Skip = "UTS spec error RTP2b2: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTP2b2")]
        public void RTP2b2_NewnessByMsgserialIndex()
        {
            // SPEC ERROR RTP2b2: the Assertions section asserts get("conn-1:client-1").data == "first" after the Test Steps have already put the
            // newer msgSerial=7 message ("newer"), then asserts "newer" for the same entry; the spec is internally inconsistent (features RTP2b2
            // makes the msgSerial=7 message newer). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTP2b2 - fix the spec first; see deviations.md");
        }

        // UTS: realtime/unit/RTP2b2/newness-by-index-same-serial-1
        [Fact]
        [Trait("spec", "RTP2b2")]
        public void RTP2b2_NewnessByIndexSameSerial()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:5:2", 1000, "index-2"));

            // Same msgSerial, lower index — stale
            var stale = map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:5:1", 2000, "index-1"));

            // Same msgSerial, higher index — newer
            var newer = map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:5:5", 500, "index-5"));

            // Assertions
            stale.Should().BeFalse();
            newer.Should().BeTrue();
            Get(map, "conn-1", "client-1").Data.Should().Be("index-5");
        }

        // UTS: realtime/unit/RTP2b1/newness-by-timestamp-0
        [Fact]
        [Trait("spec", "RTP2b1")]
        public void RTP2b1_NewnessByTimestamp()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add member with normal id (connectionId is prefix of id)
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered"));

            // Synthesized leave: id does NOT start with connectionId
            // (server-generated, uses a different id format)
            var synthLeaveMsg = Msg(PresenceAction.Leave, "client-1", "conn-1", "synthesized-leave-id", 2000);
            var synthLeave = map.Remove(synthLeaveMsg);

            // Assertions
            // Timestamp 2000 > 1000, so the synthesized leave is newer
            synthLeave.Should().BeTrue();
            synthLeaveMsg.Action.Should().Be(PresenceAction.Leave);
            Get(map, "conn-1", "client-1").Should().BeNull();
        }

        // UTS: realtime/unit/RTP2b1/older-synth-leave-rejected-1
        [Fact]
        [Trait("spec", "RTP2b1")]
        public void RTP2b1_OlderSynthLeaveRejected()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 5000, "entered"));

            // Synthesized leave with older timestamp
            var result = map.Remove(Msg(PresenceAction.Leave, "client-1", "conn-1", "synthesized-leave-id", 3000));

            // Assertions
            // Rejected — existing message (timestamp 5000) is newer
            result.Should().BeFalse();
            Get(map, "conn-1", "client-1").Should().NotBeNull();
            Get(map, "conn-1", "client-1").Data.Should().Be("entered");
        }

        // UTS: realtime/unit/RTP2b1a/equal-timestamps-incoming-wins-0
        [Fact]
        [Trait("spec", "RTP2b1a")]
        public void RTP2b1a_EqualTimestampsIncomingWins()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "synthesized-id-1", 1000, "first"));

            // Same timestamp, incoming wins
            var result = map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "synthesized-id-2", 1000, "second"));

            // Assertions
            result.Should().BeTrue();
            Get(map, "conn-1", "client-1").Data.Should().Be("second");
        }

        // UTS: realtime/unit/RTP2c/sync-uses-same-newness-0
        [Fact]
        [Trait("spec", "RTP2c")]
        public void RTP2c_SyncUsesSameNewness()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.StartSync();

            // First SYNC message
            map.Put(Msg(PresenceAction.Present, "client-1", "conn-1", "conn-1:5:0", 1000, "sync-first"));

            // Second SYNC message with older serial — rejected
            var stale = map.Put(Msg(PresenceAction.Present, "client-1", "conn-1", "conn-1:3:0", 2000, "sync-stale"));

            // Third SYNC message with newer serial — accepted
            var newer = map.Put(Msg(PresenceAction.Present, "client-1", "conn-1", "conn-1:8:0", 500, "sync-newer"));

            // Assertions
            stale.Should().BeFalse();
            newer.Should().BeTrue();
            Get(map, "conn-1", "client-1").Data.Should().Be("sync-newer");
        }

        // UTS: realtime/unit/RTP2/multiple-members-coexist-1
        [Fact]
        [Trait("spec", "RTP2")]
        public void RTP2_MultipleMembersCoexist()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "alice", "c3", "c3:0:0", 100));

            // Assertions
            // Three distinct members (alice on c1, bob on c2, alice on c3)
            map.Values.Should().HaveCount(3);
            Get(map, "c1", "alice").Should().NotBeNull();
            Get(map, "c2", "bob").Should().NotBeNull();
            Get(map, "c3", "alice").Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP2/values-excludes-absent-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTP2h2a")]
        [Trait("spec", "RTP2")]
        public void RTP2_ValuesExcludesAbsent()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "c2", "c2:0:0", 100));

            // Start sync and mark bob as ABSENT
            map.StartSync();
            map.Remove(Msg(PresenceAction.Leave, "bob", "c2", "c2:1:0", 200));

            // Assertions
            // Bob is stored as ABSENT but excluded from values()
            Get(map, "c2", "bob").Should().NotBeNull();
            Get(map, "c2", "bob").Action.Should().Be(PresenceAction.Absent);

            var members = map.Values;
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");
        }

        // UTS: realtime/unit/RTP2/clear-resets-state-3
        [Fact]
        [Trait("spec", "RTP2")]
        public void RTP2_ClearResetsState()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "c1", "c1:0:0", 100));
            map.StartSync();

            map.Clear();

            // Assertions
            map.Values.Should().HaveCount(0);
            Get(map, "c1", "alice").Should().BeNull();

            // DEVIATION: spec expects clear() to end an in-progress sync (isSyncInProgress == false); the SDK's PresenceMap.Clear
            // keeps SyncInProgress set, and the sync is ended by EndSync - see deviations.md
            map.SyncInProgress.Should().BeTrue();
        }
    }
}
