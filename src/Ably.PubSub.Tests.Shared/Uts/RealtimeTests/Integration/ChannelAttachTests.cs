// Realtime Channel Attach/Detach Integration Tests (RTL4, RTL4c, RTL5, RTL5d, RTL14)
// Derived from uts/realtime/integration/channels/channel_attach_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
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
    public class ChannelAttachTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ChannelAttachTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL4c/attach-succeeds-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_AttachSucceeds()
        {
            // Setup
            var channelName = "attach-RTL4c-" + RandomId();

            var client = Realtime(new ClientOptions { AutoConnect = false });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Test Steps
            var channel = client.Channels.Get(channelName);
            channel.State.Should().Be(ChannelState.Initialized);

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/integration/RTL5d/detach-succeeds-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL5d")]
        public async Task RTL5d_DetachSucceeds()
        {
            // Setup
            var channelName = "detach-RTL5d-" + RandomId();

            var client = Realtime(new ClientOptions { AutoConnect = false });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Test Steps
            AssertSucceeded(await channel.DetachAsync(), "channel.detach()");

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);

            await CloseClient(client);
        }

        // UTS: realtime/integration/RTL14/insufficient-capability-failed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL14")]
        public async Task RTL14_InsufficientCapabilityFailed()
        {
            // Setup
            var channelName = "publish-not-allowed-" + RandomId();

            // Use key with subscribe-only capability
            // Key at index 3 has subscribe-only capability: {"*":["subscribe"]}
            var subscribeOnlyKey = App.Keys[3].KeyStr;
            var client = Realtime(new ClientOptions { Key = subscribeOnlyKey, AutoConnect = false });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Test Steps
            var channel = client.Channels.Get(channelName);

            // Attach succeeds (subscribe-only key can attach to any channel)
            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Publish should fail — key lacks publish capability
            var publishResult = await channel.PublishAsync("test", "data");
            publishResult.IsFailure.Should().BeTrue("AWAIT channel.publish(...) FAILS WITH error");
            var error = publishResult.Error;

            // Assertions
            error.Should().NotBeNull();
            error.Code.Should().Be(40160);
            ((int?)error.StatusCode).Should().Be(401);

            // Connection should remain connected (channel-scoped error)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            await CloseClient(client);
        }
    }
}
