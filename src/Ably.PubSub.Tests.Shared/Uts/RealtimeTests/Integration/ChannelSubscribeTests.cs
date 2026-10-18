// Realtime Channel Subscribe Integration Tests (RTL7, RTL7a, RTL7b, RTL7d)
// Derived from uts/realtime/integration/channels/channel_subscribe_test.md
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
    public class ChannelSubscribeTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ChannelSubscribeTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL7a/subscribe-all-messages-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL7a")]
        public async Task RTL7a_SubscribeAllMessages()
        {
            // Setup
            var channelName = "subscribe-all-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("event-a", "data-a"), "pub_channel.publish(event-a)");
            AssertSucceeded(await pubChannel.PublishAsync("event-b", "data-b"), "pub_channel.publish(event-b)");
            AssertSucceeded(await pubChannel.PublishAsync("event-c", "data-c"), "pub_channel.publish(event-c)");

            await UtsWait.PollUntil(
                () => received.Count >= 3,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 3 messages");

            // Assertions
            received.Count.Should().Be(3);

            var names = received.Snapshot().Select(m => m.Name).ToList();
            names.Should().Contain("event-a");
            names.Should().Contain("event-b");
            names.Should().Contain("event-c");

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // UTS: realtime/integration/RTL7b/subscribe-filtered-by-name-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL7b")]
        public async Task RTL7b_SubscribeFilteredByName()
        {
            // Setup
            var channelName = "subscribe-filtered-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            // Subscribe only for "target" events
            var targetReceived = new CaptureList<Message>();
            subChannel.Subscribe("target", msg => targetReceived.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);

            // Also subscribe to all events to know when publishing is complete
            var allReceived = new CaptureList<Message>();
            subChannel.Subscribe(msg => allReceived.Add(msg));

            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("other", "ignored"), "pub_channel.publish(other)");
            AssertSucceeded(await pubChannel.PublishAsync("target", "wanted-1"), "pub_channel.publish(target 1)");
            AssertSucceeded(await pubChannel.PublishAsync("other", "ignored"), "pub_channel.publish(other)");
            AssertSucceeded(await pubChannel.PublishAsync("target", "wanted-2"), "pub_channel.publish(target 2)");

            await UtsWait.PollUntil(
                () => allReceived.Count >= 4,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 4 messages");

            // Assertions
            // All 4 messages arrived
            allReceived.Count.Should().Be(4);

            // Filtered subscription received only "target" messages
            targetReceived.Count.Should().Be(2);
            targetReceived[0].Name.Should().Be("target");
            targetReceived[0].Data.Should().Be("wanted-1");
            targetReceived[1].Name.Should().Be("target");
            targetReceived[1].Data.Should().Be("wanted-2");

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // UTS: realtime/integration/RTL7/bidirectional-message-flow-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL7")]
        public async Task RTL7_BidirectionalMessageFlow()
        {
            // Setup
            var channelName = "subscribe-bidir-" + RandomId();

            var clientA = Realtime(new ClientOptions { AutoConnect = false, ClientId = "client-a" });
            var clientB = Realtime(new ClientOptions { AutoConnect = false, ClientId = "client-b" });

            clientA.Connect();
            clientB.Connect();
            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected);
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected);

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            // Test Steps
            var receivedByA = new CaptureList<Message>();
            var receivedByB = new CaptureList<Message>();

            channelA.Subscribe(msg => receivedByA.Add(msg));
            channelB.Subscribe(msg => receivedByB.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(channelA, ChannelState.Attached);
            await AwaitChannelState(channelB, ChannelState.Attached);

            // A publishes, B should receive
            AssertSucceeded(await channelA.PublishAsync("from-a", "hello from a"), "channel_a.publish()");

            // B publishes, A should receive
            AssertSucceeded(await channelB.PublishAsync("from-b", "hello from b"), "channel_b.publish()");

            await UtsWait.PollUntil(
                () => receivedByA.Count >= 2 && receivedByB.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "both clients received at least 2 messages");

            // Assertions
            // Both clients receive messages from both publishers (including their own echoes)
            var aNames = receivedByA.Snapshot().Select(m => m.Name).ToList();
            var bNames = receivedByB.Snapshot().Select(m => m.Name).ToList();

            aNames.Should().Contain("from-a");
            aNames.Should().Contain("from-b");
            bNames.Should().Contain("from-a");
            bNames.Should().Contain("from-b");

            await CloseClient(clientA);
            await CloseClient(clientB);
        }
    }
}
