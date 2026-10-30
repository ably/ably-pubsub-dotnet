// Realtime Channel Publish Integration Tests (RTL6, RTL6f, RSL4, RSL6, RSL6a2)
// Derived from uts/realtime/integration/channels/channel_publish_test.md
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
    public class ChannelPublishTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ChannelPublishTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL6/string-data-roundtrip-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL6")]
        public async Task RTL6_StringDataRoundtrip()
        {
            // Setup
            var channelName = "publish-string-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            // Subscribe first, then publish
            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("string-event", "hello world"), "pub_channel.publish()");

            await UtsWait.PollUntil(
                () => received.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 1 message");

            // Assertions
            received.Count.Should().Be(1);
            received[0].Name.Should().Be("string-event");
            received[0].Data.Should().Be("hello world");
            received[0].Data.Should().BeOfType<string>();

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL6/json-data-roundtrip-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL6")]
        public async Task RTL6_JsonDataRoundtrip()
        {
            // Setup
            var channelName = "publish-json-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            var jsonData = new JObject
            {
                ["key"] = "value",
                ["nested"] = new JObject { ["count"] = 42 },
                ["list"] = new JArray(1, 2, 3),
            };

            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("json-event", jsonData), "pub_channel.publish()");

            await UtsWait.PollUntil(
                () => received.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 1 message");

            // Assertions
            received.Count.Should().Be(1);
            received[0].Name.Should().Be("json-event");
            var data = received[0].Data.Should().BeAssignableTo<JObject>().Subject;
            ((string)data["key"]).Should().Be("value");
            ((int)data["nested"]["count"]).Should().Be(42);
            data["list"].Select(item => (int)item).Should().Equal(1, 2, 3);

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL6/binary-data-roundtrip-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL6")]
        public async Task RTL6_BinaryDataRoundtrip()
        {
            // Setup
            var channelName = "publish-binary-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            // Create a binary payload with known content
            var binaryData = new byte[] { 0, 1, 2, 255, 128, 64 };

            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("binary-event", binaryData), "pub_channel.publish()");

            await UtsWait.PollUntil(
                () => received.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 1 message");

            // Assertions
            received.Count.Should().Be(1);
            received[0].Name.Should().Be("binary-event");
            var receivedData = received[0].Data.Should().BeAssignableTo<byte[]>().Subject;
            receivedData.Should().Equal(binaryData);

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL6f/connectionid-matches-publisher-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL6f")]
        public async Task RTL6f_ConnectionidMatchesPublisher()
        {
            // Setup
            var channelName = "publish-connid-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            var publisherConnectionId = publisher.Connection.Id;

            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync("connid-test", "data"), "pub_channel.publish()");

            await UtsWait.PollUntil(
                () => received.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 1 message");

            // Assertions
            received[0].ConnectionId.Should().Be(publisherConnectionId);
            received[0].ConnectionId.Should().NotBe(subscriber.Connection.Id);

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RSL6a2/message-extras-roundtrip-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL6a2")]
        public async Task RSL6a2_MessageExtrasRoundtrip()
        {
            // Setup
            var channelName = "pushenabled:publish-extras-" + RandomId();

            var publisher = Realtime(new ClientOptions { AutoConnect = false });
            var subscriber = Realtime(new ClientOptions { AutoConnect = false });

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Test Steps
            var extras = new JObject
            {
                ["push"] = new JObject
                {
                    ["notification"] = new JObject { ["title"] = "Testing" },
                },
            };

            var received = new CaptureList<Message>();
            subChannel.Subscribe(msg => received.Add(msg));

            // NOTE: Subscribe is void and attaches implicitly; the spec's AWAIT subscribe() (which waits for the attach) is rendered as an explicit wait for ATTACHED
            await AwaitChannelState(subChannel, ChannelState.Attached);
            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");

            AssertSucceeded(await pubChannel.PublishAsync(new Message("extras-test", "payload", extras: new MessageExtras(extras))), "pub_channel.publish()");

            await UtsWait.PollUntil(
                () => received.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "received at least 1 message");

            // Assertions
            received[0].Extras.Should().NotBeNull();
            ((string)received[0].Extras.ToJson()["push"]["notification"]["title"]).Should().Be("Testing");

            await CloseClient(publisher);
            await CloseClient(subscriber);
        }
    }
}
