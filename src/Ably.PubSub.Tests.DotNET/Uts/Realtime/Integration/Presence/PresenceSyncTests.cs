using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Presence
{
    /// <summary>
    /// Derived from uts/realtime/integration/presence/presence_sync_test.md in ably/specification.
    ///
    /// Spec points: RTP2, RTP11a
    ///
    /// Both tests target the SYNC path rather than real-time delivery: client B attaches only after
    /// client A has entered, so B learns the members from the server-initiated SYNC. Neither test
    /// needs a poll after the attach, because <c>presence.get()</c> waits for the sync to complete
    /// (RTP11a) — <c>GetAsync</c>'s <c>waitForSync</c> defaults to true, which is the spec's
    /// behaviour, so the spec's bare <c>presence.get()</c> translates to a bare <c>GetAsync()</c>.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// The spec's <c>AWAIT channel.attach()</c> and <c>AWAIT presence.enter()</c> reject on failure;
    /// the .NET equivalents return a <see cref="Result"/> rather than throwing, so each one asserts
    /// <c>IsSuccess</c> on the returned result.
    ///
    /// Every channel name and client id carries a <see cref="UtsSandbox.RandomId"/> suffix because the
    /// sandbox app is shared across the run, so the spec's literal <c>"sync-member-a"</c> and
    /// <c>"sync-user-" + i</c> ids become per-run locals that both the enter and the assertion read.
    /// <c>CLOSE_CLIENT</c> runs in a <c>finally</c> so the members leave the shared app even when an
    /// assertion fails first.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PresenceSyncTests : UtsRealtimeIntegrationTestBase
    {
        public PresenceSyncTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTP2/sync-delivers-members-0
        [Fact]
        public async Task RTP2_SyncDeliversMembers()
        {
            var channelName = "presence-sync-" + UtsSandbox.RandomId();

            // The spec's "sync-member-a"; see the class summary for why it carries a suffix.
            var memberClientId = "sync-member-a-" + UtsSandbox.RandomId();

            var clientA = await SandboxRealtimeClient(configure: options =>
            {
                options.ClientId = memberClientId;
                options.AutoConnect = false;
            });

            var clientB = await SandboxRealtimeClient(
                configure: options => options.AutoConnect = false);

            try
            {
                // Connect client A and enter presence.
                clientA.Connect();
                await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                var attachA = await channelA.AttachAsync();
                attachA.IsSuccess.Should().BeTrue("channel A must attach: {0}", attachA.Error);

                var enterResult = await channelA.Presence.EnterAsync("sync-data");
                enterResult.IsSuccess.Should().BeTrue("enter must succeed: {0}", enterResult.Error);

                // Now connect client B — client A is already present.
                clientB.Connect();
                await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

                var channelB = clientB.Channels.Get(channelName);
                var attachB = await channelB.AttachAsync();
                attachB.IsSuccess.Should().BeTrue("channel B must attach: {0}", attachB.Error);

                // presence.get() waits for SYNC to complete (RTP11a).
                var members = (await channelB.Presence.GetAsync()).ToList();

                members.Should().HaveCount(1);
                members[0].ClientId.Should().Be(memberClientId);
                members[0].Data.Should().Be("sync-data");
                members[0].Action.Should().Be(PresenceAction.Present);
            }
            finally
            {
                clientA.Close();
                clientB.Close();
            }
        }

        // UTS: realtime/integration/RTP2/sync-multiple-members-1
        [Fact]
        public async Task RTP2_SyncMultipleMembers()
        {
            const int memberCount = 10;
            var channelName = "presence-sync-multi-" + UtsSandbox.RandomId();

            // The spec's "sync-user-" + i; see the class summary for why it carries a suffix.
            var memberPrefix = "sync-user-" + UtsSandbox.RandomId() + "-";

            var clientA = await SandboxRealtimeClient(
                configure: options => options.AutoConnect = false);
            var clientB = await SandboxRealtimeClient(
                configure: options => options.AutoConnect = false);

            try
            {
                // Connect client A and enter multiple members.
                clientA.Connect();
                await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                var attachA = await channelA.AttachAsync();
                attachA.IsSuccess.Should().BeTrue("channel A must attach: {0}", attachA.Error);

                for (var i = 0; i < memberCount; i++)
                {
                    var enterResult = await channelA.Presence.EnterClientAsync(
                        memberPrefix + i,
                        "data-" + i);
                    enterResult.IsSuccess.Should().BeTrue(
                        "entering {0} must succeed: {1}",
                        memberPrefix + i,
                        enterResult.Error);
                }

                // Now connect client B.
                clientB.Connect();
                await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

                var channelB = clientB.Channels.Get(channelName);
                var attachB = await channelB.AttachAsync();
                attachB.IsSuccess.Should().BeTrue("channel B must attach: {0}", attachB.Error);

                // presence.get() waits for SYNC to complete.
                var members = (await channelB.Presence.GetAsync()).ToList();

                members.Should().HaveCount(memberCount);

                for (var i = 0; i < memberCount; i++)
                {
                    var expectedClientId = memberPrefix + i;
                    var member = members.SingleOrDefault(m => m.ClientId == expectedClientId);
                    member.Should().NotBeNull($"{expectedClientId} should have arrived via SYNC");
                    member.Data.Should().Be("data-" + i);
                }
            }
            finally
            {
                clientA.Close();
                clientB.Close();
            }
        }
    }
}
