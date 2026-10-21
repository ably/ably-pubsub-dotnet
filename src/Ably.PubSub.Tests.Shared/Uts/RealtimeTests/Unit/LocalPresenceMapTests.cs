// LocalPresenceMap Tests (RTP17, RTP17b, RTP17h)
// Derived from uts/realtime/unit/presence/local_presence_map.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: white-box: drives the internal InternalPresenceMap (the spec's LocalPresenceMap) directly (realtime-mapping.md section 14); shape tags S-1 (put returns bool), S-3 (get is a lookup in the internal Members dictionary, keyed by clientId); the synthesized-LEAVE filter is applied at the calling level, as the spec's implementation note allows

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
    public class LocalPresenceMapTests : UtsUnitTestBase
    {
        public LocalPresenceMapTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The specs' LocalPresenceMap(): the internal PresenceMap keyed by clientId (RTP17h)
        private static InternalPresenceMap NewMap()
        {
            return new InternalPresenceMap("test", InternalLogger.Create(LogLevel.Debug, new CapturingLogSink()));
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

        // S-3: the specs' map.get(clientId), looked up in the internal Members dictionary
        private static PresenceMessage Get(InternalPresenceMap map, string clientId)
        {
            return map.Members.TryGetValue(clientId, out var member) ? member : null;
        }

        // The specs' map.remove(message) -> bool: true if removed, false if a synthesized leave (ignored).
        // NOTE: the synthesized-LEAVE filter is enforced at the calling level (Presence.OnPresence uses IsServerSynthesized()), which the spec's implementation note allows
        private static bool Remove(InternalPresenceMap map, PresenceMessage message)
        {
            return !message.IsServerSynthesized() && map.Remove(message);
        }

        // UTS: realtime/unit/RTP17h/keyed-by-clientid-0
        [Fact]
        [Trait("spec", "RTP17h")]
        public void RTP17h_KeyedByClientid()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            var msg1 = Msg(PresenceAction.Enter, "user-1", "conn-A", "conn-A:0:0", 1000, "first");
            var msg2 = Msg(PresenceAction.Enter, "user-1", "conn-B", "conn-B:0:0", 2000, "second");

            map.Put(msg1);
            map.Put(msg2);

            // Assertions
            // Only one entry — keyed by clientId, second put overwrites the first
            map.Values.Should().HaveCount(1);
            Get(map, "user-1").Should().NotBeNull();
            Get(map, "user-1").Data.Should().Be("second");
            Get(map, "user-1").ConnectionId.Should().Be("conn-B");
        }

        // UTS: realtime/unit/RTP17b/enter-adds-to-map-0
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_EnterAddsToMap()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "hello"));

            // Assertions
            Get(map, "client-1").Should().NotBeNull();

            // RTP2d2: stored action is always PRESENT
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("hello");
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17b/update-adds-to-map-1
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_UpdateAddsToMap()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:0:0", 1000, "from-update"));

            // Assertions
            Get(map, "client-1").Should().NotBeNull();

            // RTP2d2: stored action is always PRESENT
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("from-update");
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17b/enter-overwrites-enter-2
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_EnterOverwritesEnter()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "first"));

            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:1:0", 2000, "second"));

            // Assertions
            map.Values.Should().HaveCount(1);

            // RTP2d2: stored action is always PRESENT
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("second");
        }

        // UTS: realtime/unit/RTP17b/update-overwrites-enter-3
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_UpdateOverwritesEnter()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "initial"));

            map.Put(Msg(PresenceAction.Update, "client-1", "conn-1", "conn-1:1:0", 2000, "updated"));

            // Assertions
            map.Values.Should().HaveCount(1);

            // RTP2d2: stored action is always PRESENT
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("updated");
        }

        // UTS: realtime/unit/RTP17b/present-adds-to-map-4
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_PresentAddsToMap()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Present, "client-1", "conn-1", "conn-1:0:0", 1000, "present"));

            // Assertions
            Get(map, "client-1").Should().NotBeNull();
            Get(map, "client-1").Action.Should().Be(PresenceAction.Present);
            Get(map, "client-1").Data.Should().Be("present");
        }

        // UTS: realtime/unit/RTP17b/non-synthesized-leave-removes-5
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_NonSynthesizedLeaveRemoves()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add member
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000));

            Get(map, "client-1").Should().NotBeNull();

            // Non-synthesized LEAVE: connectionId "conn-1" IS an initial substring of id "conn-1:1:0"
            var result = Remove(map, Msg(PresenceAction.Leave, "client-1", "conn-1", "conn-1:1:0", 2000));

            // Assertions
            result.Should().BeTrue();
            Get(map, "client-1").Should().BeNull();
            map.Values.Should().HaveCount(0);
        }

        // UTS: realtime/unit/RTP17b/synthesized-leave-ignored-6
        [Fact]
        [Trait("spec", "RTP17b")]
        public void RTP17b_SynthesizedLeaveIgnored()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            // Add member
            map.Put(Msg(PresenceAction.Enter, "client-1", "conn-1", "conn-1:0:0", 1000, "entered"));

            // Synthesized LEAVE: connectionId "conn-1" is NOT an initial substring of id "synthesized-leave-id"
            var result = Remove(map, Msg(PresenceAction.Leave, "client-1", "conn-1", "synthesized-leave-id", 2000));

            // Assertions
            // remove returns false — synthesized leave was ignored
            result.Should().BeFalse();

            // Member is still present
            Get(map, "client-1").Should().NotBeNull();
            Get(map, "client-1").Data.Should().Be("entered");
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17/multiple-clientids-coexist-0
        [Fact]
        [Trait("spec", "RTP17")]
        public void RTP17_MultipleClientidsCoexist()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100, "alice-data"));
            map.Put(Msg(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100, "bob-data"));
            map.Put(Msg(PresenceAction.Enter, "carol", "conn-1", "conn-1:0:2", 100, "carol-data"));

            // Assertions
            map.Values.Should().HaveCount(3);
            Get(map, "alice").Should().NotBeNull();
            Get(map, "bob").Should().NotBeNull();
            Get(map, "carol").Should().NotBeNull();
            Get(map, "alice").Data.Should().Be("alice-data");
            Get(map, "bob").Data.Should().Be("bob-data");
            Get(map, "carol").Data.Should().Be("carol-data");
        }

        // UTS: realtime/unit/RTP17/remove-one-of-multiple-1
        [Fact]
        [Trait("spec", "RTP17")]
        public void RTP17_RemoveOneOfMultiple()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100));

            Remove(map, Msg(PresenceAction.Leave, "alice", "conn-1", "conn-1:1:0", 200));

            // Assertions
            Get(map, "alice").Should().BeNull();
            Get(map, "bob").Should().NotBeNull();
            map.Values.Should().HaveCount(1);
        }

        // UTS: realtime/unit/RTP17/clear-resets-state-2
        [Fact]
        [Trait("spec", "RTP17")]
        public void RTP17_ClearResetsState()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));
            map.Put(Msg(PresenceAction.Enter, "bob", "conn-1", "conn-1:0:1", 100));

            map.Values.Should().HaveCount(2);

            map.Clear();

            // Assertions
            map.Values.Should().HaveCount(0);
            Get(map, "alice").Should().BeNull();
            Get(map, "bob").Should().BeNull();
        }

        // UTS: realtime/unit/RTP17/get-null-unknown-clientid-3
        [Fact]
        [Trait("spec", "RTP17")]
        public void RTP17_GetNullUnknownClientid()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            var result = Get(map, "nonexistent");

            // Assertions
            result.Should().BeNull();
        }

        // UTS: realtime/unit/RTP17/remove-unknown-noop-4
        [Fact]
        [Trait("spec", "RTP17")]
        public void RTP17_RemoveUnknownNoop()
        {
            // Setup
            var map = NewMap();

            // Test Steps
            map.Put(Msg(PresenceAction.Enter, "alice", "conn-1", "conn-1:0:0", 100));

            // Remove a clientId that was never added (non-synthesized leave)
            Remove(map, Msg(PresenceAction.Leave, "nonexistent", "conn-1", "conn-1:1:0", 200));

            // Assertions
            // Original member is unaffected
            Get(map, "alice").Should().NotBeNull();
            map.Values.Should().HaveCount(1);
        }
    }
}
