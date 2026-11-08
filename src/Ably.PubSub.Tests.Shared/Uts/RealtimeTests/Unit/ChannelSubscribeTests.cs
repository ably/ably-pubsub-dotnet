// RealtimeChannel Subscribe and Unsubscribe Tests (RTL7, RTL7a, RTL7b, RTL7g, RTL7h, RTL7f, RTL8, RTL8a, RTL8b, RTL8c, RTL17, RTL22, MFI1, MFI2)
// Derived from uts/realtime/unit/channels/channel_subscribe.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: RTL7f is adapted as the spec's implementation note allows (this SDK delegates echo suppression to the server through the echo=false connection parameter); attachOnSubscribe: false used as setup only is dropped, see the NOTEs; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelSubscribeTests : UtsUnitTestBase
    {
        public ChannelSubscribeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's recurring mock: CONNECTED on connect and ATTACHED for every ATTACH
        private MockWebSocket AttachingMock(string channelName, Action<ProtocolMessage> onAttach = null, JObject connected = null)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(connected ?? ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        onAttach?.Invoke(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);
            return mockWs;
        }

        // The spec's ProtocolMessage(action: MESSAGE, channel:, messages: [...]) sent on the active connection
        private static void SendMessages(MockWebSocket mockWs, string channelName, params JObject[] messages)
        {
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.MessageProtocolMessage(channelName, new JArray(messages)));
        }

        private async Task ConnectAndAttach(PubSubRealtimeClient client, IRealtimeChannel channel)
        {
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
        }

        private static JObject M(string name, string data)
        {
            var message = new JObject { ["data"] = data };
            if (name != null)
            {
                message["name"] = name;
            }

            return message;
        }

        private PubSubRealtimeClient NewClient(ClientOptions options = null)
        {
            return Realtime(options ?? new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
        }

        // UTS: realtime/unit/RTL7a/subscribe-all-messages-0
        [UtsFact]
        [Trait("spec", "RTL7a")]
        public async Task RTL7a_SubscribeAllMessages()
        {
            // Setup
            var channelName = "test-RTL7a-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Server sends messages with different names
            SendMessages(mockWs, channelName, M("event1", "data1"));
            SendMessages(mockWs, channelName, M("event2", "data2"));
            SendMessages(mockWs, channelName, M(null, "data3"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            receivedMessages.Count.Should().Be(3);
            receivedMessages[0].Name.Should().Be("event1");
            receivedMessages[0].Data.Should().Be("data1");
            receivedMessages[1].Name.Should().Be("event2");
            receivedMessages[1].Data.Should().Be("data2");
            receivedMessages[2].Name.Should().BeNull();
            receivedMessages[2].Data.Should().Be("data3");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7a/multiple-messages-per-protocol-1
        [UtsFact]
        [Trait("spec", "RTL7a")]
        public async Task RTL7a_MultipleMessagesPerProtocol()
        {
            // Setup
            var channelName = "test-RTL7a-multi-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Server sends a single ProtocolMessage with multiple messages
            SendMessages(mockWs, channelName, M("batch1", "first"), M("batch2", "second"), M("batch3", "third"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            receivedMessages.Count.Should().Be(3);
            receivedMessages[0].Name.Should().Be("batch1");
            receivedMessages[1].Name.Should().Be("batch2");
            receivedMessages[2].Name.Should().Be("batch3");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7b/name-filtered-subscribe-0
        [UtsFact]
        [Trait("spec", "RTL7b")]
        public async Task RTL7b_NameFilteredSubscribe()
        {
            // Setup
            var channelName = "test-RTL7b-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe("target", message =>
            {
                receivedMessages.Add(message);
            });

            // Server sends messages with different names
            SendMessages(mockWs, channelName, M("other", "should-not-receive"));
            SendMessages(mockWs, channelName, M("target", "should-receive"));
            SendMessages(mockWs, channelName, M(null, "no-name-should-not-receive"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            receivedMessages.Count.Should().Be(1);
            receivedMessages[0].Name.Should().Be("target");
            receivedMessages[0].Data.Should().Be("should-receive");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7b/multiple-name-subscriptions-1
        [UtsFact]
        [Trait("spec", "RTL7b")]
        public async Task RTL7b_MultipleNameSubscriptions()
        {
            // Setup
            var channelName = "test-RTL7b-multi-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var alphaMessages = new CaptureList<Message>();
            var betaMessages = new CaptureList<Message>();

            channel.Subscribe("alpha", message =>
            {
                alphaMessages.Add(message);
            });

            channel.Subscribe("beta", message =>
            {
                betaMessages.Add(message);
            });

            SendMessages(mockWs, channelName, M("alpha", "a1"), M("beta", "b1"), M("alpha", "a2"), M("gamma", "g1"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            alphaMessages.Count.Should().Be(2);
            alphaMessages[0].Data.Should().Be("a1");
            alphaMessages[1].Data.Should().Be("a2");

            betaMessages.Count.Should().Be(1);
            betaMessages[0].Data.Should().Be("b1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7g/implicit-attach-initialized-0
        [UtsFact]
        [Trait("spec", "RTL7g")]
        public async Task RTL7g_ImplicitAttachInitialized()
        {
            // Setup
            var channelName = "test-RTL7g-" + Guid.NewGuid().ToString("N");

            var attachMessageCount = 0;

            var mockWs = AttachingMock(channelName, onAttach: msg => Interlocked.Increment(ref attachMessageCount));

            var client = NewClient();

            // Default attachOnSubscribe is true
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Wait for implicit attach to complete
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Verify the listener was registered by sending a message
            SendMessages(mockWs, channelName, M("test", "hello"));
            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();
            receivedMessages.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7g/implicit-attach-detached-1
        [UtsFact]
        [Trait("spec", "RTL7g")]
        public async Task RTL7g_ImplicitAttachDetached()
        {
            // Setup
            var channelName = "test-RTL7g-detached-" + Guid.NewGuid().ToString("N");

            var attachMessageCount = 0;

            AttachingMock(channelName, onAttach: msg => Interlocked.Increment(ref attachMessageCount));

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Subscribe should trigger implicit attach from DETACHED
            channel.Subscribe(message =>
            {
            });

            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7g/listener-registered-attach-fails-2
        [UtsFact]
        [Trait("spec", "RTL7g")]
        public async Task RTL7g_ListenerRegisteredAttachFails()
        {
            // Setup
            var channelName = "test-RTL7g-fail-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Fail the attach with a channel error
                        mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted", 401));
                    }
                });
            InstallMock(mockWs);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Wait for the channel to enter FAILED from the rejected attach
            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Verify the listener was registered despite the failed attach.
            // Re-attach the channel so messages can flow.
            // First, reset mock to succeed on attach:
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
            };
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            SendMessages(mockWs, channelName, M("test", "after-reattach"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            receivedMessages.Count.Should().Be(1);
            receivedMessages[0].Data.Should().Be("after-reattach");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7h/no-attach-on-subscribe-0
        [UtsFact(Skip = "Missing SDK public API: ChannelOptions#attachOnSubscribe (TB4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL7h")]
        public async Task RTL7h_NoAttachOnSubscribe()
        {
            // NOTE: attachOnSubscribe channel option (TB4, RTL7h) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL7h-${random_id()}"
            // attach_message_count = 0
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       attach_message_count++
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //
            // ASSERT channel.state == ChannelState.initialized
            //
            // channel.subscribe((message) => {})

            // # Channel should remain INITIALIZED — no attach triggered
            // ASSERT channel.state == ChannelState.initialized
            // ASSERT attach_message_count == 0
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL7g/no-attach-when-attached-3
        [UtsFact]
        [Trait("spec", "RTL7g")]
        public async Task RTL7g_NoAttachWhenAttached()
        {
            // Setup
            var channelName = "test-RTL7g-already-" + Guid.NewGuid().ToString("N");

            var attachMessageCount = 0;

            AttachingMock(channelName, onAttach: msg => Interlocked.Increment(ref attachMessageCount));

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Subscribe on already-attached channel — no additional attach
            channel.Subscribe(message =>
            {
            });

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7g/no-attach-when-attaching-4
        [UtsFact]
        [Trait("spec", "RTL7g")]
        public async Task RTL7g_NoAttachWhenAttaching()
        {
            // Setup
            var channelName = "test-RTL7g-attaching-" + Guid.NewGuid().ToString("N");

            var attachMessageCount = 0;

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Increment(ref attachMessageCount);

                        // Don't respond yet — leave channel in ATTACHING
                    }
                });
            InstallMock(mockWs);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't complete it
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // NOTE: the SDK enters ATTACHING before its ATTACH reaches the mock, so the queued send is drained before the count is read
            await ProcessPendingEvents();
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Subscribe while attaching — should not trigger another attach
            channel.Subscribe(message =>
            {
            });

            // NOTE: drained so an ATTACH the subscribe might queue reaches the mock before the negative check
            await ProcessPendingEvents();

            // Assertions
            channel.State.Should().Be(ChannelState.Attaching);

            // No additional ATTACH message sent
            Volatile.Read(ref attachMessageCount).Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL17/no-delivery-when-not-attached-0
        [UtsFact]
        [Trait("spec", "RTL17")]
        public async Task RTL17_NoDeliveryWhenNotAttached()
        {
            // Setup
            var channelName = "test-RTL17-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches implicitly here, but the test attaches explicitly right after and the mock never answers, so the channel is ATTACHING either way and the outcome is unchanged
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Don't respond — leave channel in ATTACHING
                    }
                });
            InstallMock(mockWs);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Start attach but don't complete it — channel stays ATTACHING
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Server sends a message while channel is still ATTACHING
            SendMessages(mockWs, channelName, M("premature", "should-not-deliver"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            // Message should not have been delivered
            receivedMessages.Count.Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL7f/no-echo-messages-0
        [UtsFact]
        [Trait("spec", "RTL7f")]
        public async Task RTL7f_NoEchoMessages()
        {
            // Setup
            var channelName = "test-RTL7f-" + Guid.NewGuid().ToString("N");
            var connectionId = "conn-self-123";

            var mockWs = AttachingMock(
                channelName,
                connected: ProtocolMessages.ConnectedMessage(connectionId, "key-456"));

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var client = NewClient(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                EchoMessages = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(message =>
            {
                receivedMessages.Add(message);
            });

            // Assertions
            // NOTE: the spec's implementation note allows adapting to server-side echo suppression: this SDK sends echo=false on the connection URL and does not filter by connectionId, so the echo parameter is asserted instead of client-side filtering
            // Server echoes back a message with this connection's connectionId
            // ProtocolMessage(action: MESSAGE, channel: channel_name, connectionId: connection_id, messages: [Message(name: "echo", data: "from-self")])
            // Server sends a message from a different connection
            // ProtocolMessage(action: MESSAGE, channel: channel_name, connectionId: "conn-other-789", messages: [Message(name: "remote", data: "from-other")])
            // Only the message from the other connection should be delivered
            // ASSERT length(received_messages) == 1
            // ASSERT received_messages[0].name == "remote"
            // ASSERT received_messages[0].data == "from-other"
            mockWs.LastConnectUrl.QueryParams["echo"].Should().Be("false");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL8a/unsubscribe-specific-listener-0
        [UtsFact]
        [Trait("spec", "RTL8a")]
        public async Task RTL8a_UnsubscribeSpecificListener()
        {
            // Setup
            var channelName = "test-RTL8a-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var messagesA = new CaptureList<Message>();
            var messagesB = new CaptureList<Message>();

            Action<Message> listenerA = message => messagesA.Add(message);
            Action<Message> listenerB = message => messagesB.Add(message);

            channel.Subscribe(listenerA);
            channel.Subscribe(listenerB);

            // Both listeners receive first message
            SendMessages(mockWs, channelName, M("msg1", "first"));
            await UtsWait.PollUntil(() => messagesA.Count == 1 && messagesB.Count == 1, description: "both listeners received the first message");

            messagesA.Count.Should().Be(1);
            messagesB.Count.Should().Be(1);

            // Unsubscribe listener_a
            channel.Unsubscribe(listenerA);

            // Only listener_b should receive second message
            SendMessages(mockWs, channelName, M("msg2", "second"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            // Did not receive second message
            messagesA.Count.Should().Be(1);

            // Received both messages
            messagesB.Count.Should().Be(2);
            messagesB[1].Name.Should().Be("msg2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL8b/unsubscribe-named-listener-0
        [UtsFact]
        [Trait("spec", "RTL8b")]
        public async Task RTL8b_UnsubscribeNamedListener()
        {
            // Setup
            var channelName = "test-RTL8b-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            Action<Message> listener = message => receivedMessages.Add(message);

            // Subscribe to two different names with the same listener
            channel.Subscribe("alpha", listener);
            channel.Subscribe("beta", listener);

            // Both subscriptions active
            SendMessages(mockWs, channelName, M("alpha", "a1"), M("beta", "b1"));
            await UtsWait.PollUntil(() => receivedMessages.Count == 2, description: "both subscriptions received a message");
            receivedMessages.Count.Should().Be(2);

            // Unsubscribe only from "alpha"
            channel.Unsubscribe("alpha", listener);

            SendMessages(mockWs, channelName, M("alpha", "a2"), M("beta", "b2"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            // "alpha" unsubscribed but "beta" still active
            receivedMessages.Count.Should().Be(3);
            receivedMessages[2].Name.Should().Be("beta");
            receivedMessages[2].Data.Should().Be("b2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL8c/unsubscribe-all-listeners-0
        [UtsFact]
        [Trait("spec", "RTL8c")]
        public async Task RTL8c_UnsubscribeAllListeners()
        {
            // Setup
            var channelName = "test-RTL8c-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var messagesAll = new CaptureList<Message>();
            var messagesNamed = new CaptureList<Message>();

            channel.Subscribe(message => messagesAll.Add(message));
            channel.Subscribe("specific", message => messagesNamed.Add(message));

            // Both listeners receive
            SendMessages(mockWs, channelName, M("specific", "first"));
            await UtsWait.PollUntil(() => messagesAll.Count == 1 && messagesNamed.Count == 1, description: "both listeners received the first message");
            messagesAll.Count.Should().Be(1);
            messagesNamed.Count.Should().Be(1);

            // Unsubscribe all
            channel.Unsubscribe();

            // No listeners should receive
            SendMessages(mockWs, channelName, M("specific", "second"), M("other", "third"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            // No new messages
            messagesAll.Count.Should().Be(1);

            // No new messages
            messagesNamed.Count.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL8a/unsubscribe-noop-not-subscribed-1
        [UtsFact]
        [Trait("spec", "RTL8a")]
        public async Task RTL8a_UnsubscribeNoopNotSubscribed()
        {
            // Setup
            var channelName = "test-RTL8a-noop-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var receivedMessages = new CaptureList<Message>();
            Action<Message> activeListener = message => receivedMessages.Add(message);
            Action<Message> unusedListener = message =>
            {
            };

            channel.Subscribe(activeListener);

            // Unsubscribe a listener that was never subscribed — should be no-op
            channel.Unsubscribe(unusedListener);

            SendMessages(mockWs, channelName, M("test", "still-works"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            // Existing subscription should be unaffected
            receivedMessages.Count.Should().Be(1);
            receivedMessages[0].Data.Should().Be("still-works");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL22a/filter-matching-name-0
        [UtsFact]
        [Trait("spec", "RTL22a")]
        public async Task RTL22a_FilterMatchingName()
        {
            // Setup
            var channelName = "test-RTL22a-name-" + Guid.NewGuid().ToString("N");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var mockWs = AttachingMock(channelName);

            var client = NewClient();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await ConnectAndAttach(client, channel);

            var filteredMessages = new CaptureList<Message>();

            // DEVIATION: RealtimeChannel#subscribe(MessageFilter) absent; using channel.Subscribe("target-event", listener)
            // filter = MessageFilter(name: "target-event")
            // channel.subscribe(filter, (message) => {
            channel.Subscribe("target-event", message =>
            {
                filteredMessages.Add(message);
            });

            // Server sends messages with different names
            SendMessages(mockWs, channelName, M("target-event", "match-1"));
            SendMessages(mockWs, channelName, M("other-event", "no-match"));
            SendMessages(mockWs, channelName, M("target-event", "match-2"));
            SendMessages(mockWs, channelName, M(null, "no-name"));

            // NOTE: delivery to subscribers is asynchronous; ProcessPendingEvents drains it before the assertions
            await ProcessPendingEvents();

            // Assertions
            filteredMessages.Count.Should().Be(2);
            filteredMessages[0].Name.Should().Be("target-event");
            filteredMessages[0].Data.Should().Be("match-1");
            filteredMessages[1].Name.Should().Be("target-event");
            filteredMessages[1].Data.Should().Be("match-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL22a/filter-matching-ref-timeserial-1
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#subscribe(MessageFilter) (RTL22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL22a")]
        public async Task RTL22a_FilterMatchingRefTimeserial()
        {
            // NOTE: subscribe(MessageFilter, ...) (RTL22) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL22a-ref-timeserial-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            //
            // filtered_messages = []
            // filter = MessageFilter(refTimeserial: "abc123@1700000000000-0")
            // channel.subscribe(filter, (message) => {
            //   filtered_messages.append(message)
            // })
            //
            // # Message with matching extras.ref.timeserial
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "reply", data: "match", extras: {
            //       "ref": {"timeserial": "abc123@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))
            //
            // # Message with different extras.ref.timeserial
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "reply", data: "no-match", extras: {
            //       "ref": {"timeserial": "xyz789@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))
            //
            // # Message with no extras.ref at all
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "plain", data: "no-ref")
            //   ]
            // ))
            //
            // # Another message with matching extras.ref.timeserial
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "reaction", data: "match-2", extras: {
            //       "ref": {"timeserial": "abc123@1700000000000-0", "type": "com.ably.reaction"}
            //     })
            //   ]
            // ))

            // ASSERT length(filtered_messages) == 2
            // ASSERT filtered_messages[0].data == "match"
            // ASSERT filtered_messages[1].data == "match-2"
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL22b/filter-isref-false-0
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#subscribe(MessageFilter) (RTL22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL22b")]
        public async Task RTL22b_FilterIsrefFalse()
        {
            // NOTE: subscribe(MessageFilter, ...) (RTL22) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL22b-isref-false-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            //
            // filtered_messages = []
            // filter = MessageFilter(isRef: false)
            // channel.subscribe(filter, (message) => {
            //   filtered_messages.append(message)
            // })
            //
            // # Message WITHOUT extras.ref (no extras at all)
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "plain", data: "no-extras")
            //   ]
            // ))
            //
            // # Message WITH extras.ref — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "reply", data: "has-ref", extras: {
            //       "ref": {"timeserial": "abc123@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))
            //
            // # Message with extras but no ref field — should be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "annotated", data: "extras-no-ref", extras: {
            //       "headers": {"custom-key": "custom-value"}
            //     })
            //   ]
            // ))
            //
            // # Another message WITH extras.ref — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "reaction", data: "also-has-ref", extras: {
            //       "ref": {"timeserial": "xyz789@1700000000000-0", "type": "com.ably.reaction"}
            //     })
            //   ]
            // ))

            // # Only messages without extras.ref should be delivered
            // ASSERT length(filtered_messages) == 2
            // ASSERT filtered_messages[0].name == "plain"
            // ASSERT filtered_messages[0].data == "no-extras"
            // ASSERT filtered_messages[1].name == "annotated"
            // ASSERT filtered_messages[1].data == "extras-no-ref"
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL22c/filter-multiple-criteria-0
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#subscribe(MessageFilter) (RTL22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL22c")]
        public async Task RTL22c_FilterMultipleCriteria()
        {
            // NOTE: subscribe(MessageFilter, ...) (RTL22) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL22c-multi-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            //
            // filtered_messages = []
            // filter = MessageFilter(name: "comment", refType: "com.ably.reply")
            // channel.subscribe(filter, (message) => {
            //   filtered_messages.append(message)
            // })
            //
            // # Message matching BOTH name AND refType — should be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "comment", data: "both-match", extras: {
            //       "ref": {"timeserial": "abc@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))
            //
            // # Message matching name but NOT refType — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "comment", data: "name-only", extras: {
            //       "ref": {"timeserial": "def@1700000000000-0", "type": "com.ably.reaction"}
            //     })
            //   ]
            // ))
            //
            // # Message matching refType but NOT name — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "update", data: "type-only", extras: {
            //       "ref": {"timeserial": "ghi@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))
            //
            // # Message matching NEITHER — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "update", data: "neither")
            //   ]
            // ))
            //
            // # Another message matching BOTH — should be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "comment", data: "both-match-2", extras: {
            //       "ref": {"timeserial": "jkl@1700000000000-0", "type": "com.ably.reply"}
            //     })
            //   ]
            // ))

            // # Only messages matching ALL criteria (name == "comment" AND refType == "com.ably.reply")
            // ASSERT length(filtered_messages) == 2
            // ASSERT filtered_messages[0].data == "both-match"
            // ASSERT filtered_messages[1].data == "both-match-2"
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL22a/filter-matching-clientid-2
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#subscribe(MessageFilter) (RTL22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL22a")]
        public async Task RTL22a_FilterMatchingClientid()
        {
            // NOTE: subscribe(MessageFilter, ...) (RTL22) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL22a-clientid-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            //
            // filtered_messages = []
            // filter = MessageFilter(clientId: "user-42")
            // channel.subscribe(filter, (message) => {
            //   filtered_messages.append(message)
            // })
            //
            // # Message with matching clientId
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "chat", data: "hello", clientId: "user-42")
            //   ]
            // ))
            //
            // # Message with different clientId — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "chat", data: "hi", clientId: "user-99")
            //   ]
            // ))
            //
            // # Message with no clientId — should NOT be delivered
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "system", data: "broadcast")
            //   ]
            // ))
            //
            // # Another message with matching clientId
            // mock_ws.active_connection.send_to_client(ProtocolMessage(
            //   action: MESSAGE,
            //   channel: channel_name,
            //   messages: [
            //     Message(name: "chat", data: "world", clientId: "user-42")
            //   ]
            // ))

            // ASSERT length(filtered_messages) == 2
            // ASSERT filtered_messages[0].data == "hello"
            // ASSERT filtered_messages[0].clientId == "user-42"
            // ASSERT filtered_messages[1].data == "world"
            // ASSERT filtered_messages[1].clientId == "user-42"
            // CLOSE_CLIENT(client)
        }
    }
}
