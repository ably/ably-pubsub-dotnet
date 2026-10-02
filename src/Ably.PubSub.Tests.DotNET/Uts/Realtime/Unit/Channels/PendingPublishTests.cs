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
    /// Derived from uts/realtime/unit/channels/channel_publish.md in ably/specification - the
    /// RTN7/RTN19 half, about messages already on the wire and waiting for an ACK. The RTL6 half
    /// is in <c>ChannelPublishTests</c>.
    ///
    /// Spec points: RTN7d, RTN7e, RTN19a, RTN19a2, RTN19b
    ///
    /// <para>
    /// A publish that has been sent but not acknowledged is in limbo, and what happens to it
    /// depends on where the connection goes next. SUSPENDED, CLOSED or FAILED and it has failed
    /// (RTN7e). DISCONNECTED and it depends on whether queueing is on: off means failed too
    /// (RTN7d), on means held for the new transport (RTN19a), where whether it keeps its msgSerial
    /// turns on whether the resume worked (RTN19a2). ATTACH and DETACH get the same treatment
    /// (RTN19b).
    /// </para>
    ///
    /// <para>
    /// The spec uses a fake clock to skip the reconnect wait. There is no timer seam here, so
    /// <c>DisconnectedRetryTimeout</c> is shortened instead - and often not even that is needed,
    /// since RTN15a retries the first disconnect instantly.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class PendingPublishTests : UtsTestBase
    {
        /// <summary>
        /// Waits that cross a disconnect and a reconnect need more than the five-second default.
        /// </summary>
        private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);

        public PendingPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN7e/pending-fail-suspended-0
        [Fact]
        public async Task RTN7e_PendingPublishesFailWhenTheConnectionSuspends()
        {
            const string ChannelName = "test-RTN7e-suspended";

            var clock = new TestClock();
            var attempts = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts = attempts + 1;

                if (attempts == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                    return;
                }

                // Past connectionStateTtl on the second refusal, so DISCONNECTED converts to
                // SUSPENDED - the placement RTN14e needs, for the same reason.
                if (attempts == 3)
                {
                    clock.Advance((int)TimeSpan.FromSeconds(120).TotalMilliseconds);
                }

                conn.RespondWithRefused();
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(200));

            // The MESSAGE is never answered, so the publish is still pending when the connection
            // gives up.
            mockWs.OnMessageFromClient = msg => AnswerAttachOnly(mockWs, msg);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            mockWs.SimulateDisconnect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Suspended, StateTimeout);

            var result = await publishing;

            result.IsSuccess.Should().BeFalse("RTN7e - a pending publish cannot survive SUSPENDED");
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTN7e/pending-fail-closed-1
        [Fact]
        public async Task RTN7e_PendingPublishesFailWhenTheConnectionCloses()
        {
            const string ChannelName = "test-RTN7e-closed";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Close)
                {
                    mockWs.SendToClient(ProtocolMessages.ClosedMessage());
                    return;
                }

                AnswerAttachOnly(mockWs, msg);
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            var closed = UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await closed;

            var result = await publishing;

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTN7e/pending-fail-failed-2
        [Fact]
        public async Task RTN7e_PendingPublishesFailWhenTheConnectionFails()
        {
            const string ChannelName = "test-RTN7e-failed";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    // A fatal ERROR instead of the ACK the publish is waiting for.
                    mockWs.SendToClient(ProtocolMessages.ErrorMessage(
                        80019, "Connection closed due to admin action", 400));
                    return;
                }

                AnswerAttachOnly(mockWs, msg);
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, StateTimeout);

            var result = await publishing;

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTN7e/multiple-pending-fail-3
        [Fact]
        public async Task RTN7e_EveryPendingPublishFailsOnTheStateChange()
        {
            const string ChannelName = "test-RTN7e-multiple";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Close)
                {
                    mockWs.SendToClient(ProtocolMessages.ClosedMessage());
                    return;
                }

                AnswerAttachOnly(mockWs, msg);
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var first = channel.PublishAsync("one", "1");
            var second = channel.PublishAsync("two", "2");
            var third = channel.PublishAsync("three", "3");

            var closed = UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await closed;

            var results = await Task.WhenAll(first, second, third);

            foreach (var result in results)
            {
                result.IsSuccess.Should().BeFalse("RTN7e - none of them can be acknowledged now");
                result.Error.Should().NotBeNull();
            }
        }

        // UTS: realtime/unit/RTN7e/error-represents-reason-4
        [Fact]
        public async Task RTN7e_TheErrorIsTheReasonForTheStateChange()
        {
            const string ChannelName = "test-RTN7e-reason";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Message)
                {
                    mockWs.SendToClient(ProtocolMessages.ErrorMessage(
                        80019, "Connection closed due to admin action", 400));
                    return;
                }

                AnswerAttachOnly(mockWs, msg);
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, StateTimeout);

            var result = await publishing;

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().Be(80019, "RTN7e - the publish is told why, not just that");
            result.Error.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
            result.Error.Message.Should().Be("Connection closed due to admin action");

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
        }

        // UTS: realtime/unit/RTN7d/fail-disconnected-no-queue-0
        [Fact]
        public async Task RTN7d_PendingPublishesFailOnDisconnectedWithoutQueueing()
        {
            const string ChannelName = "test-RTN7d-no-queue";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.QueueMessages = false;
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            mockWs.OnMessageFromClient = msg => AnswerAttachOnly(mockWs, msg);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            var disconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);

            mockWs.SimulateDisconnect();
            await disconnected;

            var result = await publishing;

            result.IsSuccess.Should().BeFalse(
                "RTN7d - without queueing there is nothing to hold it for the new transport");
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().NotBe(0);
        }

        // UTS: realtime/unit/RTN7d/survive-disconnected-queue-1
        //
        // The spec asserts the publish resolves with a PublishResult carrying a serial. There is
        // no such type here, so what remains is the part RTN7d is actually about: with queueing on
        // - the default - DISCONNECTED does not fail a pending publish, and it completes once the
        // new transport acknowledges it.
        [Fact]
        public async Task RTN7d_PendingPublishesSurviveDisconnectedWhenQueueingIsOn()
        {
            const string ChannelName = "test-RTN7d-default";

            var connections = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message && connections >= 2)
                {
                    // Only the resend, on the second transport, is acknowledged.
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("pending", "data");

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            var result = await publishing;

            result.IsSuccess.Should().BeTrue(
                "RTN7d - queueing is on, so the publish waited for the new transport");
        }

        // UTS: realtime/unit/RTN19a/resent-on-new-transport-0
        //
        // The spec's PublishResult assertions are replaced by the plain success of the publish;
        // the two that matter - sent on the first transport, sent again on the second with the
        // same content - are unaffected.
        [Fact]
        public async Task RTN19a_PendingMessagesAreResentOnTheNewTransport()
        {
            const string ChannelName = "test-RTN19a-resend";

            var sent = new List<(int Connection, ProtocolMessage Message)>();
            var connections = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                Record(sent, connections, msg);

                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message && connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("resend-me", "data");

            await UtsClients.PollUntil(
                () => MessagesOn(sent, 1).Count == 1,
                "the publish on the first transport");

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            (await publishing).IsSuccess.Should().BeTrue();

            var resent = MessagesOn(sent, 2);
            resent.Should().NotBeEmpty("RTN19a - the old transport will never ACK it");
            resent[0].Messages[0].Name.Should().Be("resend-me");
        }

        // UTS: realtime/unit/RTN19a2/same-serial-on-resume-0
        [Fact]
        public async Task RTN19a2_ResentMessagesKeepTheirMsgSerialOnASuccessfulResume()
        {
            const string ChannelName = "test-RTN19a2-resume";

            var sent = new List<(int Connection, ProtocolMessage Message)>();
            var connections = 0;

            // The same connectionId on both transports, with no error: RTN15c6, a valid resume.
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                Record(sent, connections, msg);

                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message && connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var first = channel.PublishAsync("one", "1");
            var second = channel.PublishAsync("two", "2");

            await UtsClients.PollUntil(
                () => MessagesOn(sent, 1).Count == 2,
                "both publishes on the first transport");

            var original = MessagesOn(sent, 1);
            var firstSerial = original[0].MsgSerial;
            var secondSerial = original[1].MsgSerial;

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            await Task.WhenAll(first, second);

            var resent = MessagesOn(sent, 2);
            resent.Should().HaveCount(2);
            resent[0].MsgSerial.Should().Be(
                firstSerial,
                "RTN19a2 - a resumed connection is the same connection, so the serials stand");
            resent[1].MsgSerial.Should().Be(secondSerial);
        }

        // UTS: realtime/unit/RTN19a2/new-serial-failed-resume-1
        [Fact]
        public async Task RTN19a2_ResentMessagesGetNewMsgSerialsOnAFailedResume()
        {
            const string ChannelName = "test-RTN19a2-failed-resume";

            var sent = new List<(int Connection, ProtocolMessage Message)>();
            var connections = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();

                if (connections == 1)
                {
                    conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                    return;
                }

                // A different connectionId and an error: RTN15c7, the resume did not take, so the
                // msgSerial counter starts again.
                var connected = ProtocolMessages.ConnectedMessage("different-connection-id");
                connected["error"] = ProtocolMessages.ErrorObject(
                    80008, "Unable to recover connection", 400);
                conn.SendToClient(connected);
            });

            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                Record(sent, connections, msg);

                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Message && connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var first = channel.PublishAsync("one", "1");
            var second = channel.PublishAsync("two", "2");

            await UtsClients.PollUntil(
                () => MessagesOn(sent, 1).Count == 2,
                "both publishes on the first transport");

            var original = MessagesOn(sent, 1);
            original[0].MsgSerial.Should().Be(0);
            original[1].MsgSerial.Should().Be(1);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            await Task.WhenAll(first, second);

            var resent = MessagesOn(sent, 2);
            resent.Should().HaveCount(2);
            resent[0].MsgSerial.Should().Be(
                0,
                "RTN19a2 - a failed resume resets the counter, so the resend starts from zero");
            resent[1].MsgSerial.Should().Be(1);
        }

        // UTS: realtime/unit/RTN19b/attach-resent-on-reconnect-0
        [Fact]
        public async Task RTN19b_APendingAttachIsResentOnTheNewTransport()
        {
            const string ChannelName = "test-RTN19b-attach";

            var sent = new List<(int Connection, ProtocolMessage Message)>();
            var connections = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                Record(sent, connections, msg);

                // The first transport never answers the ATTACH, so it is still outstanding when
                // the connection drops.
                if (msg.Action == ProtocolMessage.MessageAction.Attach && connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = channel.AttachAsync();

            await UtsClients.PollUntil(
                () => AttachesOn(sent, 1).Count == 1,
                "the ATTACH on the first transport");

            AttachesOn(sent, 1)[0].Channel.Should().Be(ChannelName);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            // The resend happens after CONNECTED is observed, so it is waited for rather than
            // read. The task returned by AttachAsync is not the signal - see D47 and the gated
            // test below.
            await UtsClients.PollUntil(
                () => AttachesOn(sent, 2).Count >= 1,
                "RTN19b - the pending ATTACH going out again");

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, StateTimeout);

            var resent = AttachesOn(sent, 2);
            resent[0].Channel.Should().Be(ChannelName);
            channel.State.Should().Be(ChannelState.Attached);

            // Observed, not asserted on: this is the task D47 is about.
            var attachResult = await attaching;
            Output.WriteLine("attach() reported IsSuccess=" + attachResult.IsSuccess);
        }

        // UTS: realtime/unit/RTN19b/attach-resent-on-reconnect-0 (the caller's half)
        //
        // DEVIATION, D47. RTL4d says the attach callback fires when the channel *next moves to*
        // ATTACHED, DETACHED, SUSPENDED or FAILED. Measured: it fires on DISCONNECTED, while the
        // channel is still ATTACHING, reporting failure - and the same channel then goes on to
        // attach, because RTN19b resends the ATTACH. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL4d_APendingAttachReportsTheOutcomeOfTheResentAttach()
        {
            const string ChannelName = "test-RTN19b-attach-callback";

            var connections = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            var attaches = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attaches = attaches + 1;
                if (connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = channel.AttachAsync();

            await UtsClients.PollUntil(() => attaches >= 1, "the ATTACH on the first transport");

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            var attachResult = await attaching;

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, StateTimeout);

            attachResult.IsSuccess.Should().BeTrue(
                "RTL4d - the channel reached ATTACHED, which is the move the callback is for");
        }

        // UTS: realtime/unit/RTN19b/detach-resent-on-reconnect-1
        [Fact]
        public async Task RTN19b_APendingDetachIsResentOnTheNewTransport()
        {
            const string ChannelName = "test-RTN19b-detach";

            var sent = new List<(int Connection, ProtocolMessage Message)>();
            var connections = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connections = connections + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                Record(sent, connections, msg);

                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach && connections >= 2)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var detaching = channel.DetachAsync();

            await UtsClients.PollUntil(
                () => DetachesOn(sent, 1).Count == 1,
                "the DETACH on the first transport");

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, StateTimeout);

            mockWs.SimulateDisconnect();
            await reconnected;

            await UtsClients.PollUntil(
                () => DetachesOn(sent, 2).Count >= 1,
                "RTN19b - the pending DETACH going out again");

            await UtsClients.AwaitChannelState(channel, ChannelState.Detached, StateTimeout);

            var resent = DetachesOn(sent, 2);
            resent[0].Channel.Should().Be(ChannelName);
            channel.State.Should().Be(ChannelState.Detached);

            // As with the attach, the returned task is D47's subject, not this test's.
            var detachResult = await detaching;
            Output.WriteLine("detach() reported IsSuccess=" + detachResult.IsSuccess);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

        /// <summary>
        /// Answers the ATTACH and nothing else, so a publish stays pending.
        /// </summary>
        private static void AnswerAttachOnly(MockWebSocket mockWs, ProtocolMessage msg)
        {
            if (msg.Action == ProtocolMessage.MessageAction.Attach)
            {
                mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
            }
        }

        private static void Record(
            List<(int Connection, ProtocolMessage Message)> sent,
            int connection,
            ProtocolMessage msg)
        {
            lock (sent)
            {
                sent.Add((connection, msg));
            }
        }

        /// <summary>
        /// The frames of one action that went out on a given transport. The mock has no per-
        /// connection record of its own, so the connection number is stamped on as they are sent.
        /// </summary>
        private static List<ProtocolMessage> On(
            List<(int Connection, ProtocolMessage Message)> sent,
            int connection,
            ProtocolMessage.MessageAction action)
        {
            lock (sent)
            {
                var matches = new List<ProtocolMessage>();
                foreach (var entry in sent)
                {
                    if (entry.Connection == connection && entry.Message.Action == action)
                    {
                        matches.Add(entry.Message);
                    }
                }

                return matches;
            }
        }

        private static List<ProtocolMessage> MessagesOn(
            List<(int Connection, ProtocolMessage Message)> sent, int connection)
            => On(sent, connection, ProtocolMessage.MessageAction.Message);

        private static List<ProtocolMessage> AttachesOn(
            List<(int Connection, ProtocolMessage Message)> sent, int connection)
            => On(sent, connection, ProtocolMessage.MessageAction.Attach);

        private static List<ProtocolMessage> DetachesOn(
            List<(int Connection, ProtocolMessage Message)> sent, int connection)
            => On(sent, connection, ProtocolMessage.MessageAction.Detach);
    }
}
