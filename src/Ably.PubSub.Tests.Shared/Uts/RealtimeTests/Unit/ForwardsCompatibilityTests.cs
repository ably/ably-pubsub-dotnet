// Forwards Compatibility Tests (RTF1, RSF1)
// Derived from uts/realtime/unit/connection/forwards_compatibility_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ForwardsCompatibilityTests : UtsUnitTestBase
    {
        public ForwardsCompatibilityTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/unit/RTF1/unrecognised-attributes-ignored-0
        [UtsFact]
        [Trait("spec", "RTF1")]
        public async Task RTF1_UnrecognisedAttributesIgnored()
        {
            // Setup
            var channelName = "test-RTF1-extra-attrs-" + Guid.NewGuid().ToString("N");
            var receivedMessages = new CaptureList<Message>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));
            var attachTask = channel.AttachAsync();

            // Respond to ATTACH request
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            var attachResult = await UtsWait.WithTimeout(attachTask, TimeSpan.FromSeconds(10), "attachTask");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Send a MESSAGE ProtocolMessage with extra unknown attributes.
            // The raw JSON includes fields that don't exist in the current spec.
            // The client must ignore these and process the message normally.
            mockWs.ActiveConnection.SendToClientRaw(new JObject
            {
                ["action"] = 15, // MESSAGE
                ["channel"] = channelName,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "test-event",
                        ["data"] = "hello",
                        ["serial"] = "msg-serial-1",
                    },
                },
                ["unknownField1"] = "some-future-value",
                ["unknownField2"] = 42,
                ["unknownNestedObject"] = new JObject { ["nestedKey"] = "nestedValue" },
                ["unknownArray"] = new JArray { 1, 2, 3 },
            });

            // Wait for the message to be delivered to the subscriber
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 1,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(100),
                description: "message delivered to the subscriber");

            // Assertions
            // Message was delivered successfully despite unknown fields
            receivedMessages.Count.Should().Be(1);
            receivedMessages[0].Name.Should().Be("test-event");
            receivedMessages[0].Data.Should().Be("hello");

            // Connection remains healthy
            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/unit/RTF1/unknown-action-handled-1
        [UtsFact]
        [Trait("spec", "RTF1")]
        public async Task RTF1_UnknownActionHandled()
        {
            // Setup
            var channelName = "test-RTF1-unknown-action-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Record connection state changes to detect unexpected disconnections
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Send a ProtocolMessage with an unknown action value.
            // Action 254 is not defined in the current spec.
            mockWs.ActiveConnection.SendToClientRaw(new JObject
            {
                ["action"] = 254,
                ["channel"] = channelName,
                ["unknownPayload"] = "future-feature-data",
            });

            // Send a normal HEARTBEAT to verify the connection is still processing messages
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.HeartbeatMessage());

            // Give the client time to process both messages
            // NOTE: spec poll_until(() => true, timeout: 1s) is a delivery wait; ProcessPendingEvents drains both frames
            await ProcessPendingEvents();

            // Assertions
            // Connection should still be CONNECTED - the unknown action was silently ignored
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // No unexpected state transitions occurred (only the initial connecting -> connected)
            UtsAssert.ContainsInOrder(stateChanges.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });

            // Verify no disconnected or failed states appeared
            stateChanges.Snapshot().Should().NotContain(ConnectionState.Disconnected);
            stateChanges.Snapshot().Should().NotContain(ConnectionState.Failed);

            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/unit/RSF1/message-unrecognised-attrs-0
        [UtsFact]
        [Trait("spec", "RSF1")]
        public async Task RSF1_MessageUnrecognisedAttrs()
        {
            // Setup
            var channelName = "test-RSF1-extra-attrs-" + Guid.NewGuid().ToString("N");
            var receivedMessages = new CaptureList<Message>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg => receivedMessages.Add(msg));
            var attachTask = channel.AttachAsync();

            // Respond to ATTACH request
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            var attachResult = await UtsWait.WithTimeout(attachTask, TimeSpan.FromSeconds(10), "attachTask");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Send a MESSAGE ProtocolMessage where the individual messages within
            // the messages array contain unknown fields. The ProtocolMessage itself
            // is well-formed, but the Message objects have extra attributes.
            mockWs.ActiveConnection.SendToClientRaw(new JObject
            {
                ["action"] = 15, // MESSAGE
                ["channel"] = channelName,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "event-1",
                        ["data"] = "payload-1",
                        ["serial"] = "serial-1",
                        ["futureField"] = "future-value",
                        ["futureNumber"] = 99,
                        ["futureObject"] = new JObject { ["nested"] = true },
                    },
                    new JObject
                    {
                        ["name"] = "event-2",
                        ["data"] = "payload-2",
                        ["serial"] = "serial-2",
                        ["anotherUnknownField"] = new JArray { 1, 2, 3 },
                    },
                },
            });

            // Wait for both messages to be delivered
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 2,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(100),
                description: "both messages delivered to the subscriber");

            // Assertions
            // Both messages were delivered successfully despite unknown fields
            receivedMessages.Count.Should().Be(2);

            // Known fields were correctly parsed
            receivedMessages[0].Name.Should().Be("event-1");
            receivedMessages[0].Data.Should().Be("payload-1");

            receivedMessages[1].Name.Should().Be("event-2");
            receivedMessages[1].Data.Should().Be("payload-2");

            // Connection and channel remain healthy
            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }
    }
}
