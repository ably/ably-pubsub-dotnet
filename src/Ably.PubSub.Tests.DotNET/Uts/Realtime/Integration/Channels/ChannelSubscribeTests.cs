using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Channels
{
    /// <summary>
    /// Derived from uts/realtime/integration/channels/channel_subscribe_test.md in ably/specification.
    ///
    /// Spec points: RTL7, RTL7a, RTL7b, RTL7d
    ///
    /// Integration tier: nothing sits in front of the client, so every channel name and client id
    /// carries a <see cref="UtsSandbox.RandomId"/> suffix and every wait that follows a publish is
    /// wall-clock.
    ///
    /// The spec's <c>AWAIT pub_channel.publish(...)</c> and <c>AWAIT pub_channel.attach()</c> reject
    /// on failure; .NET returns a <see cref="Result"/> rather than throwing, so the equivalent
    /// translation asserts <c>IsSuccess</c> on it. <c>AWAIT sub_channel.subscribe(handler)</c> is a
    /// void <c>Subscribe(handler)</c> — which attaches the channel as a side effect — followed by a
    /// wait for ATTACHED, which is what the spec's awaited subscribe resolves on.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// Handlers run on an SDK thread, so each received list is appended under its own lock and read
    /// through <see cref="UtsClients.Snapshot{T}"/>.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ChannelSubscribeTests : UtsRealtimeIntegrationTestBase
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        public ChannelSubscribeTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL7a/subscribe-all-messages-0
        [Fact]
        public async Task RTL7a_SubscribeAllMessages()
        {
            var channelName = "subscribe-all-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            var received = new List<Message>();
            subChannel.Subscribe(message =>
            {
                lock (received)
                {
                    received.Add(message);
                }
            });
            await AwaitChannelState(subChannel, ChannelState.Attached);

            var attachResult = await pubChannel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("attach must succeed: {0}", attachResult.Error);

            var firstPublish = await pubChannel.PublishAsync("event-a", "data-a");
            firstPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", firstPublish.Error);

            var secondPublish = await pubChannel.PublishAsync("event-b", "data-b");
            secondPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", secondPublish.Error);

            var thirdPublish = await pubChannel.PublishAsync("event-c", "data-c");
            thirdPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", thirdPublish.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 3),
                "the unfiltered subscriber to receive all three messages",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(3);

            var names = messages.Select(m => m.Name).ToList();
            names.Should().Contain("event-a");
            names.Should().Contain("event-b");
            names.Should().Contain("event-c");

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RTL7b/subscribe-filtered-by-name-0
        [Fact]
        public async Task RTL7b_SubscribeFilteredByName()
        {
            var channelName = "subscribe-filtered-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Subscribe only for "target" events.
            var targetReceived = new List<Message>();
            subChannel.Subscribe("target", message =>
            {
                lock (targetReceived)
                {
                    targetReceived.Add(message);
                }
            });
            await AwaitChannelState(subChannel, ChannelState.Attached);

            // Also subscribe to all events to know when publishing is complete.
            var allReceived = new List<Message>();
            subChannel.Subscribe(message =>
            {
                lock (allReceived)
                {
                    allReceived.Add(message);
                }
            });

            var attachResult = await pubChannel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("attach must succeed: {0}", attachResult.Error);

            var firstPublish = await pubChannel.PublishAsync("other", "ignored");
            firstPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", firstPublish.Error);

            var secondPublish = await pubChannel.PublishAsync("target", "wanted-1");
            secondPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", secondPublish.Error);

            var thirdPublish = await pubChannel.PublishAsync("other", "ignored");
            thirdPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", thirdPublish.Error);

            var fourthPublish = await pubChannel.PublishAsync("target", "wanted-2");
            fourthPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", fourthPublish.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(allReceived).Count >= 4),
                "the unfiltered subscriber to receive all four messages",
                PollTimeout,
                PollInterval);

            // NOTE: the spec gates the whole test on the unfiltered subscription alone, which is safe
            // in a single-threaded event loop but not here: RealtimeChannel.OnMessage notifies the
            // unfiltered handlers before the name-filtered ones, so a test thread polling the
            // unfiltered list can observe the state between the two for the last message. The second
            // wait closes that window without weakening anything — the exact counts are still what is
            // asserted, so an over-delivering filter still fails.
            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(targetReceived).Count >= 2),
                "the name-filtered subscriber to receive both target messages",
                PollTimeout,
                PollInterval);

            // All 4 messages arrived.
            UtsClients.Snapshot(allReceived).Should().HaveCount(4);

            // The filtered subscription received only the "target" messages.
            var targets = UtsClients.Snapshot(targetReceived);
            targets.Should().HaveCount(2);
            targets[0].Name.Should().Be("target");
            targets[0].Data.Should().Be("wanted-1");
            targets[1].Name.Should().Be("target");
            targets[1].Data.Should().Be("wanted-2");

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RTL7/bidirectional-message-flow-0
        [Fact]
        public async Task RTL7_BidirectionalMessageFlow()
        {
            var suffix = UtsSandbox.RandomId();
            var channelName = "subscribe-bidir-" + suffix;

            // The sandbox app is shared, so the spec's fixed "client-a" / "client-b" take the same
            // random suffix as the channel. No assertion in this test reads the client id back.
            var clientA = await SandboxRealtimeClient(configure: options =>
            {
                options.AutoConnect = false;
                options.ClientId = "client-a-" + suffix;
            });

            var clientB = await SandboxRealtimeClient(configure: options =>
            {
                options.AutoConnect = false;
                options.ClientId = "client-b-" + suffix;
            });

            clientA.Connect();
            clientB.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            var receivedByA = new List<Message>();
            var receivedByB = new List<Message>();

            channelA.Subscribe(message =>
            {
                lock (receivedByA)
                {
                    receivedByA.Add(message);
                }
            });
            await AwaitChannelState(channelA, ChannelState.Attached);

            channelB.Subscribe(message =>
            {
                lock (receivedByB)
                {
                    receivedByB.Add(message);
                }
            });
            await AwaitChannelState(channelB, ChannelState.Attached);

            // A publishes, B should receive.
            var publishFromA = await channelA.PublishAsync("from-a", "hello from a");
            publishFromA.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishFromA.Error);

            // B publishes, A should receive.
            var publishFromB = await channelB.PublishAsync("from-b", "hello from b");
            publishFromB.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishFromB.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(
                    UtsClients.Snapshot(receivedByA).Count >= 2 &&
                    UtsClients.Snapshot(receivedByB).Count >= 2),
                "both clients to receive both messages",
                PollTimeout,
                PollInterval);

            // Both clients receive messages from both publishers, including their own echoes.
            var aNames = UtsClients.Snapshot(receivedByA).Select(m => m.Name).ToList();
            var bNames = UtsClients.Snapshot(receivedByB).Select(m => m.Name).ToList();

            aNames.Should().Contain("from-a");
            aNames.Should().Contain("from-b");
            bNames.Should().Contain("from-a");
            bNames.Should().Contain("from-b");

            clientA.Close();
            clientB.Close();
        }
    }
}
