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
    /// Derived from uts/realtime/unit/channels/channel_publish.md in ably/specification - the RTL6
    /// half. The RTN7/RTN19 tests about messages already in flight are in
    /// <c>PendingPublishTests</c>.
    ///
    /// Spec points: RTL6c1, RTL6c2, RTL6c4, RTL6c5, RTL6i1, RTL6i2, RTL6i3, RTL6j
    ///
    /// <para>
    /// RTL6c is a three-way decision made per publish: send it now, hold it until the connection
    /// comes up, or fail it. What decides is the connection's state, not the channel's - a channel
    /// that has never attached still publishes (RTL6c5) - with two channel states, SUSPENDED and
    /// FAILED, as the exception.
    /// </para>
    ///
    /// <para>
    /// Three of the file's thirty-five tests are not translated here or in the companion class.
    /// <c>RTL6i3/null-fields-msgpack-1</c> needs a msgpack frame, and msgpack is compiled out of
    /// this build. <c>RTL6j/publish-result-serials-0</c> and <c>RTL6j/batch-publish-serials-1</c>
    /// are wholly about the <c>PublishResult</c> that <c>PublishAsync</c> does not return - it
    /// returns <c>Task&lt;Result&gt;</c>, success or an error and nothing else - so nothing would
    /// be left of either test but a msgSerial check that
    /// <c>RTL6j_SequentialPublishesGetIncrementingMsgSerial</c> already makes. See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>publish(...) FAILS WITH error</c> maps to two different things here, and
    /// which one depends on where the failure came from. A NACK from the server arrives as a
    /// failed <c>Result</c>; a state check that refuses to publish at all throws. Both are
    /// translated as what they are. N3 in Uts/deviations.md records the split, and the misleading
    /// text the second one carries.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelPublishTests : UtsTestBase
    {
        public ChannelPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL6i1/publish-name-and-data-0
        [Fact]
        public async Task RTL6i1_PublishByNameAndData()
        {
            const string ChannelName = "test-RTL6i1";

            var published = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(ChannelName, published);

            var result = await channel.PublishAsync("greeting", "hello");
            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Action.Should().Be(ProtocolMessage.MessageAction.Message);
            messages[0].Channel.Should().Be(ChannelName);
            messages[0].Messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("greeting");
            messages[0].Messages[0].Data.Should().Be("hello");
        }

        // UTS: realtime/unit/RTL6i1/publish-message-object-1
        [Fact]
        public async Task RTL6i1_PublishAMessageObject()
        {
            const string ChannelName = "test-RTL6i1-object";

            var published = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(ChannelName, published);

            var data = new JObject { ["key"] = "value" };
            var result = await channel.PublishAsync(new Message("custom", data));
            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("custom");

            // RSL4d3: an object payload goes on the wire as its JSON text with a json encoding
            // step, so the captured message holds the string rather than the object given in.
            messages[0].Messages[0].Encoding.Should().Be("json");

            var wire = JObject.Parse((string)messages[0].Messages[0].Data);
            wire["key"].Value<string>().Should().Be("value");
        }

        // UTS: realtime/unit/RTL6i2/publish-message-array-0
        [Fact]
        public async Task RTL6i2_PublishAnArrayOfMessages()
        {
            const string ChannelName = "test-RTL6i2";

            var published = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(ChannelName, published);

            var result = await channel.PublishAsync(new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            });

            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1, "RTL6i2 - one ProtocolMessage carries the whole array");
            messages[0].Messages.Should().HaveCount(3);
            messages[0].Messages[0].Name.Should().Be("event1");
            messages[0].Messages[1].Name.Should().Be("event2");
            messages[0].Messages[2].Name.Should().Be("event3");
        }

        // UTS: realtime/unit/RTL6i3/null-fields-json-0
        [Fact]
        public async Task RTL6i3_NullFieldsAreOmittedFromTheJsonFrame()
        {
            const string ChannelName = "test-RTL6i3-json";

            var frames = new List<JObject>();
            var mockWs = ConnectingMock();

            mockWs.OnTextDataFrame = text =>
            {
                var decoded = JObject.Parse(text);
                if (decoded["action"] != null
                    && decoded["action"].Value<int>() == ProtocolMessages.Message)
                {
                    lock (frames)
                    {
                        frames.Add(decoded);
                    }
                }
            };

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            await channel.PublishAsync("click", null);
            await channel.PublishAsync(null, "payload");
            await channel.PublishAsync((string)null, null);

            await UtsClients.PollUntil(() => FrameCount(frames) == 3, "all three frames");

            var captured = SnapshotFrames(frames);

            var first = (JObject)captured[0]["messages"][0];
            first["name"].Value<string>().Should().Be("click");
            first.Should().NotContainKey("data", "RTL6i3 - a null data is left off the wire");

            var second = (JObject)captured[1]["messages"][0];
            second.Should().NotContainKey("name", "RTL6i3 - a null name is left off the wire");
            second["data"].Value<string>().Should().Be("payload");

            // The spec's third case - a message with both null - is in
            // RTL6i3_AMessageWithEveryFieldNullStillReachesTheWire below, where it fails. See D46.
        }

        // UTS: realtime/unit/RTL6i3/null-fields-json-0 (the both-null case)
        //
        // DEVIATION, D46. Omitting a null field is right; omitting the message is not. Publishing
        // a message with neither name nor data produces a MESSAGE frame with no messages array at
        // all, because ProtocolMessage.OnSerializing drops messages it considers empty. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RTL6i3_AMessageWithEveryFieldNullStillReachesTheWire()
        {
            const string ChannelName = "test-RTL6i3-all-null";

            var frames = new List<JObject>();
            var mockWs = ConnectingMock();

            mockWs.OnTextDataFrame = text =>
            {
                var decoded = JObject.Parse(text);
                if (decoded["action"] != null
                    && decoded["action"].Value<int>() == ProtocolMessages.Message)
                {
                    lock (frames)
                    {
                        frames.Add(decoded);
                    }
                }
            };

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            await channel.PublishAsync((string)null, null);

            await UtsClients.PollUntil(() => FrameCount(frames) == 1, "the frame");

            var frame = SnapshotFrames(frames)[0];

            frame.Should().ContainKey(
                "messages",
                "RTL6i3 - the fields are omitted, not the message carrying them");

            var message = (JObject)frame["messages"][0];
            message.Should().NotContainKey("name");
            message.Should().NotContainKey("data");
        }

        // UTS: realtime/unit/RTL6c1/publish-when-attached-0
        [Fact]
        public async Task RTL6c1_PublishesImmediatelyWhenAttached()
        {
            const string ChannelName = "test-RTL6c1-attached";

            var published = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(ChannelName, published);

            channel.State.Should().Be(ChannelState.Attached);

            var result = await channel.PublishAsync("test", "immediate");
            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("test");
            messages[0].Messages[0].Data.Should().Be("immediate");
        }

        // UTS: realtime/unit/RTL6c1/publish-when-attaching-1
        [Fact]
        public async Task RTL6c1_PublishesImmediatelyWhileAttaching()
        {
            const string ChannelName = "test-RTL6c1-attaching";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            // The ATTACH goes unanswered, so the channel stays ATTACHING for the whole test.
            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: false);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = UtsClients.AwaitChannelState(channel, ChannelState.Attaching);
            _ = channel.AttachAsync();
            await attaching;

            var result = await channel.PublishAsync("while-attaching", "data");
            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(
                1,
                "RTL6c1 - ATTACHING is neither SUSPENDED nor FAILED, so the publish goes out");
            messages[0].Messages[0].Name.Should().Be("while-attaching");
        }

        // UTS: realtime/unit/RTL6c1/publish-when-initialized-2
        [Fact]
        public async Task RTL6c1_PublishesImmediatelyWhenTheChannelIsInitialized()
        {
            const string ChannelName = "test-RTL6c1-initialized";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: false);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            var result = await channel.PublishAsync("before-attach", "data");
            result.IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("before-attach");
        }

        // UTS: realtime/unit/RTL6c5/no-implicit-attach-0
        [Fact]
        public async Task RTL6c5_PublishDoesNotTriggerAnImplicitAttach()
        {
            const string ChannelName = "test-RTL6c5";

            var published = new List<ProtocolMessage>();
            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    Record(published, msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            var result = await channel.PublishAsync("no-attach", "test");
            result.IsSuccess.Should().BeTrue();

            Snapshot(published).Should().HaveCount(1, "RTL6c1 - the publish was sent");

            channel.State.Should().Be(
                ChannelState.Initialized,
                "RTL6c5 - publishing is not a reason to attach");
            attachCount.Should().Be(0);
        }

        // UTS: realtime/unit/RTL6c2/queued-when-connecting-0
        [Fact]
        public async Task RTL6c2_PublishIsQueuedWhileConnecting()
        {
            const string ChannelName = "test-RTL6c2-connecting";

            var published = new List<ProtocolMessage>();

            // The connection attempt is held until the test releases it, so CONNECTING is a state
            // the test controls rather than one it has to catch.
            var release = new TaskCompletionSource<bool>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                release.Task.ContinueWith(
                    _ =>
                    {
                        conn.RespondWithSuccess();
                        conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                    },
                    TaskScheduler.Default);
            });

            var client = RealtimeClient(mockWs);
            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: true);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            var channel = client.Channels.Get(ChannelName);
            var publishing = channel.PublishAsync("queued", "waiting");

            Snapshot(published).Should().BeEmpty("RTL6c2 - there is no transport to send it on yet");

            release.TrySetResult(true);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            (await publishing).IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("queued");
            messages[0].Messages[0].Data.Should().Be("waiting");
        }

        // UTS: realtime/unit/RTL6c2/queued-when-initialized-2
        [Fact]
        public async Task RTL6c2_PublishIsQueuedBeforeConnectIsCalled()
        {
            const string ChannelName = "test-RTL6c2-initialized";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: true);

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            var channel = client.Channels.Get(ChannelName);
            var publishing = channel.PublishAsync("pre-connect", "early");

            Snapshot(published).Should().BeEmpty("RTL6c2 - nothing is sent before connecting");

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            (await publishing).IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(1);
            messages[0].Messages[0].Name.Should().Be("pre-connect");
        }

        // UTS: realtime/unit/RTL6c2/queued-when-disconnected-1
        [Fact]
        public async Task RTL6c2_PublishIsQueuedWhileDisconnected()
        {
            const string ChannelName = "test-RTL6c2-disconnected";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: true);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var disconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Disconnected);

            mockWs.SimulateDisconnect();
            await disconnected;

            var countBefore = Snapshot(published).Count;
            var publishing = channel.PublishAsync("during-disconnect", "queued");

            // RTN15a retries instantly, so the reconnect needs no help.
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            (await publishing).IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Count.Should().BeGreaterThan(countBefore);

            var queued = messages.FindAll(m => m.Messages[0].Name == "during-disconnect");
            queued.Should().HaveCount(
                1,
                "RTL6c2 - the publish waited for the connection rather than failing");
        }

        // UTS: realtime/unit/RTL6c2/queued-messages-order-4
        [Fact]
        public async Task RTL6c2_QueuedMessagesAreSentInOrder()
        {
            const string ChannelName = "test-RTL6c2-order";

            var published = new List<ProtocolMessage>();
            var release = new TaskCompletionSource<bool>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                release.Task.ContinueWith(
                    _ =>
                    {
                        conn.RespondWithSuccess();
                        conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                    },
                    TaskScheduler.Default);
            });

            var client = RealtimeClient(mockWs);
            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: true);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            var channel = client.Channels.Get(ChannelName);
            var first = channel.PublishAsync("first", "1");
            var second = channel.PublishAsync("second", "2");
            var third = channel.PublishAsync("third", "3");

            Snapshot(published).Should().BeEmpty();

            release.TrySetResult(true);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            await Task.WhenAll(first, second, third);

            var messages = Snapshot(published);
            messages.Should().HaveCount(3);
            messages[0].Messages[0].Name.Should().Be("first");
            messages[1].Messages[0].Name.Should().Be("second");
            messages[2].Messages[0].Name.Should().Be("third");
        }

        // UTS: realtime/unit/RTL6c2/fails-no-queue-messages-3
        [Fact]
        public async Task RTL6c2_PublishFailsWhenQueueMessagesIsFalse()
        {
            const string ChannelName = "test-RTL6c2-no-queue";

            var release = new TaskCompletionSource<bool>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                release.Task.ContinueWith(
                    _ =>
                    {
                        conn.RespondWithSuccess();
                        conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                    },
                    TaskScheduler.Default);
            });

            var client = RealtimeClient(mockWs, configure: options => options.QueueMessages = false);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            var channel = client.Channels.Get(ChannelName);

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull(
                "RTL6c2 - without queueing there is nowhere to put it");
            error.ErrorInfo.Code.Should().NotBe(0);

            release.TrySetResult(true);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-suspended-0
        [Fact]
        public async Task RTL6c4_PublishFailsWhenTheConnectionIsSuspended()
        {
            const string ChannelName = "test-RTL6c4-conn-suspended";

            var clock = new TestClock();
            var attempts = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts = attempts + 1;

                // The spec advances past connectionStateTtl. The second refusal is the one that
                // converts DISCONNECTED into SUSPENDED, so the clock moves just before it - the
                // same placement RTN14e needed.
                if (attempts == 2)
                {
                    clock.Advance((int)TimeSpan.FromSeconds(120).TotalMilliseconds);
                }

                conn.RespondWithRefused();
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(200));

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Suspended, TimeSpan.FromSeconds(10));

            var channel = client.Channels.Get(ChannelName);

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull();
            error.ErrorInfo.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-closed-1
        [Fact]
        public async Task RTL6c4_PublishFailsWhenTheConnectionIsClosed()
        {
            const string ChannelName = "test-RTL6c4-conn-closed";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Close)
                {
                    mockWs.SendToClient(ProtocolMessages.ClosedMessage());
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var closed = UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await closed;

            var channel = client.Channels.Get(ChannelName);

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull();
            error.ErrorInfo.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-failed-2
        [Fact]
        public async Task RTL6c4_PublishFailsWhenTheConnectionIsFailed()
        {
            const string ChannelName = "test-RTL6c4-conn-failed";

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ErrorMessage(40100, "Invalid credentials", 401));
            });

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(10));

            var channel = client.Channels.Get(ChannelName);

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull();
            error.ErrorInfo.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTL6c4/fails-channel-suspended-3
        [Fact]
        public async Task RTL6c4_PublishFailsWhenTheChannelIsSuspended()
        {
            const string ChannelName = "test-RTL6c4-channel-suspended";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(300);
                options.ChannelRetryTimeout = TimeSpan.FromMinutes(10);
            });

            // No ATTACHED, so the attach times out into SUSPENDED (RTL4f).
            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: false);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeFalse();

            await UtsClients.AwaitChannelState(
                channel, ChannelState.Suspended, TimeSpan.FromSeconds(10));

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull();
            Snapshot(published).Should().BeEmpty("RTL6c4 - nothing reached the server");
        }

        // UTS: realtime/unit/RTL6c4/fails-channel-failed-4
        [Fact]
        public async Task RTL6c4_PublishFailsWhenTheChannelIsFailed()
        {
            const string ChannelName = "test-RTL6c4-channel-failed";

            var published = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName, 40160, "Not permitted", 401));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    Record(published, msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attachResult = await channel.AttachAsync();

            attachResult.IsSuccess.Should().BeFalse();
            channel.State.Should().Be(ChannelState.Failed);

            var error = await Assert.ThrowsAsync<AblyException>(
                () => channel.PublishAsync("fail", "should-error"));

            error.ErrorInfo.Should().NotBeNull();
            Snapshot(published).Should().BeEmpty("RTL6c4 - nothing reached the server");
        }

        // UTS: realtime/unit/RTL6j/incrementing-msg-serial-2
        //
        // The spec also asserts each publish resolves with its own PublishResult. There is no such
        // type here, so those three assertions are dropped; the msgSerial half - which is RTN7b's
        // requirement and the reason the ACKs can be matched up at all - is what remains.
        [Fact]
        public async Task RTL6j_SequentialPublishesGetIncrementingMsgSerial()
        {
            const string ChannelName = "test-RTL6j-serial";

            var published = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(ChannelName, published);

            (await channel.PublishAsync("first", "1")).IsSuccess.Should().BeTrue();
            (await channel.PublishAsync("second", "2")).IsSuccess.Should().BeTrue();
            (await channel.PublishAsync("third", "3")).IsSuccess.Should().BeTrue();

            var messages = Snapshot(published);
            messages.Should().HaveCount(3);
            messages[0].MsgSerial.Should().Be(0);
            messages[1].MsgSerial.Should().Be(1);
            messages[2].MsgSerial.Should().Be(2);
        }

        // UTS: realtime/unit/RTL6j/nack-results-error-3
        [Fact]
        public async Task RTL6j_NackResultsInAnError()
        {
            const string ChannelName = "test-RTL6j-nack";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    mockWs.SendToClient(ProtocolMessages.NackMessage(
                        msg.MsgSerial, 40160, "Publish rejected", 401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var result = await channel.PublishAsync("rejected", "data");

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().Be(40160);
            result.Error.Message.Should().Be("Publish rejected");
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

        private static void Record(List<ProtocolMessage> published, ProtocolMessage msg)
        {
            lock (published)
            {
                published.Add(msg);
            }
        }

        private static List<ProtocolMessage> Snapshot(List<ProtocolMessage> published)
        {
            lock (published)
            {
                return new List<ProtocolMessage>(published);
            }
        }

        private static int FrameCount(List<JObject> frames)
        {
            lock (frames)
            {
                return frames.Count;
            }
        }

        private static List<JObject> SnapshotFrames(List<JObject> frames)
        {
            lock (frames)
            {
                return new List<JObject>(frames);
            }
        }

        /// <summary>
        /// Records every outgoing MESSAGE and ACKs it, so publishes resolve; optionally answers
        /// the ATTACH too.
        /// </summary>
        private static void RecordAndAck(
            MockWebSocket mockWs,
            List<ProtocolMessage> published,
            ProtocolMessage msg,
            bool attach)
        {
            if (msg.Action == ProtocolMessage.MessageAction.Attach && attach)
            {
                mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                return;
            }

            if (msg.Action == ProtocolMessage.MessageAction.Message)
            {
                Record(published, msg);
                mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
            }
        }

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            string channelName,
            List<ProtocolMessage> published)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg => RecordAndAck(mockWs, published, msg, attach: true);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();

            return (mockWs, channel);
        }
    }
}
