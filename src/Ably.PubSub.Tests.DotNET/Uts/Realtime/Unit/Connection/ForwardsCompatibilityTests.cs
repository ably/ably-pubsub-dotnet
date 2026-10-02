using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/forwards_compatibility_test.md in ably/specification.
    ///
    /// Spec points: RTF1, RSF1
    ///
    /// Four translation notes that apply to the whole file.
    ///
    /// The spec's send_to_client_raw needs no extra mock method here, and none of the workarounds its
    /// implementation note offers are required. Every template in <see cref="ProtocolMessages"/> is a
    /// JObject of wire names rather than a ProtocolMessage, so an unknown field on a known action -
    /// and an unknown action number entirely - reaches the SDK untouched. SendToClient with a JObject
    /// already is the spec's send_to_client_raw.
    ///
    /// Each ATTACHED is injected only once the client's ATTACH has been seen leaving the mock. The
    /// spec writes that step as "respond to ATTACH request"; injecting it eagerly would race the
    /// channel into ATTACHING, and ChannelMessageProcessor drops a MESSAGE for a channel that is not
    /// yet ATTACHED, so the test would hang on its own poll rather than fail informatively.
    ///
    /// The subscriber appends under a lock and the assertions read a snapshot of the list, because the
    /// handler runs on the workflow's own thread rather than the test's.
    ///
    /// The spec's CLOSE_CLIENT(client) is <see cref="UtsTestBase"/>'s teardown, which disposes every
    /// client it built - including when a test throws before reaching the end.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ForwardsCompatibilityTests : UtsTestBase
    {
        public ForwardsCompatibilityTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTF1/unrecognised-attributes-ignored-0
        [Fact]
        public async Task RTF1_UnrecognisedAttributesIgnored()
        {
            var channelName = "test-RTF1-extra-attrs-" + UtsSandbox.RandomId();
            var receivedMessages = new List<Message>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                }
            });
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            // Respond to the ATTACH request.
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["flags"] = 0 }));
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            // A MESSAGE whose ProtocolMessage carries fields that do not exist in the current spec.
            // The client must ignore them and process the message normally.
            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray
                {
                    new JObject
                    {
                        ["name"] = "test-event",
                        ["data"] = "hello",
                        ["serial"] = "msg-serial-1",
                    },
                },
                new Dictionary<string, JToken>
                {
                    ["unknownField1"] = "some-future-value",
                    ["unknownField2"] = 42,
                    ["unknownNestedObject"] = new JObject { ["nestedKey"] = "nestedValue" },
                    ["unknownArray"] = new JArray { 1, 2, 3 },
                }));

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(receivedMessages).Count >= 1,
                "the message to be delivered to the subscriber");

            // The message was delivered despite the unknown fields.
            var received = UtsClients.Snapshot(receivedMessages);
            received.Should().HaveCount(1);
            received[0].Name.Should().Be("test-event");
            received[0].Data.Should().Be("hello");

            // The connection remains healthy.
            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTF1/unknown-action-handled-1
        [Fact]
        public async Task RTF1_UnknownActionHandled()
        {
            var channelName = "test-RTF1-unknown-action-" + UtsSandbox.RandomId();

            // maxIdleInterval 0 disables the transport's idle timer. This test advances the injected
            // clock, and the idle check measures elapsed time against that same clock, so a live idle
            // timer sees the jump as silence and drops the connection - which is a DISCONNECTED the
            // test then (correctly) reports as an unexpected transition.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key",
                        maxIdleInterval: 0)));

            var clock = new TestClock();
            var client = RealtimeClient(mockWs, clock: clock);

            // The spec's connection.on(...) recorder, registered before connecting: CONNECTING is
            // transient and is gone by the time the first await returns.
            var stateChanges = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec gives the client time to process both messages with
            // poll_until(() => true, timeout: 1s) - a settling window rather than a premise, and the
            // unit tier never waits on real time. The observable premise is that both messages have
            // been through the receive pipeline: RealtimeWorkflow.ProcessMessage stamps
            // Connection.ConfirmedAliveAt from the injected clock for every inbound message, and the
            // workflow is a single reader, so the stamp reaching the advanced instant proves the
            // HEARTBEAT and the unknown action queued ahead of it were both processed.
            clock.Advance(1000);
            var aliveAfterAdvance = clock.Now;

            // Action 254 is not defined in the current spec.
            mockWs.SendToClient(new JObject
            {
                ["action"] = 254,
                ["channel"] = channelName,
                ["unknownPayload"] = "future-feature-data",
            });

            // A normal HEARTBEAT, to verify the connection is still processing messages.
            mockWs.SendToClient(ProtocolMessages.HeartbeatMessage());

            await UtsClients.PollUntil(
                () => client.Connection.ConfirmedAliveAt == aliveAfterAdvance,
                "the client to process the unknown action and the HEARTBEAT behind it");

            // The connection should still be CONNECTED - the unknown action was silently ignored.
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // No unexpected state transitions occurred (only the initial connecting -> connected).
            var observed = UtsClients.Snapshot(stateChanges);
            UtsClients.ContainsInOrder(observed, ConnectionState.Connecting, ConnectionState.Connected)
                .Should().BeTrue("the recorded states should open with connecting then connected");

            observed.Should().NotContain(ConnectionState.Disconnected);
            observed.Should().NotContain(ConnectionState.Failed);
        }

        // UTS: realtime/unit/RSF1/message-unrecognised-attrs-0
        [Fact]
        public async Task RSF1_MessageUnrecognisedAttrs()
        {
            var channelName = "test-RSF1-extra-attrs-" + UtsSandbox.RandomId();
            var receivedMessages = new List<Message>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Subscribe(msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                }
            });
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            // Respond to the ATTACH request.
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["flags"] = 0 }));
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            // The ProtocolMessage itself is well-formed; it is the Message objects inside the messages
            // array that carry unknown fields.
            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray
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
                }));

            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(receivedMessages).Count >= 2,
                "both messages to be delivered to the subscriber");

            // Both messages were delivered despite the unknown fields.
            var received = UtsClients.Snapshot(receivedMessages);
            received.Should().HaveCount(2);

            // The known fields were parsed correctly.
            received[0].Name.Should().Be("event-1");
            received[0].Data.Should().Be("payload-1");

            received[1].Name.Should().Be("event-2");
            received[1].Data.Should().Be("payload-2");

            // The connection and the channel remain healthy.
            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);
        }
    }
}
