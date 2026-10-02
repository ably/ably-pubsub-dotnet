using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Channels
{
    /// <summary>
    /// Derived from uts/realtime/integration/channels/channel_publish_test.md in ably/specification.
    ///
    /// Spec points: RTL6, RTL6f, RSL4, RSL4d1, RSL4d2, RSL4d3, RSL6, RSL6a, RSL6a2
    ///
    /// Integration tier: nothing sits in front of the client, so every channel name carries a
    /// <see cref="UtsSandbox.RandomId"/> suffix and every wait that follows a publish is wall-clock.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// The spec's <c>AWAIT pub_channel.publish(...)</c> and <c>AWAIT pub_channel.attach()</c> reject
    /// on failure; .NET returns a <see cref="Result"/> rather than throwing, so the equivalent
    /// translation asserts <c>IsSuccess</c> on it. <c>AWAIT sub_channel.subscribe(handler)</c> is a
    /// void <c>Subscribe(handler)</c> — which attaches the channel as a side effect — followed by a
    /// wait for ATTACHED, which is what the spec's awaited subscribe resolves on.
    ///
    /// Handlers run on an SDK thread, so each received list is appended under its own lock and read
    /// through <see cref="UtsClients.Snapshot{T}"/>.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ChannelPublishTests : UtsRealtimeIntegrationTestBase
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        public ChannelPublishTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL6/string-data-roundtrip-0
        [Fact]
        public async Task RTL6_StringDataRoundtrip()
        {
            var channelName = "publish-string-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // Subscribe first, then publish.
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

            var publishResult = await pubChannel.PublishAsync("string-event", "hello world");
            publishResult.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishResult.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 1),
                "the subscriber to receive the published string message",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(1);
            messages[0].Name.Should().Be("string-event");
            messages[0].Data.Should().BeOfType<string>();
            ((string)messages[0].Data).Should().Be("hello world");

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RTL6/json-data-roundtrip-1
        [Fact]
        public async Task RTL6_JsonDataRoundtrip()
        {
            var channelName = "publish-json-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // NOTE: the spec's object literal is rendered as a JObject, which is also what the .NET
            // decoder produces on the way back in, so the published and received shapes are directly
            // comparable. Any JSON-serialisable graph would travel the same RSL4d3 path.
            var jsonData = new JObject
            {
                ["key"] = "value",
                ["nested"] = new JObject { ["count"] = 42 },
                ["list"] = new JArray(1, 2, 3),
            };

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

            var publishResult = await pubChannel.PublishAsync("json-event", jsonData);
            publishResult.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishResult.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 1),
                "the subscriber to receive the published JSON message",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(1);
            messages[0].Name.Should().Be("json-event");

            messages[0].Data.Should().BeOfType<JObject>();
            var data = (JObject)messages[0].Data;
            data["key"].Value<string>().Should().Be("value");
            data["nested"]["count"].Value<int>().Should().Be(42);
            JToken.DeepEquals(data["list"], new JArray(1, 2, 3)).Should().BeTrue(
                "the spec asserts the list round-trips as [1, 2, 3]");

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RTL6/binary-data-roundtrip-2
        [Fact]
        public async Task RTL6_BinaryDataRoundtrip()
        {
            var channelName = "publish-binary-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            // A binary payload with known content.
            var binaryData = new byte[] { 0, 1, 2, 255, 128, 64 };

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

            var publishResult = await pubChannel.PublishAsync("binary-event", binaryData);
            publishResult.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishResult.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 1),
                "the subscriber to receive the published binary message",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(1);
            messages[0].Name.Should().Be("binary-event");

            // RSL4d1 / RSL6a: the base64 encode on the way out and the decode on the way back in are
            // the encoding layer's job, so the payload must arrive as bytes, not as a base64 string.
            messages[0].Data.Should().BeOfType<byte[]>();
            ((byte[])messages[0].Data).Should().Equal(binaryData);

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RTL6f/connectionid-matches-publisher-0
        [Fact]
        public async Task RTL6f_ConnectionIdMatchesPublisher()
        {
            var channelName = "publish-connid-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            var publisherConnectionId = publisher.Connection.Id;

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

            var publishResult = await pubChannel.PublishAsync("connid-test", "data");
            publishResult.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishResult.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 1),
                "the subscriber to receive the published message",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages[0].ConnectionId.Should().Be(publisherConnectionId);
            messages[0].ConnectionId.Should().NotBe(subscriber.Connection.Id);

            publisher.Close();
            subscriber.Close();
        }

        // UTS: realtime/integration/RSL6a2/message-extras-roundtrip-0
        [Fact]
        public async Task RSL6a2_MessageExtrasRoundtrip()
        {
            var channelName = "pushenabled:publish-extras-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);
            var subscriber = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            publisher.Connect();
            subscriber.Connect();
            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected);
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected);

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            var extras = new MessageExtras(JObject.Parse(
                "{\"push\":{\"notification\":{\"title\":\"Testing\"}}}"));

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

            var publishResult = await pubChannel.PublishAsync(
                new Message("extras-test", "payload", null, extras));
            publishResult.IsSuccess.Should().BeTrue("publish must succeed: {0}", publishResult.Error);

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(UtsClients.Snapshot(received).Count >= 1),
                "the subscriber to receive the message carrying extras",
                PollTimeout,
                PollInterval);

            var messages = UtsClients.Snapshot(received);
            messages[0].Extras.Should().NotBeNull();

            var extrasJson = messages[0].Extras.ToJson();
            extrasJson["push"]["notification"]["title"].Value<string>().Should().Be("Testing");

            publisher.Close();
            subscriber.Close();
        }
    }
}
