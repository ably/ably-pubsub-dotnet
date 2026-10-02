using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channels_collection.md in ably/specification.
    ///
    /// Spec points: RTS1, RTS2, RTS3a, RTS4a
    ///
    /// <para>
    /// Three shape differences, all idiomatic. The spec's <c>channels.names</c> has no counterpart
    /// property - <c>RealtimeChannels</c> is an <c>IEnumerable&lt;IRealtimeChannel&gt;</c>, so the
    /// names come from enumerating it. <c>release()</c> is synchronous and returns a <c>bool</c>
    /// saying whether anything was removed, rather than a task to await. And the spec's
    /// <c>channels[name]</c> subscript is the indexer, which exists here too.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelsCollectionTests : UtsTestBase
    {
        public ChannelsCollectionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTS1/channels-collection-accessible-0
        [Fact]
        public void RTS1_ChannelsCollectionAccessible()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Should().NotBeNull();
            client.Channels.Should().BeOfType<RealtimeChannels>();
        }

        // UTS: realtime/unit/RTS2/channel-exists-check-0
        [Fact]
        public void RTS2_ChannelExistsCheck()
        {
            const string ChannelName = "test-RTS2";
            const string OtherChannelName = "test-RTS2-other";

            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Exists(ChannelName).Should().BeFalse();

            client.Channels.Get(ChannelName);

            client.Channels.Exists(ChannelName).Should().BeTrue();
            client.Channels.Exists(OtherChannelName).Should().BeFalse();
        }

        // UTS: realtime/unit/RTS2/iterate-channels-1
        [Fact]
        public void RTS2_IterateChannels()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Get("test-RTS2-a");
            client.Channels.Get("test-RTS2-b");
            client.Channels.Get("test-RTS2-c");

            var names = client.Channels.Select(channel => channel.Name).ToList();

            names.Should().Contain("test-RTS2-a");
            names.Should().Contain("test-RTS2-b");
            names.Should().Contain("test-RTS2-c");
            names.Should().HaveCount(3);
        }

        // UTS: realtime/unit/RTS3a/get-creates-new-channel-0
        [Fact]
        public void RTS3a_GetCreatesNewChannel()
        {
            const string ChannelName = "test-RTS3a";

            var client = RealtimeClient(new MockWebSocket());

            var channel = client.Channels.Get(ChannelName);

            channel.Should().BeAssignableTo<IRealtimeChannel>();
            channel.Name.Should().Be(ChannelName);
            client.Channels.Exists(ChannelName).Should().BeTrue();
        }

        // UTS: realtime/unit/RTS3a/get-returns-existing-channel-1
        [Fact]
        public void RTS3a_GetReturnsExistingChannel()
        {
            const string ChannelName = "test-RTS3a-existing";

            var client = RealtimeClient(new MockWebSocket());

            var channel1 = client.Channels.Get(ChannelName);
            var channel2 = client.Channels.Get(ChannelName);

            channel2.Should().BeSameAs(channel1);
            channel1.Name.Should().Be(ChannelName);
        }

        // UTS: realtime/unit/RTS3a/subscript-operator-channel-2
        [Fact]
        public void RTS3a_SubscriptOperatorReturnsSameChannel()
        {
            const string ChannelName = "test-RTS3a-subscript";

            var client = RealtimeClient(new MockWebSocket());

            var channel1 = client.Channels[ChannelName];
            var channel2 = client.Channels.Get(ChannelName);
            var channel3 = client.Channels[ChannelName];

            channel2.Should().BeSameAs(channel1);
            channel3.Should().BeSameAs(channel2);
            channel1.Name.Should().Be(ChannelName);
        }

        // UTS: realtime/unit/RTS4a/release-removes-channel-0
        [Fact]
        public void RTS4a_ReleaseRemovesChannel()
        {
            const string ChannelName = "test-RTS4a";

            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Get(ChannelName);
            client.Channels.Exists(ChannelName).Should().BeTrue();

            client.Channels.Release(ChannelName);

            client.Channels.Exists(ChannelName).Should().BeFalse();
        }

        // UTS: realtime/unit/RTS4a/release-nonexistent-noop-1
        [Fact]
        public void RTS4a_ReleaseNonexistentIsNoOp()
        {
            const string ChannelName = "test-RTS4a-nonexistent";

            var client = RealtimeClient(new MockWebSocket());

            Action act = () => client.Channels.Release(ChannelName);

            act.Should().NotThrow();
            client.Channels.Exists(ChannelName).Should().BeFalse();
        }

        // UTS: realtime/unit/RTS4a/release-detaches-attached-2
        [Fact]
        public async Task RTS4a_ReleaseDetachesAttachedChannel()
        {
            const string ChannelName = "test-RTS4a-attached";

            var detachSeen = false;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachSeen = true;
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var stateBeforeRelease = channel.State;

            client.Channels.Release(ChannelName);

            await UtsClients.PollUntil(
                () => !client.Channels.Exists(ChannelName),
                "the channel left the collection");

            stateBeforeRelease.Should().Be(ChannelState.Attached);
            client.Channels.Exists(ChannelName).Should().BeFalse();

            await UtsClients.PollUntil(
                () => detachSeen,
                "RTS4a - the channel is detached before it is released");
        }

        // UTS: realtime/unit/RTS3a/get-after-release-new-3
        [Fact]
        public void RTS3a_GetAfterReleaseCreatesNewChannel()
        {
            const string ChannelName = "test-RTS3a-release";

            var client = RealtimeClient(new MockWebSocket());

            var channel1 = client.Channels.Get(ChannelName);

            client.Channels.Release(ChannelName);

            var channel2 = client.Channels.Get(ChannelName);

            channel2.Should().NotBeSameAs(channel1, "a released channel is gone, not reused");
            channel2.Name.Should().Be(ChannelName);
            client.Channels.Exists(ChannelName).Should().BeTrue();
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
