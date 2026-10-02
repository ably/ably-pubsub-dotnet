using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration
{
    /// <summary>
    /// Derived from uts/realtime/integration/presence_lifecycle_test.md in ably/specification.
    ///
    /// Spec points: RTP4, RTP6, RTP8, RTP9, RTP10, RTP11a
    ///
    /// Four translation notes that apply to the whole file:
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// The spec's <c>AWAIT channel.attach()</c> and <c>AWAIT presence.enter()</c> reject on failure;
    /// the .NET equivalents return a <see cref="Result"/> rather than throwing, so each one asserts
    /// <c>IsSuccess</c> on the returned result.
    ///
    /// Every channel name and every client id carries a <see cref="UtsSandbox.RandomId"/> suffix,
    /// because the sandbox app is shared across the whole run. That makes the spec's literal
    /// <c>"lifecycle-client"</c> and <c>"user-${i}"</c> ids per-run values held in locals; the
    /// assertions compare against the same locals, so the structure of the check is unchanged.
    ///
    /// Each client is closed in a <c>finally</c> so its presence members leave the shared app even
    /// when an assertion fails part way through.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PresenceLifecycleTests : UtsRealtimeIntegrationTestBase
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        public PresenceLifecycleTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTP4/bulk-enter-observed-0
        [Fact]
        public async Task RTP4_BulkEnterObserved()
        {
            const int memberCount = 50;
            var channelName = "presence-bulk-" + UtsSandbox.RandomId();

            // The spec's "user-${i}"; see the class summary for why it carries a suffix.
            var memberPrefix = "user-" + UtsSandbox.RandomId() + "-";

            var clientA = await SandboxRealtimeClient();
            var clientB = await SandboxRealtimeClient();

            try
            {
                clientA.Connect();
                await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

                clientB.Connect();
                await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                var channelB = clientB.Channels.Get(channelName);

                var attachB = await channelB.AttachAsync();
                attachB.IsSuccess.Should().BeTrue("channel B must attach: {0}", attachB.Error);

                // Subscribe on client B before client A enters. Members are counted by clientId from
                // both ENTER and PRESENT events: if client B's connection drops mid-test, members it
                // missed arrive via the presence re-sync as PRESENT events rather than ENTER, and an
                // ENTER-only count would undercount forever. .NET's Presence.Subscribe takes one
                // action per call, so the spec's single action list becomes two registrations of the
                // same handler.
                var enteredClientIds = new ConcurrentDictionary<string, bool>();
                Action<PresenceMessage> onMember = presenceMessage =>
                    enteredClientIds.TryAdd(presenceMessage.ClientId, true);

                channelB.Presence.Subscribe(PresenceAction.Enter, onMember);
                channelB.Presence.Subscribe(PresenceAction.Present, onMember);

                // Attach client A (after B is attached and subscribed).
                var attachA = await channelA.AttachAsync();
                attachA.IsSuccess.Should().BeTrue("channel A must attach: {0}", attachA.Error);

                // Client A enters members in parallel — the spec's futures plus AWAIT_ALL.
                var enters = new List<Task<Result>>();
                for (var i = 0; i < memberCount; i++)
                {
                    enters.Add(channelA.Presence.EnterClientAsync(memberPrefix + i, "data-" + i));
                }

                var enterResults = await Task.WhenAll(enters);
                enterResults.Should().OnlyContain(result => result.IsSuccess);

                // Wait for client B to observe all members entering.
                await UtsSandbox.WallClockPollUntil(
                    () => Task.FromResult(enteredClientIds.Count >= memberCount),
                    $"client B to observe all {memberCount} members entering",
                    TimeSpan.FromSeconds(15),
                    PollInterval);

                var members = (await channelB.Presence.GetAsync()).ToList();

                // Client B observed every member entering via subscribe.
                enteredClientIds.Should().HaveCount(memberCount);

                // All members present via get().
                members.Should().HaveCount(memberCount);

                for (var i = 0; i < memberCount; i++)
                {
                    var expectedClientId = memberPrefix + i;
                    var member = members.SingleOrDefault(m => m.ClientId == expectedClientId);
                    member.Should().NotBeNull($"{expectedClientId} should be in the presence set");
                    member.Data.Should().Be("data-" + i);
                }
            }
            finally
            {
                clientA.Close();
                clientB.Close();
            }
        }

        // UTS: realtime/integration/RTP8/enter-update-leave-lifecycle-0
        [Fact]
        public async Task RTP8_EnterUpdateLeaveLifecycle()
        {
            var channelName = "presence-lifecycle-" + UtsSandbox.RandomId();

            // The spec's "lifecycle-client"; see the class summary for why it carries a suffix.
            var lifecycleClientId = "lifecycle-client-" + UtsSandbox.RandomId();

            var clientA = await SandboxRealtimeClient(
                configure: options => options.ClientId = lifecycleClientId);
            var clientB = await SandboxRealtimeClient();

            try
            {
                clientA.Connect();
                await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

                clientB.Connect();
                await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                var channelB = clientB.Channels.Get(channelName);

                var attachB = await channelB.AttachAsync();
                attachB.IsSuccess.Should().BeTrue("channel B must attach: {0}", attachB.Error);

                // Collect all presence events on client B.
                var allEvents = new ConcurrentQueue<PresenceMessage>();
                channelB.Presence.Subscribe(presenceMessage => allEvents.Enqueue(presenceMessage));

                // PRESENT is the presence *sync* telling a newly attached client who is already
                // there — it is not one of the lifecycle transitions this spec point is about, and
                // the server may deliver one for the same member alongside the ENTER. Indexing the
                // raw stream positionally therefore reads a PRESENT where the spec means UPDATE.
                // Filtering to the member under test and dropping PRESENT keeps the spec's
                // "enter, then update, then leave, in that order" assertion intact.
                List<PresenceMessage> Lifecycle() => allEvents
                    .Where(m => m.ClientId == lifecycleClientId && m.Action != PresenceAction.Present)
                    .ToList();

                var attachA = await channelA.AttachAsync();
                attachA.IsSuccess.Should().BeTrue("channel A must attach: {0}", attachA.Error);

                // --- Phase 1: Enter ---
                var enterResult = await channelA.Presence.EnterAsync("hello");
                enterResult.IsSuccess.Should().BeTrue("enter must succeed: {0}", enterResult.Error);

                await UtsSandbox.WallClockPollUntil(
                    () => Task.FromResult(Lifecycle().Count >= 1),
                    "the ENTER event to reach client B",
                    TimeSpan.FromSeconds(10),
                    PollInterval);

                var membersAfterEnter = (await channelB.Presence.GetAsync()).ToList();
                membersAfterEnter.Should().HaveCount(1);
                membersAfterEnter[0].ClientId.Should().Be(lifecycleClientId);
                membersAfterEnter[0].Data.Should().Be("hello");

                // --- Phase 2: Update ---
                var updateResult = await channelA.Presence.UpdateAsync("world");
                updateResult.IsSuccess.Should().BeTrue("update must succeed: {0}", updateResult.Error);

                await UtsSandbox.WallClockPollUntil(
                    () => Task.FromResult(Lifecycle().Count >= 2),
                    "the UPDATE event to reach client B",
                    TimeSpan.FromSeconds(10),
                    PollInterval);

                var membersAfterUpdate = (await channelB.Presence.GetAsync()).ToList();
                membersAfterUpdate.Should().HaveCount(1);
                membersAfterUpdate[0].Data.Should().Be("world");

                // --- Phase 3: Leave ---
                var leaveResult = await channelA.Presence.LeaveAsync("goodbye");
                leaveResult.IsSuccess.Should().BeTrue("leave must succeed: {0}", leaveResult.Error);

                await UtsSandbox.WallClockPollUntil(
                    () => Task.FromResult(Lifecycle().Count >= 3),
                    "the LEAVE event to reach client B",
                    TimeSpan.FromSeconds(10),
                    PollInterval);

                var membersAfterLeave = (await channelB.Presence.GetAsync()).ToList();
                membersAfterLeave.Should().BeEmpty();

                // Verify the sequence of events.
                var observed = Lifecycle();
                observed.Count.Should().BeGreaterOrEqualTo(3);

                var enterEvent = observed[0];
                enterEvent.Action.Should().Be(PresenceAction.Enter);
                enterEvent.ClientId.Should().Be(lifecycleClientId);
                enterEvent.Data.Should().Be("hello");

                var updateEvent = observed[1];
                updateEvent.Action.Should().Be(PresenceAction.Update);
                updateEvent.ClientId.Should().Be(lifecycleClientId);
                updateEvent.Data.Should().Be("world");

                var leaveEvent = observed[2];
                leaveEvent.Action.Should().Be(PresenceAction.Leave);
                leaveEvent.ClientId.Should().Be(lifecycleClientId);
                leaveEvent.Data.Should().Be("goodbye");
            }
            finally
            {
                clientA.Close();
                clientB.Close();
            }
        }
    }
}
