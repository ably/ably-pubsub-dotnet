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

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channel_subscribe.md in ably/specification.
    ///
    /// Spec points: RTL7a, RTL7b, RTL7f, RTL7g, RTL8a, RTL8b, RTL8c, RTL17
    ///
    /// <para>
    /// Subscribing is three separate questions the spec keeps apart: which listeners a message
    /// reaches (RTL7a, RTL7b), whether subscribing attaches the channel on the caller's behalf
    /// (RTL7g), and when delivery stops (RTL8, RTL17).
    /// </para>
    ///
    /// <para>
    /// Six of the file's twenty-one tests are not translated, all absent API.
    /// <c>RTL7h/no-attach-on-subscribe-0</c> needs <c>attachOnSubscribe</c>, and the five RTL22
    /// tests need <c>MessageFilter</c> - neither type nor any part of its surface exists here.
    /// See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// The spec's setups pass <c>attachOnSubscribe: false</c> to keep <c>subscribe()</c> from
    /// attaching, but every one of those tests attaches explicitly first, so the channel is
    /// ATTACHED before the subscribe and RTL7g does nothing either way. The option is dropped
    /// rather than worked around.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelSubscribeTests : UtsTestBase
    {
        public ChannelSubscribeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL7a/subscribe-all-messages-0
        [Fact]
        public async Task RTL7a_SubscribeWithNoNameReceivesAllMessages()
        {
            const string ChannelName = "test-RTL7a";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var received = new List<Message>();
            channel.Subscribe(message => Append(received, message));

            Send(mockWs, ChannelName, Msg("event1", "data1"));
            Send(mockWs, ChannelName, Msg("event2", "data2"));
            Send(mockWs, ChannelName, Msg(null, "data3"));

            await UtsClients.PollUntil(() => Count(received) == 3, "all three messages");

            var messages = UtsClients.Snapshot(received);
            messages[0].Name.Should().Be("event1");
            messages[0].Data.Should().Be("data1");
            messages[1].Name.Should().Be("event2");
            messages[1].Data.Should().Be("data2");
            messages[2].Name.Should().BeNull("RTL7a - an unnamed message still reaches the listener");
            messages[2].Data.Should().Be("data3");
        }

        // UTS: realtime/unit/RTL7a/multiple-messages-per-protocol-1
        [Fact]
        public async Task RTL7a_MultipleMessagesInOneProtocolMessage()
        {
            const string ChannelName = "test-RTL7a-multi";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var received = new List<Message>();
            channel.Subscribe(message => Append(received, message));

            Send(mockWs, ChannelName, Msg("batch1", "first"), Msg("batch2", "second"), Msg("batch3", "third"));

            await UtsClients.PollUntil(() => Count(received) == 3, "all three messages");

            var messages = UtsClients.Snapshot(received);
            messages[0].Name.Should().Be("batch1");
            messages[1].Name.Should().Be("batch2");
            messages[2].Name.Should().Be("batch3");
        }

        // UTS: realtime/unit/RTL7b/name-filtered-subscribe-0
        [Fact]
        public async Task RTL7b_SubscribeWithNameReceivesOnlyMatchingMessages()
        {
            const string ChannelName = "test-RTL7b";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var received = new List<Message>();
            var all = new List<Message>();
            channel.Subscribe("target", message => Append(received, message));

            // Nothing is delivered for the two non-matching messages, so there is no event to wait
            // on. A second, unfiltered listener gives the test something that does fire, which is
            // what lets it tell "not delivered yet" from "not delivered at all".
            channel.Subscribe(message => Append(all, message));

            Send(mockWs, ChannelName, Msg("other", "should-not-receive"));
            Send(mockWs, ChannelName, Msg("target", "should-receive"));
            Send(mockWs, ChannelName, Msg(null, "no-name-should-not-receive"));

            await UtsClients.PollUntil(() => Count(all) == 3, "all three messages to arrive");

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(1, "RTL7b - only the matching name is delivered");
            messages[0].Name.Should().Be("target");
            messages[0].Data.Should().Be("should-receive");
        }

        // UTS: realtime/unit/RTL7b/multiple-name-subscriptions-1
        [Fact]
        public async Task RTL7b_MultipleNameSubscriptionsAreIndependent()
        {
            const string ChannelName = "test-RTL7b-multi";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var alpha = new List<Message>();
            var beta = new List<Message>();
            channel.Subscribe("alpha", message => Append(alpha, message));
            channel.Subscribe("beta", message => Append(beta, message));

            Send(
                mockWs,
                ChannelName,
                Msg("alpha", "a1"),
                Msg("beta", "b1"),
                Msg("alpha", "a2"),
                Msg("gamma", "g1"));

            await UtsClients.PollUntil(
                () => Count(alpha) == 2 && Count(beta) == 1,
                "the alpha and beta messages");

            var alphaMessages = UtsClients.Snapshot(alpha);
            alphaMessages[0].Data.Should().Be("a1");
            alphaMessages[1].Data.Should().Be("a2");

            UtsClients.Snapshot(beta)[0].Data.Should().Be("b1");
        }

        // UTS: realtime/unit/RTL7g/implicit-attach-initialized-0
        [Fact]
        public async Task RTL7g_SubscribeAttachesAnInitializedChannel()
        {
            const string ChannelName = "test-RTL7g-initialized";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            var received = new List<Message>();
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            channel.Subscribe(message => Append(received, message));

            await attached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1, "RTL7g - subscribing attached the channel");

            // The attach is only half of it; the listener has to have been registered too.
            Send(mockWs, ChannelName, Msg("test", "hello"));
            await UtsClients.PollUntil(() => Count(received) == 1, "the message");
        }

        // UTS: realtime/unit/RTL7g/implicit-attach-detached-1
        [Fact]
        public async Task RTL7g_SubscribeAttachesADetachedChannel()
        {
            const string ChannelName = "test-RTL7g-detached";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            attachCount.Should().Be(1);

            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Subscribe(message => { });
            await attached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(2, "RTL7g - DETACHED is one of the states subscribing attaches from");
        }

        // UTS: realtime/unit/RTL7g/listener-registered-attach-fails-2
        [Fact]
        public async Task RTL7g_ListenerIsRegisteredEvenIfTheImplicitAttachFails()
        {
            const string ChannelName = "test-RTL7g-fail";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName, 40160, "Not permitted", 401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var received = new List<Message>();
            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);

            channel.Subscribe(message => Append(received, message));

            await failed;

            // The listener's registration can only be shown by delivering to it, so the channel has
            // to get back to ATTACHED. The subscribe that mattered has already happened.
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            var result = await channel.AttachAsync();
            result.IsSuccess.Should().BeTrue();

            Send(mockWs, ChannelName, Msg("test", "after-reattach"));

            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Data.Should().Be(
                "after-reattach",
                "RTL7g - the listener is registered whatever the attach did");
        }

        // UTS: realtime/unit/RTL7g/no-attach-when-attached-3
        [Fact]
        public async Task RTL7g_SubscribeDoesNotAttachAnAttachedChannel()
        {
            const string ChannelName = "test-RTL7g-already";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            attachCount.Should().Be(1);

            channel.Subscribe(message => { });

            // Nothing should happen, so there is nothing to wait for. Round-trip the workflow to
            // give a second ATTACH the chance to appear before the count is read.
            await channel.Presence.GetAsync(waitForSync: false);

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1, "RTL7g - ATTACHED is not one of the states it attaches from");
        }

        // UTS: realtime/unit/RTL7g/no-attach-when-attaching-4
        [Fact]
        public async Task RTL7g_SubscribeDoesNotAttachAnAttachingChannel()
        {
            const string ChannelName = "test-RTL7g-attaching";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;

                    // Unanswered, so the channel stays ATTACHING.
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            _ = channel.AttachAsync();
            await attaching;

            // ATTACHING is reached before the frame leaves, so wait for the ATTACH itself - the
            // whole test is a count of them.
            await UtsClients.PollUntil(() => attachCount == 1, "the first ATTACH");

            channel.Subscribe(message => { });

            await channel.Presence.GetAsync(waitForSync: false);

            channel.State.Should().Be(ChannelState.Attaching);
            attachCount.Should().Be(1, "RTL7g - ATTACHING is not one of the states it attaches from");
        }

        // UTS: realtime/unit/RTL17/no-delivery-when-not-attached-0
        [Fact]
        public async Task RTL17_MessagesAreNotDeliveredWhenNotAttached()
        {
            const string ChannelName = "test-RTL17";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg => { };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var received = new List<Message>();
            channel.Subscribe(message => Append(received, message));

            var attaching = UtsClients.AwaitChannelState(channel, ChannelState.Attaching);
            _ = channel.AttachAsync();
            await attaching;

            Send(mockWs, ChannelName, Msg("premature", "should-not-deliver"));

            // Nothing is expected, so the wait is for the workflow to have handled the frame rather
            // than for a delivery that will not come.
            await channel.Presence.GetAsync(waitForSync: false);

            Count(received).Should().Be(
                0,
                "RTL17 - nothing is passed to subscribers unless the channel is ATTACHED");
        }

        // UTS: realtime/unit/RTL7f/no-echo-messages-0
        //
        // ADAPTED, as the spec's own implementation note directs. RTL7f allows echo suppression to
        // be either client-side filtering or server-side delegation, and says an SDK doing the
        // latter "should adapt this test to verify the echo parameter is set on the connection
        // URL". This SDK delegates: TransportParams.cs:188 writes echo into the query string and
        // nothing downstream compares connectionIds.
        [Fact]
        public async Task RTL7f_EchoMessagesFalseIsSentOnTheConnectionUrl()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.EchoMessages = false);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attempt = mockWs.ConnectionAttempts[0];
            attempt.QueryParams.Should().ContainKey("echo");
            attempt.QueryParams["echo"].Should().Be(
                "false",
                "RTL7f - the server is asked not to echo, rather than the client filtering");
        }

        // UTS: realtime/unit/RTL8a/unsubscribe-specific-listener-0
        [Fact]
        public async Task RTL8a_UnsubscribeRemovesOnlyThatListener()
        {
            const string ChannelName = "test-RTL8a";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var messagesA = new List<Message>();
            var messagesB = new List<Message>();
            Action<Message> listenerA = message => Append(messagesA, message);
            Action<Message> listenerB = message => Append(messagesB, message);

            channel.Subscribe(listenerA);
            channel.Subscribe(listenerB);

            Send(mockWs, ChannelName, Msg("msg1", "first"));
            await UtsClients.PollUntil(
                () => Count(messagesA) == 1 && Count(messagesB) == 1,
                "the first message at both listeners");

            channel.Unsubscribe(listenerA);

            Send(mockWs, ChannelName, Msg("msg2", "second"));
            await UtsClients.PollUntil(() => Count(messagesB) == 2, "the second message at B");

            Count(messagesA).Should().Be(1, "RTL8a - listener A was unsubscribed");
            UtsClients.Snapshot(messagesB)[1].Name.Should().Be("msg2");
        }

        // UTS: realtime/unit/RTL8b/unsubscribe-named-listener-0
        [Fact]
        public async Task RTL8b_UnsubscribeRemovesOnlyThatNameSubscription()
        {
            const string ChannelName = "test-RTL8b";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var received = new List<Message>();
            Action<Message> listener = message => Append(received, message);

            // The same listener under two names, so unsubscribing one must leave the other.
            channel.Subscribe("alpha", listener);
            channel.Subscribe("beta", listener);

            Send(mockWs, ChannelName, Msg("alpha", "a1"), Msg("beta", "b1"));
            await UtsClients.PollUntil(() => Count(received) == 2, "both messages");

            channel.Unsubscribe("alpha", listener);

            Send(mockWs, ChannelName, Msg("alpha", "a2"), Msg("beta", "b2"));
            await UtsClients.PollUntil(() => Count(received) == 3, "the beta message");

            var messages = UtsClients.Snapshot(received);
            messages.Should().HaveCount(3, "RTL8b - only the alpha subscription was removed");
            messages[2].Name.Should().Be("beta");
            messages[2].Data.Should().Be("b2");
        }

        // UTS: realtime/unit/RTL8c/unsubscribe-all-listeners-0
        [Fact]
        public async Task RTL8c_UnsubscribeWithNoArgumentsRemovesEveryListener()
        {
            const string ChannelName = "test-RTL8c";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var messagesAll = new List<Message>();
            var messagesNamed = new List<Message>();
            channel.Subscribe(message => Append(messagesAll, message));
            channel.Subscribe("specific", message => Append(messagesNamed, message));

            Send(mockWs, ChannelName, Msg("specific", "first"));
            await UtsClients.PollUntil(
                () => Count(messagesAll) == 1 && Count(messagesNamed) == 1,
                "the first message at both listeners");

            channel.Unsubscribe();

            Send(mockWs, ChannelName, Msg("specific", "second"), Msg("other", "third"));

            // Nothing should arrive, so wait for the workflow to have processed the frame instead.
            await channel.Presence.GetAsync(waitForSync: false);

            Count(messagesAll).Should().Be(1, "RTL8c - the unnamed listener was removed");
            Count(messagesNamed).Should().Be(1, "RTL8c - the named listener was removed too");
        }

        // UTS: realtime/unit/RTL8a/unsubscribe-noop-not-subscribed-1
        [Fact]
        public async Task RTL8a_UnsubscribingAListenerThatWasNeverSubscribedIsANoOp()
        {
            const string ChannelName = "test-RTL8a-noop";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var received = new List<Message>();
            Action<Message> activeListener = message => Append(received, message);
            Action<Message> unusedListener = message => { };

            channel.Subscribe(activeListener);
            channel.Unsubscribe(unusedListener);

            Send(mockWs, ChannelName, Msg("test", "still-works"));
            await UtsClients.PollUntil(() => Count(received) == 1, "the message");

            UtsClients.Snapshot(received)[0].Data.Should().Be("still-works");
        }

        private static JObject Msg(string name, string data)
            => new JObject { ["name"] = name, ["data"] = data };

        private static void Send(MockWebSocket mockWs, string channelName, params JObject[] messages)
            => mockWs.SendToClient(
                ProtocolMessages.MessageProtocolMessage(channelName, new JArray(messages)));

        private static void Append(List<Message> received, Message message)
        {
            lock (received)
            {
                received.Add(message);
            }
        }

        private static int Count(List<Message> received)
        {
            lock (received)
            {
                return received.Count;
            }
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            string channelName)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();

            return (mockWs, channel);
        }
    }
}
