using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Channels
{
    /// <summary>
    /// Derived from uts/realtime/integration/channels/channel_attach_test.md in ably/specification.
    ///
    /// Spec points: RTL4, RTL4c, RTL5, RTL5d, RTL14
    ///
    /// Integration tier: nothing sits in front of the client, so every channel name carries a
    /// <see cref="UtsSandbox.RandomId"/> suffix and every wait is wall-clock. The spec's
    /// <c>key: subscribe_only_key</c> is <c>UtsSandbox.Key(3)</c> — the <c>{"*":["subscribe"]}</c> key.
    ///
    /// The spec's <c>AWAIT channel.attach()</c> rejects on failure; .NET's <c>AttachAsync()</c> returns
    /// a <see cref="Result"/> instead of throwing, so the equivalent translation asserts
    /// <c>IsSuccess</c> on it. <c>FAILS WITH error</c> likewise becomes <c>IsFailure</c> plus the
    /// <c>Error</c> carried on the result, not a thrown exception.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ChannelAttachTests : UtsRealtimeIntegrationTestBase
    {
        public ChannelAttachTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL4c/attach-succeeds-0
        [Fact]
        public async Task RTL4c_AttachSucceeds()
        {
            var channelName = "attach-RTL4c-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.State.Should().Be(ChannelState.Initialized);

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue(
                "the spec's AWAIT channel.attach() rejects on failure: {0}",
                attachResult.Error);

            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull();

            client.Close();
        }

        // UTS: realtime/integration/RTL5d/detach-succeeds-0
        [Fact]
        public async Task RTL5d_DetachSucceeds()
        {
            var channelName = "detach-RTL5d-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue(
                "the spec's AWAIT channel.attach() rejects on failure: {0}",
                attachResult.Error);
            channel.State.Should().Be(ChannelState.Attached);

            var detachResult = await channel.DetachAsync();
            detachResult.IsSuccess.Should().BeTrue(
                "the spec's AWAIT channel.detach() rejects on failure: {0}",
                detachResult.Error);

            channel.State.Should().Be(ChannelState.Detached);

            client.Close();
        }

        // UTS: realtime/integration/RTL14/insufficient-capability-failed-0
        [Fact]
        public async Task RTL14_InsufficientCapabilityFailed()
        {
            var channelName = "publish-not-allowed-" + UtsSandbox.RandomId();

            var sandbox = await Sandbox();
            var subscribeOnlyKey = sandbox.Key(3).KeyStr;

            var client = await SandboxRealtimeClient(
                key: subscribeOnlyKey,
                configure: options => options.AutoConnect = false);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);

            // Attach succeeds — a subscribe-only key may attach to any channel.
            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue(
                "the spec's AWAIT channel.attach() rejects on failure: {0}",
                attachResult.Error);
            channel.State.Should().Be(ChannelState.Attached);

            // Publish should fail — the key lacks the publish capability.
            var publishResult = await channel.PublishAsync("test", "data");

            publishResult.IsFailure.Should().BeTrue();
            publishResult.Error.Should().NotBeNull();
            publishResult.Error.Code.Should().Be(40160);
            ((int)publishResult.Error.StatusCode.Value).Should().Be(401);

            // The connection should remain connected: the error is channel scoped.
            client.Connection.State.Should().Be(ConnectionState.Connected);

            client.Close();
        }
    }
}
