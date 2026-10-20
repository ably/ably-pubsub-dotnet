// Realtime Presence Lifecycle Integration Tests (RTP4, RTP6, RTP8, RTP9, RTP10, RTP11a)
// Derived from uts/realtime/integration/presence_lifecycle_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class PresenceLifecycleTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public PresenceLifecycleTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTP4/bulk-enter-observed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP4")]
        public async Task RTP4_BulkEnterObserved()
        {
            // Setup
            var channelName = "presence-bulk-" + RandomId();
            var memberCount = 50;

            var clientA = Realtime();

            var clientB = Realtime();

            // Test Steps
            // Connect both clients
            clientA.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

            clientB.Connect();
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            // Attach both to the channel
            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);
            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // Subscribe on client B before client A enters. Members are counted by clientId from
            // both ENTER and PRESENT events: if client B's connection drops mid-test, members it
            // missed arrive via the presence re-sync as PRESENT events rather than ENTER, and an
            // ENTER-only count would undercount forever.
            var enteredClientIds = new ConcurrentDictionary<string, byte>();
            Action<PresenceMessage> recordClientId = presenceEvent =>
            {
                enteredClientIds[presenceEvent.ClientId] = 0;
            };

            // NOTE: Presence.Subscribe takes one action per call; the spec's action: [ENTER, PRESENT] is two subscriptions sharing one handler
            channelB.Presence.Subscribe(PresenceAction.Enter, recordClientId);
            channelB.Presence.Subscribe(PresenceAction.Present, recordClientId);

            // Attach client A (after B is attached and subscribed)
            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Client A enters members in parallel
            var futures = new List<Task<Result>>();
            for (var i = 0; i <= memberCount - 1; i++)
            {
                futures.Add(channelA.Presence.EnterClientAsync($"user-{i}", $"data-{i}"));
            }

            var enterResults = await Task.WhenAll(futures);
            foreach (var enterResult in enterResults)
            {
                AssertSucceeded(enterResult, "channel_a.presence.enterClient()");
            }

            // Wait for client B to observe all members entering
            await UtsWait.PollUntil(
                () => enteredClientIds.Count >= memberCount,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B observed all members entering");

            // Client B gets all members
            var members = (await channelB.Presence.GetAsync()).ToList();

            // Assertions
            // Client B observed every member entering via subscribe
            enteredClientIds.Count.Should().Be(memberCount);

            // All members present via get()
            members.Should().HaveCount(memberCount);

            // Verify each member has correct clientId and data
            for (var i = 0; i <= memberCount - 1; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == $"user-{i}");
                member.Should().NotBeNull();
                member.Data.Should().Be($"data-{i}");
            }

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTP8/enter-update-leave-lifecycle-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP8")]
        public async Task RTP8_EnterUpdateLeaveLifecycle()
        {
            // Setup
            var channelName = "presence-lifecycle-" + RandomId();

            var clientA = Realtime(new ClientOptions { ClientId = "lifecycle-client" });

            var clientB = Realtime();

            // Test Steps
            // Connect and attach both clients
            clientA.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);
            clientB.Connect();
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);
            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // Collect all presence events on client B
            var allEvents = new CaptureList<PresenceMessage>();
            channelB.Presence.Subscribe(presenceEvent =>
            {
                allEvents.Add(presenceEvent);
            });

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // --- Phase 1: Enter ---
            AssertSucceeded(await channelA.Presence.EnterAsync("hello"), "channel_a.presence.enter()");

            // Wait for ENTER event on client B
            // NOTE: polls for the ENTER action, not an event count: a presence re-sync can legitimately emit PRESENT for a member
            // already in the map (RTP2a/RTP2b2/RTP2d1) - see deviations.md (UTS Spec Errors, RTP8)
            // poll_until(condition: all_events.length >= 1)
            await UtsWait.PollUntil(
                () => allEvents.Snapshot().Any(e => e.Action == PresenceAction.Enter),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B observed the ENTER event");

            // Verify member is present via get()
            var membersAfterEnter = (await channelB.Presence.GetAsync()).ToList();
            membersAfterEnter.Should().HaveCount(1);
            membersAfterEnter[0].ClientId.Should().Be("lifecycle-client");
            membersAfterEnter[0].Data.Should().Be("hello");

            // --- Phase 2: Update ---
            AssertSucceeded(await channelA.Presence.UpdateAsync("world"), "channel_a.presence.update()");

            // Wait for UPDATE event on client B
            // NOTE: polls for the UPDATE action, not an event count: a presence re-sync can legitimately emit PRESENT for a member
            // already in the map (RTP2a/RTP2b2/RTP2d1) - see deviations.md (UTS Spec Errors, RTP8)
            // poll_until(condition: all_events.length >= 2)
            await UtsWait.PollUntil(
                () => allEvents.Snapshot().Any(e => e.Action == PresenceAction.Update),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B observed the UPDATE event");

            // Verify member data updated via get()
            var membersAfterUpdate = (await channelB.Presence.GetAsync()).ToList();
            membersAfterUpdate.Should().HaveCount(1);
            membersAfterUpdate[0].Data.Should().Be("world");

            // --- Phase 3: Leave ---
            AssertSucceeded(await channelA.Presence.LeaveAsync("goodbye"), "channel_a.presence.leave()");

            // Wait for LEAVE event on client B
            // NOTE: polls for the LEAVE action, not an event count: a presence re-sync can legitimately emit PRESENT for a member
            // already in the map (RTP2a/RTP2b2/RTP2d1) - see deviations.md (UTS Spec Errors, RTP8)
            // poll_until(condition: all_events.length >= 3)
            await UtsWait.PollUntil(
                () => allEvents.Snapshot().Any(e => e.Action == PresenceAction.Leave),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B observed the LEAVE event");

            // Verify member is gone via get()
            var membersAfterLeave = (await channelB.Presence.GetAsync()).ToList();
            membersAfterLeave.Should().HaveCount(0);

            // Assertions
            // Verify the sequence of events
            // NOTE: PRESENT events are excluded before indexing: a presence re-sync can emit PRESENT for the member between
            // the live events (RTP2a/RTP2b2/RTP2d1), so all_events[1] is not necessarily the UPDATE - see deviations.md
            // (UTS Spec Errors, RTP8)
            var observedEvents = allEvents.Snapshot().Where(e => e.Action != PresenceAction.Present).ToList();
            observedEvents.Count.Should().BeGreaterThanOrEqualTo(3);

            var enterEvent = observedEvents[0];
            enterEvent.Action.Should().Be(PresenceAction.Enter);
            enterEvent.ClientId.Should().Be("lifecycle-client");
            enterEvent.Data.Should().Be("hello");

            var updateEvent = observedEvents[1];
            updateEvent.Action.Should().Be(PresenceAction.Update);
            updateEvent.ClientId.Should().Be("lifecycle-client");
            updateEvent.Data.Should().Be("world");

            var leaveEvent = observedEvents[2];
            leaveEvent.Action.Should().Be(PresenceAction.Leave);
            leaveEvent.ClientId.Should().Be("lifecycle-client");
            leaveEvent.Data.Should().Be("goodbye");

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }
    }
}
