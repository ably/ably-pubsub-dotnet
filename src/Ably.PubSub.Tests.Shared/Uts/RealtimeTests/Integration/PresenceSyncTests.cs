// Realtime Presence Sync Integration Tests (RTP2, RTP11a)
// Derived from uts/realtime/integration/presence/presence_sync_test.md
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
    public class PresenceSyncTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public PresenceSyncTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTP2/sync-delivers-members-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP2")]
        public async Task RTP2_SyncDeliversMembers()
        {
            // Setup
            var channelName = "presence-sync-" + RandomId();

            var clientA = Realtime(new ClientOptions { ClientId = "sync-member-a", AutoConnect = false });

            var clientB = Realtime(new ClientOptions { AutoConnect = false });

            // Test Steps
            // Connect client A and enter presence
            clientA.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

            var channelA = clientA.Channels.Get(channelName);
            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");
            AssertSucceeded(await channelA.Presence.EnterAsync("sync-data"), "channel_a.presence.enter()");

            // Now connect client B — client A is already present
            clientB.Connect();
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            var channelB = clientB.Channels.Get(channelName);
            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // presence.get() waits for SYNC to complete (RTP11a)
            var members = (await channelB.Presence.GetAsync()).ToList();

            // Assertions
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("sync-member-a");
            members[0].Data.Should().Be("sync-data");
            members[0].Action.Should().Be(PresenceAction.Present);

            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // UTS: realtime/integration/RTP2/sync-multiple-members-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTP2")]
        public async Task RTP2_SyncMultipleMembers()
        {
            // Setup
            var channelName = "presence-sync-multi-" + RandomId();
            var memberCount = 10;

            var clientA = Realtime(new ClientOptions { AutoConnect = false });

            var clientB = Realtime(new ClientOptions { AutoConnect = false });

            // Test Steps
            // Connect client A and enter multiple members
            clientA.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);

            var channelA = clientA.Channels.Get(channelName);
            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            for (var i = 0; i <= memberCount - 1; i++)
            {
                AssertSucceeded(await channelA.Presence.EnterClientAsync("sync-user-" + i, "data-" + i), "channel_a.presence.enterClient(sync-user-" + i + ")");
            }

            // Now connect client B
            clientB.Connect();
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            var channelB = clientB.Channels.Get(channelName);
            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // presence.get() waits for SYNC to complete
            var members = (await channelB.Presence.GetAsync()).ToList();

            // Assertions
            members.Should().HaveCount(memberCount);

            for (var i = 0; i <= memberCount - 1; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == "sync-user-" + i);
                member.Should().NotBeNull();
                member.Data.Should().Be("data-" + i);
            }

            await CloseClient(clientA);
            await CloseClient(clientB);
        }
    }
}
