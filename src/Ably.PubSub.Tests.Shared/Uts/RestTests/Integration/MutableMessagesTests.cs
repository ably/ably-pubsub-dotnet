// REST Mutable Messages Integration Tests (RSL1n, RSL11, RSL14, RSL15, RSAN1, RSAN2, RSAN3)
// Derived from uts/rest/integration/mutable_messages.md
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

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class MutableMessagesTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        // The skill's integration defaults for a poll that states no timeout or interval.
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(15);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        public MutableMessagesTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The specs' full_access_key (app_config.keys[0].key_str).
        private string FullAccessKey => App.Keys[0].KeyStr;

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1n/publish-returns-serials-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1n")]
        public async Task RSL1n_PublishReturnsSerials()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL1n-serials-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps and Assertions
            // Single message
            var result1 = await channel.PublishAsync("event1", "data1");

            // ASSERT result1 IS PublishResult: guaranteed at compile time (PublishAsync returns PublishResult)
            result1.Should().NotBeNull();

            // ASSERT result1.serials IS List: guaranteed at compile time (Serials is an IReadOnlyList<string>)
            result1.Serials.Should().NotBeNull();
            result1.Serials.Should().HaveCount(1);

            // ASSERT result1.serials[0] IS String: guaranteed at compile time (string elements)
            result1.Serials[0].Should().NotBeNull();
            result1.Serials[0].Length.Should().BeGreaterThan(0);

            // Multiple messages
            var result2 = await channel.PublishAsync(new[]
            {
                new Message("event2", "data2"),
                new Message("event3", "data3"),
                new Message("event4", "data4"),
            });
            result2.Serials.Should().HaveCount(3);

            // ALL serial IN result2.serials: serial IS String (compile-time) AND serial.length > 0
            result2.Serials.Should().NotContain(serial => !(serial != null && serial.Length > 0));

            // Serials should be unique
            result2.Serials[0].Should().NotBe(result2.Serials[1]);
            result2.Serials[1].Should().NotBe(result2.Serials[2]);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL11/get-message-by-serial-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL11")]
        public async Task RSL11_GetMessageBySerial()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL11-getMessage-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish a message and get its serial
            var publishResult = await channel.PublishAsync("test-event", "hello world");
            var serial = publishResult.Serials[0];

            // Retrieve the message by serial (polled: getMessage throws not-found until the
            // just-published message is visible in the eventually-consistent store)
            var msg = await UtsWait.PollUntilSuccess(
                () => channel.GetMessageAsync(serial),
                timeout: PollTimeout,
                interval: PollInterval,
                description: "getMessage(serial)");

            // Assertions
            // ASSERT msg IS Message: guaranteed at compile time (GetMessageAsync returns Message)
            msg.Name.Should().Be("test-event");
            msg.Data.Should().Be("hello world");
            msg.Serial.Should().Be(serial);
            msg.Action.Should().Be(MessageAction.MessageCreate);
            msg.Timestamp.Should().NotBeNull();
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL15/update-message-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL15")]
        public async Task RSL15_UpdateMessage()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL15-update-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish original message
            var publishResult = await channel.PublishAsync("original", "original-data");
            var serial = publishResult.Serials[0];

            // Update the message
            var updateResult = await channel.UpdateMessageAsync(
                new Message("updated", "updated-data") { Serial = serial },
                new MessageOperation { Description = "edited content" });

            // Assertions
            // Update returns a version serial
            // ASSERT update_result IS UpdateDeleteResult: guaranteed at compile time (UpdateMessageAsync returns UpdateDeleteResult)
            // ASSERT update_result.versionSerial IS String: guaranteed at compile time (VersionSerial is a string)
            updateResult.VersionSerial.Should().NotBeNull();
            updateResult.VersionSerial.Length.Should().BeGreaterThan(0);

            // Verify via getMessage — poll until the update is visible
            var updatedMsg = await UtsWait.PollUntilSuccess(
                async () =>
                {
                    var msg = await channel.GetMessageAsync(serial);
                    if (msg.Action == MessageAction.MessageUpdate)
                    {
                        return msg;
                    }

                    return null;
                },
                timeout: PollTimeout,
                interval: PollInterval,
                description: "getMessage(serial) shows the update");
            updatedMsg.Name.Should().Be("updated");
            updatedMsg.Data.Should().Be("updated-data");
            updatedMsg.Action.Should().Be(MessageAction.MessageUpdate);
            updatedMsg.Version.Should().NotBeNull();
            updatedMsg.Version.Description.Should().Be("edited content");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL15/delete-message-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL15")]
        public async Task RSL15_DeleteMessage()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL15-delete-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish original message
            var publishResult = await channel.PublishAsync("to-delete", "delete-me");
            var serial = publishResult.Serials[0];

            // Delete the message
            var deleteResult = await channel.DeleteMessageAsync(new Message { Serial = serial });

            // Assertions
            // ASSERT delete_result IS UpdateDeleteResult: guaranteed at compile time (DeleteMessageAsync returns UpdateDeleteResult)
            // ASSERT delete_result.versionSerial IS String: guaranteed at compile time (VersionSerial is a string)
            deleteResult.VersionSerial.Should().NotBeNull();
            deleteResult.VersionSerial.Length.Should().BeGreaterThan(0);

            // Verify via getMessage — poll until the delete is visible
            var deletedMsg = await UtsWait.PollUntilSuccess(
                async () =>
                {
                    var msg = await channel.GetMessageAsync(serial);
                    if (msg.Action == MessageAction.MessageDelete)
                    {
                        return msg;
                    }

                    return null;
                },
                timeout: PollTimeout,
                interval: PollInterval,
                description: "getMessage(serial) shows the delete");
            deletedMsg.Action.Should().Be(MessageAction.MessageDelete);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL14/get-message-versions-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL14")]
        public async Task RSL14_GetMessageVersions()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL14-versions-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish original
            var publishResult = await channel.PublishAsync("versioned", "v1");
            var serial = publishResult.Serials[0];

            // Update twice
            await channel.UpdateMessageAsync(
                new Message { Serial = serial, Data = "v2" },
                new MessageOperation { Description = "first edit" });
            await channel.UpdateMessageAsync(
                new Message { Serial = serial, Data = "v3" },
                new MessageOperation { Description = "second edit" });

            // Poll version history until all versions appear
            var versions = await UtsWait.PollUntilSuccess(
                async () =>
                {
                    var result = await channel.GetMessageVersionsAsync(serial);
                    if (result.Items.Count >= 3)
                    {
                        return result;
                    }

                    return null;
                },
                timeout: PollTimeout,
                interval: PollInterval,
                description: "getMessageVersions(serial) has 3 items");

            // Assertions
            // ASSERT versions IS PaginatedResult: guaranteed at compile time (GetMessageVersionsAsync returns PaginatedResult<Message>)
            versions.Items.Count.Should().BeGreaterThanOrEqualTo(3); // Original + 2 updates

            // All items should be Messages with the same serial
            foreach (var item in versions.Items)
            {
                // ASSERT item IS Message: guaranteed at compile time (Items is a List<Message>)
                item.Serial.Should().Be(serial);
            }
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL15/append-message-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL15")]
        public async Task RSL15_AppendMessage()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSL15-append-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish original
            var publishResult = await channel.PublishAsync("appendable", "original");
            var serial = publishResult.Serials[0];

            // Append to the message
            var appendResult = await channel.AppendMessageAsync(
                new Message { Serial = serial, Data = "appended-data" },
                new MessageOperation { Description = "appended content" });

            // Assertions
            // ASSERT append_result IS UpdateDeleteResult: guaranteed at compile time (AppendMessageAsync returns UpdateDeleteResult)
            // ASSERT append_result.versionSerial IS String: guaranteed at compile time (VersionSerial is a string)
            appendResult.VersionSerial.Should().NotBeNull();
            appendResult.VersionSerial.Length.Should().BeGreaterThan(0);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSAN1/annotation-lifecycle-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSAN1")]
        public async Task RSAN1_AnnotationLifecycle()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSAN-lifecycle-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish a message to annotate
            var publishResult = await channel.PublishAsync("annotatable", "content");
            var serial = publishResult.Serials[0];

            // Create an annotation
            await channel.Annotations.PublishAsync(serial, new Annotation("com.ably.reactions", "like"));

            // Verify annotation exists — poll until it appears
            var annotations = await UtsWait.PollUntilSuccess(
                async () =>
                {
                    var result = await channel.Annotations.GetAsync(serial);
                    if (result.Items.Count >= 1)
                    {
                        return result;
                    }

                    return null;
                },
                timeout: PollTimeout,
                interval: PollInterval,
                description: "annotations.get(serial) has 1 item");
            annotations.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            var found = false;
            foreach (var ann in annotations.Items)
            {
                if (ann.Type == "com.ably.reactions" && ann.Name == "like")
                {
                    found = true;
                    ann.Action.Should().Be(AnnotationAction.Create);
                    ann.MessageSerial.Should().Be(serial);
                }
            }

            found.Should().BeTrue();

            // Delete the annotation
            await channel.Annotations.DeleteAsync(serial, new Annotation("com.ably.reactions", "like"));
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSAN3/get-annotations-paginated-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSAN3")]
        public async Task RSAN3_GetAnnotationsPaginated()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "mutable:test-RSAN3-paginated-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish a message
            var publishResult = await channel.PublishAsync("multi-annotated", "content");
            var serial = publishResult.Serials[0];

            // Publish multiple annotations
            await channel.Annotations.PublishAsync(serial, new Annotation("com.ably.reactions", "like"));
            await channel.Annotations.PublishAsync(serial, new Annotation("com.ably.reactions", "heart"));

            // Retrieve annotations — poll until both appear
            var result = await UtsWait.PollUntilSuccess(
                async () =>
                {
                    var r = await channel.Annotations.GetAsync(serial);
                    if (r.Items.Count >= 2)
                    {
                        return r;
                    }

                    return null;
                },
                timeout: PollTimeout,
                interval: PollInterval,
                description: "annotations.get(serial) has 2 items");

            // Assertions
            // ASSERT result IS PaginatedResult: guaranteed at compile time (GetAsync returns PaginatedResult<Annotation>)
            result.Items.Count.Should().BeGreaterThanOrEqualTo(2);

            foreach (var ann in result.Items)
            {
                // ASSERT ann IS Annotation: guaranteed at compile time (Items is a List<Annotation>)
                ann.MessageSerial.Should().Be(serial);
                ann.Type.Should().Be("com.ably.reactions");
                ann.Timestamp.Should().NotBeNull();
            }
        }
    }
}
