// Message Field Population from ProtocolMessage (TM2a, TM2c, TM2f)
// Derived from uts/realtime/unit/channels/message_field_population.md
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
    public class MessageFieldPopulationTests : UtsUnitTestBase
    {
        public MessageFieldPopulationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's shared Setup: the mock answers ATTACH with ATTACHED, and the channel's subscriber records every message
        private MockWebSocket BuildRig(string channelName, out PubSubRealtimeClient client, out IRealtimeChannel channel, out CaptureList<Message> receivedMessages)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });
            channel = client.Channels.Get(channelName);
            var received = new CaptureList<Message>();
            channel.Subscribe(msg => received.Add(msg));
            receivedMessages = received;
            return mockWs;
        }

        private async Task ConnectAndAttach(PubSubRealtimeClient client, IRealtimeChannel channel)
        {
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
        }

        private static JObject Msg(string channelName, string id, string connectionId, long? timestamp, params JObject[] messages)
        {
            var frame = ProtocolMessages.MessageProtocolMessage(channelName, new JArray(messages));
            if (id != null)
            {
                frame["id"] = id;
            }

            if (connectionId != null)
            {
                frame["connectionId"] = connectionId;
            }

            if (timestamp.HasValue)
            {
                frame["timestamp"] = timestamp.Value;
            }

            return frame;
        }

        // UTS: realtime/unit/TM2a/id-from-protocol-message-0
        [UtsFact]
        [Trait("spec", "TM2a")]
        public async Task TM2a_IdFromProtocolMessage()
        {
            // Setup
            var channelName = "test-TM2a-id-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Send a ProtocolMessage with 3 messages that have no id field.
            // The ProtocolMessage itself has id "connId:serial".
            mockWs.SendToClient(Msg(
                channelName,
                "abc123:5",
                "abc123",
                1700000000000,
                new JObject { ["name"] = "first", ["data"] = "a" },
                new JObject { ["name"] = "second", ["data"] = "b" },
                new JObject { ["name"] = "third", ["data"] = "c" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 3, description: "3 message(s) received");

            // Assertions
            // Each message id is computed as protocolMessageId:index
            receivedMessages[0].Id.Should().Be("abc123:5:0");
            receivedMessages[1].Id.Should().Be("abc123:5:1");
            receivedMessages[2].Id.Should().Be("abc123:5:2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2a/existing-id-not-overwritten-1
        [UtsFact]
        [Trait("spec", "TM2a")]
        public async Task TM2a_ExistingIdNotOverwritten()
        {
            // Setup
            var channelName = "test-TM2a-existing-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message already has its own id — should not be overwritten
            mockWs.SendToClient(Msg(
                channelName,
                "proto-id:0",
                null,
                null,
                new JObject { ["id"] = "my-custom-id", ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].Id.Should().Be("my-custom-id");
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2a/no-id-without-protocol-id-2
        // DEVIATION: see deviations.md
        [DeviationFact("TM2a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TM2a")]
        public async Task TM2a_NoIdWithoutProtocolId()
        {
            // Setup
            var channelName = "test-TM2a-no-proto-id-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // ProtocolMessage has no id field — messages should not get computed ids
            mockWs.SendToClient(Msg(
                channelName,
                null,
                "abc123",
                null,
                new JObject { ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].Id.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2c/connectionid-from-protocol-0
        [UtsFact]
        [Trait("spec", "TM2c")]
        public async Task TM2c_ConnectionidFromProtocol()
        {
            // Setup
            var channelName = "test-TM2c-connId-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message has no connectionId — should inherit from ProtocolMessage
            mockWs.SendToClient(Msg(
                channelName,
                "msg:0",
                "server-conn-xyz",
                null,
                new JObject { ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].ConnectionId.Should().Be("server-conn-xyz");
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2c/existing-connectionid-kept-1
        [UtsFact]
        [Trait("spec", "TM2c")]
        public async Task TM2c_ExistingConnectionidKept()
        {
            // Setup
            var channelName = "test-TM2c-existing-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message already has its own connectionId — should not be overwritten
            mockWs.SendToClient(Msg(
                channelName,
                "msg:0",
                "proto-conn",
                null,
                new JObject { ["connectionId"] = "msg-conn", ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].ConnectionId.Should().Be("msg-conn");
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2f/timestamp-from-protocol-0
        [UtsFact]
        [Trait("spec", "TM2f")]
        public async Task TM2f_TimestampFromProtocol()
        {
            // Setup
            var channelName = "test-TM2f-timestamp-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message has no timestamp — should inherit from ProtocolMessage
            mockWs.SendToClient(Msg(
                channelName,
                "msg:0",
                null,
                1700000000000,
                new JObject { ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2f/existing-timestamp-kept-1
        // DEVIATION: see deviations.md
        [DeviationFact("TM2f", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TM2f")]
        public async Task TM2f_ExistingTimestampKept()
        {
            // Setup
            var channelName = "test-TM2f-existing-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // Message already has its own timestamp — should not be overwritten
            mockWs.SendToClient(Msg(
                channelName,
                "msg:0",
                null,
                1700000000000,
                new JObject { ["timestamp"] = 1600000000000, ["name"] = "msg", ["data"] = "hello" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 1, description: "1 message(s) received");

            // Assertions
            receivedMessages[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1600000000000));
            await CloseClient(client);
        }

        // UTS: realtime/unit/TM2a/all-fields-populated-together-3
        [UtsFact]
        [Trait("spec", "TM2a")]
        public async Task TM2a_AllFieldsPopulatedTogether()
        {
            // Setup
            var channelName = "test-TM2-all-fields-" + Guid.NewGuid().ToString("N");

            var mockWs = BuildRig(channelName, out var client, out var channel, out var receivedMessages);

            // Test Steps
            await ConnectAndAttach(client, channel);

            // ProtocolMessage with all parent fields set, messages with none
            mockWs.SendToClient(Msg(
                channelName,
                "connId:7",
                "connId",
                1700000000000,
                new JObject { ["name"] = "first", ["data"] = "a" },
                new JObject { ["name"] = "second", ["data"] = "b" }));

            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "2 message(s) received");

            // Assertions
            // First message
            receivedMessages[0].Id.Should().Be("connId:7:0");
            receivedMessages[0].ConnectionId.Should().Be("connId");
            receivedMessages[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            receivedMessages[0].Name.Should().Be("first");
            receivedMessages[0].Data.Should().Be("a");

            // Second message — same connectionId and timestamp, different id index
            receivedMessages[1].Id.Should().Be("connId:7:1");
            receivedMessages[1].ConnectionId.Should().Be("connId");
            receivedMessages[1].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            receivedMessages[1].Name.Should().Be("second");
            receivedMessages[1].Data.Should().Be("b");
            await CloseClient(client);
        }
    }
}
