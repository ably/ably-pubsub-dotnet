// Realtime Mutable Messages & Annotations Integration Tests (RTL28, RTL31, RTL32, RTAN1, RTAN2, RTAN4)
// Derived from uts/realtime/integration/mutable_messages_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); the sandbox app's mutable namespace (mutableMessages: true) is added by SandboxApp (the vendored test-app-setup.json lacks it)

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
    public class MutableMessagesTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 90000;

        public MutableMessagesTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL32/update-message-observed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL32")]
        public async Task RTL32_UpdateMessageObserved()
        {
            // Setup
            var channelName = "mutable:rt-update-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // Collect all messages on client B
            var receivedMessages = new CaptureList<Message>();
            channelB.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // Publish original message via realtime
            AssertSucceeded(await channelA.PublishAsync("original", "v1"), "channel_a.publish()");

            // Wait for client B to receive the original
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the original");

            // Get the serial from the received message
            var serial = receivedMessages[0].Serial;

            // Update via realtime
            var updateResult = await channelA.UpdateMessageAsync(
                new Message { Serial = serial, Name = "updated", Data = "v2" },
                new MessageOperation { Description = "edited" });
            AssertSucceeded(updateResult, "channel_a.updateMessage()");

            // Wait for client B to receive the update event
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the update");

            // Assertions
            // Update returned a result
            // ASSERT update_result IS UpdateDeleteResult: guaranteed by the declared type Result<UpdateDeleteResult>
            updateResult.Value.Should().NotBeNull();
            // ASSERT update_result.versionSerial IS String: guaranteed by the declared type string
            updateResult.Value.VersionSerial.Should().NotBeNullOrEmpty();

            // Client B received the original
            receivedMessages[0].Action.Should().Be(MessageAction.MessageCreate);
            receivedMessages[0].Name.Should().Be("original");
            receivedMessages[0].Data.Should().Be("v1");
            // ASSERT received_messages[0].serial IS String: guaranteed by the declared type string
            receivedMessages[0].Serial.Should().NotBeNullOrEmpty();

            // Client B received the update in real-time
            var updateMsg = receivedMessages[1];
            updateMsg.Action.Should().Be(MessageAction.MessageUpdate);
            updateMsg.Name.Should().Be("updated");
            updateMsg.Data.Should().Be("v2");
            updateMsg.Serial.Should().Be(serial);

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL32/delete-message-observed-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL32")]
        public async Task RTL32_DeleteMessageObserved()
        {
            // Setup
            var channelName = "mutable:rt-delete-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            var receivedMessages = new CaptureList<Message>();
            channelB.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // Publish original
            AssertSucceeded(await channelA.PublishAsync("to-delete", "ephemeral"), "channel_a.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the original");

            var serial = receivedMessages[0].Serial;

            // Delete via realtime
            var deleteResult = await channelA.DeleteMessageAsync(new Message { Serial = serial });
            AssertSucceeded(deleteResult, "channel_a.deleteMessage()");

            // Wait for delete event
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the delete");

            // Assertions
            // ASSERT delete_result IS UpdateDeleteResult: guaranteed by the declared type Result<UpdateDeleteResult>
            deleteResult.Value.Should().NotBeNull();
            // ASSERT delete_result.versionSerial IS String: guaranteed by the declared type string
            deleteResult.Value.VersionSerial.Should().NotBeNullOrEmpty();

            // Client B received the delete event
            var deleteMsg = receivedMessages[1];
            deleteMsg.Action.Should().Be(MessageAction.MessageDelete);
            deleteMsg.Serial.Should().Be(serial);

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL32/append-message-observed-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL32")]
        public async Task RTL32_AppendMessageObserved()
        {
            // Setup
            var channelName = "mutable:rt-append-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            var receivedMessages = new CaptureList<Message>();
            channelB.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // Publish original
            AssertSucceeded(await channelA.PublishAsync("appendable", "original"), "channel_a.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the original");

            var serial = receivedMessages[0].Serial;

            // Append via realtime
            var appendResult = await channelA.AppendMessageAsync(
                new Message { Serial = serial, Data = "appended-data" },
                new MessageOperation { Description = "thread reply" });
            AssertSucceeded(appendResult, "channel_a.appendMessage()");

            // Wait for append event
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the append");

            // Assertions
            // ASSERT append_result IS UpdateDeleteResult: guaranteed by the declared type Result<UpdateDeleteResult>
            appendResult.Value.Should().NotBeNull();
            // ASSERT append_result.versionSerial IS String: guaranteed by the declared type string
            appendResult.Value.VersionSerial.Should().NotBeNullOrEmpty();

            // Client B received the append event
            var appendMsg = receivedMessages[1];
            appendMsg.Action.Should().Be(MessageAction.MessageAppend);
            appendMsg.Data.Should().Be("appended-data");
            appendMsg.Serial.Should().Be(serial);

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL32/full-mutation-lifecycle-3
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL32")]
        public async Task RTL32_FullMutationLifecycle()
        {
            // Setup
            var channelName = "mutable:rt-lifecycle-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            var receivedMessages = new CaptureList<Message>();
            channelB.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // 1. Publish original
            AssertSucceeded(await channelA.PublishAsync("lifecycle", "v1"), "channel_a.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the original");

            var serial = receivedMessages[0].Serial;

            // 2. Update
            AssertSucceeded(
                await channelA.UpdateMessageAsync(
                    new Message { Serial = serial, Name = "lifecycle", Data = "v2" },
                    new MessageOperation { Description = "edit 1" }),
                "channel_a.updateMessage()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the update");

            // 3. Append
            AssertSucceeded(
                await channelA.AppendMessageAsync(
                    new Message { Serial = serial, Data = "reply-data" },
                    new MessageOperation { Description = "thread reply" }),
                "channel_a.appendMessage()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 3,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the append");

            // 4. Delete
            AssertSucceeded(await channelA.DeleteMessageAsync(new Message { Serial = serial }), "channel_a.deleteMessage()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 4,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the delete");

            // Assertions
            receivedMessages.Count.Should().Be(4);

            // Create
            receivedMessages[0].Action.Should().Be(MessageAction.MessageCreate);
            receivedMessages[0].Name.Should().Be("lifecycle");
            receivedMessages[0].Data.Should().Be("v1");
            receivedMessages[0].Serial.Should().Be(serial);

            // Update
            receivedMessages[1].Action.Should().Be(MessageAction.MessageUpdate);
            receivedMessages[1].Name.Should().Be("lifecycle");
            receivedMessages[1].Data.Should().Be("v2");
            receivedMessages[1].Serial.Should().Be(serial);

            // Append
            receivedMessages[2].Action.Should().Be(MessageAction.MessageAppend);
            receivedMessages[2].Data.Should().Be("reply-data");
            receivedMessages[2].Serial.Should().Be(serial);

            // Delete
            receivedMessages[3].Action.Should().Be(MessageAction.MessageDelete);
            receivedMessages[3].Serial.Should().Be(serial);

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL28/get-message-and-versions-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL28")]
        public async Task RTL28_GetMessageAndVersions()
        {
            // Setup
            var channelName = "mutable:rt-get-versions-" + RandomId();

            var client = Realtime();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channel = client.Channels.Get(channelName);
            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            // Use subscribe to capture the serial from the published message
            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Test Steps
            // Publish original
            AssertSucceeded(await channel.PublishAsync("versioned", "v1"), "channel.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "the original message was received");

            var serial = receivedMessages[0].Serial;

            // Update twice
            AssertSucceeded(
                await channel.UpdateMessageAsync(
                    new Message { Serial = serial, Data = "v2" },
                    new MessageOperation { Description = "first edit" }),
                "channel.updateMessage() first edit");
            AssertSucceeded(
                await channel.UpdateMessageAsync(
                    new Message { Serial = serial, Data = "v3" },
                    new MessageOperation { Description = "second edit" }),
                "channel.updateMessage() second edit");

            // The message store is eventually consistent: poll until the second update is
            // visible rather than sleeping a fixed interval before the HTTP-based reads (a
            // found message may still be a stale version). poll_until_success treats any
            // read error as "keep polling" - see the pseudocode conventions in uts/README.md.
            var msg = await UtsWait.PollUntilSuccess<Message>(
                async () =>
                {
                    var m = await channel.GetMessageAsync(serial);
                    if (m.Action == MessageAction.MessageUpdate && (string)m.Data == "v3")
                    {
                        return m;
                    }

                    return null;
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "getMessage returns the second update");

            // getMessageVersions — poll until the full version history is visible
            var versions = await UtsWait.PollUntilSuccess<PaginatedResult<Message>>(
                async () =>
                {
                    var result = await channel.GetMessageVersionsAsync(serial);
                    if (result.Items.Count >= 3)
                    {
                        return result;
                    }

                    return null;
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "getMessageVersions returns the full version history");

            // Assertions
            // getMessage returns the latest state
            // ASSERT msg IS Message: guaranteed by the declared type
            msg.Serial.Should().Be(serial);
            msg.Data.Should().Be("v3");
            msg.Action.Should().Be(MessageAction.MessageUpdate);

            // getMessageVersions returns history
            // ASSERT versions IS PaginatedResult: guaranteed by the declared type
            versions.Items.Count.Should().BeGreaterThanOrEqualTo(3); // original + 2 updates

            foreach (var item in versions.Items)
            {
                // ASSERT item IS Message: guaranteed by the declared type
                item.Serial.Should().Be(serial);
            }

            // Cleanup
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTAN1/annotation-publish-delete-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTAN1")]
        public async Task RTAN1_AnnotationPublishDelete()
        {
            // Setup
            var channelName = "mutable:rt-annotations-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(
                channelName,
                new ChannelOptions(modes: new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe, ChannelMode.AnnotationPublish, ChannelMode.AnnotationSubscribe)));
            var channelB = clientB.Channels.Get(
                channelName,
                new ChannelOptions(modes: new ChannelModes(ChannelMode.Subscribe, ChannelMode.AnnotationSubscribe)));

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // Subscribe to annotations on client B
            var receivedAnnotations = new CaptureList<Annotation>();
            channelB.Annotations.Subscribe(ann => receivedAnnotations.Add(ann));

            // Also subscribe to messages to capture the serial
            var receivedMessages = new CaptureList<Message>();
            channelA.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // Publish a message to annotate
            AssertSucceeded(await channelA.PublishAsync("annotatable", "content"), "channel_a.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "the message was received");

            var serial = receivedMessages[0].Serial;

            // Publish an annotation via realtime
            AssertSucceeded(
                await channelA.Annotations.PublishAsync(serial, new Annotation(type: "com.ably.reactions", name: "like")),
                "channel_a.annotations.publish()");

            // Wait for annotation to arrive on client B
            await UtsWait.PollUntil(
                () => receivedAnnotations.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the annotation");

            // Delete the annotation via realtime
            AssertSucceeded(
                await channelA.Annotations.DeleteAsync(serial, new Annotation(type: "com.ably.reactions", name: "like")),
                "channel_a.annotations.delete()");

            // Wait for delete event on client B
            await UtsWait.PollUntil(
                () => receivedAnnotations.Count >= 2,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received the annotation delete");

            // Assertions
            receivedAnnotations.Count.Should().Be(2);

            // Create event
            var createAnn = receivedAnnotations[0];
            createAnn.Action.Should().Be(AnnotationAction.Create);
            createAnn.Type.Should().Be("com.ably.reactions");
            createAnn.Name.Should().Be("like");
            createAnn.MessageSerial.Should().Be(serial);

            // Delete event
            var deleteAnn = receivedAnnotations[1];
            deleteAnn.Action.Should().Be(AnnotationAction.Delete);
            deleteAnn.Type.Should().Be("com.ably.reactions");
            deleteAnn.Name.Should().Be("like");
            deleteAnn.MessageSerial.Should().Be(serial);

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTAN4c/annotation-type-filtering-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTAN4c")]
        public async Task RTAN4c_AnnotationTypeFiltering()
        {
            // Setup
            var channelName = "mutable:rt-ann-filter-" + RandomId();

            var clientA = Realtime();

            var clientB = Realtime();

            clientA.Connect();
            clientB.Connect();

            await AwaitConnectionState(clientA.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(clientB.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channelA = clientA.Channels.Get(
                channelName,
                new ChannelOptions(modes: new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe, ChannelMode.AnnotationPublish, ChannelMode.AnnotationSubscribe)));
            var channelB = clientB.Channels.Get(
                channelName,
                new ChannelOptions(modes: new ChannelModes(ChannelMode.Subscribe, ChannelMode.AnnotationSubscribe)));

            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");

            // Subscribe only to "com.ably.reactions" type
            var filteredAnnotations = new CaptureList<Annotation>();
            channelB.Annotations.Subscribe("com.ably.reactions", ann => filteredAnnotations.Add(ann));

            // Also subscribe to all annotations to know when all have been delivered
            var allAnnotations = new CaptureList<Annotation>();
            channelB.Annotations.Subscribe(ann => allAnnotations.Add(ann));

            var receivedMessages = new CaptureList<Message>();
            channelA.Subscribe(msg => receivedMessages.Add(msg));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");

            // Test Steps
            // Publish a message
            AssertSucceeded(await channelA.PublishAsync("multi-type", "content"), "channel_a.publish()");

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "the message was received");

            var serial = receivedMessages[0].Serial;

            // Publish annotations of different types
            AssertSucceeded(
                await channelA.Annotations.PublishAsync(serial, new Annotation(type: "com.ably.reactions", name: "like")),
                "channel_a.annotations.publish() like");
            AssertSucceeded(
                await channelA.Annotations.PublishAsync(serial, new Annotation(type: "com.example.comments", name: "comment")),
                "channel_a.annotations.publish() comment");
            AssertSucceeded(
                await channelA.Annotations.PublishAsync(serial, new Annotation(type: "com.ably.reactions", name: "heart")),
                "channel_a.annotations.publish() heart");

            // Wait for all 3 annotations to arrive on client B (unfiltered listener)
            await UtsWait.PollUntil(
                () => allAnnotations.Count >= 3,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                description: "client B received all 3 annotations");

            // Assertions
            // Unfiltered listener got all 3
            allAnnotations.Count.Should().Be(3);

            // Filtered listener got only the 2 "com.ably.reactions" annotations
            filteredAnnotations.Count.Should().Be(2);
            filteredAnnotations[0].Type.Should().Be("com.ably.reactions");
            filteredAnnotations[0].Name.Should().Be("like");
            filteredAnnotations[1].Type.Should().Be("com.ably.reactions");
            filteredAnnotations[1].Name.Should().Be("heart");

            // Cleanup
            await CloseClient(clientA);
            await CloseClient(clientB);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTAN4d/annotation-implicit-attach-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTAN4d")]
        public async Task RTAN4d_AnnotationImplicitAttach()
        {
            // Setup
            var channelName = "mutable:rt-ann-implicit-attach-" + RandomId();

            var client = Realtime();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var channel = client.Channels.Get(
                channelName,
                new ChannelOptions(modes: new ChannelModes(ChannelMode.AnnotationSubscribe)));

            // Test Steps
            // Channel should be initialized (not attached)
            channel.State.Should().Be(ChannelState.Initialized);

            // Subscribe to annotations — should trigger implicit attach
            channel.Annotations.Subscribe(ann =>
            {
                // no-op
            });

            // Wait for channel to become attached
            await AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(10));

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // Cleanup
            await CloseClient(client);
        }
    }
}
