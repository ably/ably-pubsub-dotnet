using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Derived from uts/realtime/unit/channels/channel_detach.md in ably/specification.
    ///
    /// Spec points: RTL5, RTL5a, RTL5b, RTL5d, RTL5f, RTL5i, RTL5j, RTL5k, RTL5l
    ///
    /// <para>
    /// The spec's last section is marked <c>[REMOVED]</c> and carries no test id, so there is
    /// nothing to translate for it.
    /// </para>
    ///
    /// <para>
    /// Four of these are gated on three findings - D40, D41 and D42 - which between them say that
    /// the ordinary detach path works and the three conditional ones around it do not. See
    /// Uts/deviations.md.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelDetachTests : UtsTestBase
    {
        public ChannelDetachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL5a/detach-initialized-noop-0
        [Fact]
        public async Task RTL5a_DetachWhenInitializedIsNoOp()
        {
            const string ChannelName = "test-RTL5a-init";

            var (_, client, detachMessages) = ConnectedClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            await channel.DetachAsync();

            channel.State.Should().BeOneOf(ChannelState.Initialized, ChannelState.Detached);
            detachMessages.Should().BeEmpty("nothing was attached, so nothing to detach");
        }

        // UTS: realtime/unit/RTL5a/detach-already-detached-noop-1
        [Fact]
        public async Task RTL5a_DetachWhenAlreadyDetachedIsNoOp()
        {
            const string ChannelName = "test-RTL5a-detached";

            var (_, client, detachMessages) = ConnectedClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            detachMessages.Should().HaveCount(1);

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            detachMessages.Should().HaveCount(1, "RTL5a - nothing further is sent");
        }

        // UTS: realtime/unit/RTL5i/detach-while-detaching-0
        [Fact]
        public async Task RTL5i_DetachWhileDetachingWaitsForCompletion()
        {
            const string ChannelName = "test-RTL5i-detaching";

            var detachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachCount = detachCount + 1;
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var first = channel.DetachAsync();
            await UtsClients.AwaitChannelState(channel, ChannelState.Detaching);

            var second = channel.DetachAsync();

            mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));

            await first;
            await second;

            channel.State.Should().Be(ChannelState.Detached);
            detachCount.Should().Be(1, "RTL5i - the second detach joins the first");
        }

        // UTS: realtime/unit/RTL5i/detach-while-attaching-1
        //
        // DEVIATION, D40 - the mirror of D39. RTL5i requires a detach issued while the channel is
        // ATTACHING to happen "after the completion of the pending request"; measured, the detach
        // resolves with the channel still ATTACHING and no DETACH on the wire at all. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RTL5i_DetachWhileAttachingWaitsThenDetaches()
        {
            const string ChannelName = "test-RTL5i-attaching";

            var clientMessages = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach
                    || msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    clientMessages.Add(msg);
                }

                if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var attach = channel.AttachAsync();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            var detach = channel.DetachAsync();

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await attach;
            await detach;

            channel.State.Should().Be(ChannelState.Detached);
            clientMessages.Should().HaveCount(2);
            clientMessages[0].Action.Should().Be(ProtocolMessage.MessageAction.Attach);
            clientMessages[1].Action.Should().Be(ProtocolMessage.MessageAction.Detach);
        }

        // UTS: realtime/unit/RTL5b/detach-failed-errors-0
        [Fact]
        public async Task RTL5b_DetachFromFailedStateErrors()
        {
            const string ChannelName = "test-RTL5b";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        40160,
                        "Denied",
                        401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeFalse();
            channel.State.Should().Be(ChannelState.Failed);

            var detachResult = await channel.DetachAsync();

            detachResult.IsSuccess.Should().BeFalse("RTL5b");
            detachResult.Error.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Failed, "the state is unchanged");
        }

        // UTS: realtime/unit/RTL5j/detach-suspended-to-detached-0
        //
        // DEVIATION, D41. RTL5j: "If the channel state is SUSPENDED, the detach request transitions
        // the channel immediately to the DETACHED state." Measured: the channel stayed SUSPENDED.
        // See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL5j_DetachFromSuspendedGoesToDetached()
        {
            const string ChannelName = "test-RTL5j";

            var detachCount = 0;
            var mockWs = ConnectingMock();

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200);
                options.ChannelRetryTimeout = TimeSpan.FromMinutes(10);
            });

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachCount = detachCount + 1;
                }

                // The ATTACH goes unanswered so the attach times out into SUSPENDED.
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeFalse();
            channel.State.Should().Be(ChannelState.Suspended);

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached, "RTL5j");
            detachCount.Should().Be(0, "RTL5j - the transition is immediate, nothing is sent");
        }

        // UTS: realtime/unit/RTL5l/detach-not-connected-immediate-0
        [Fact]
        public async Task RTL5l_DetachWhileConnectingIsImmediate()
        {
            const string ChannelName = "test-RTL5l-connecting";

            var detachCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Never answered, so the connection stays CONNECTING.
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachCount = detachCount + 1;
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached, "RTL5l");
            detachCount.Should().Be(0, "the transport cannot carry it");
        }

        // UTS: realtime/unit/RTL5l/detach-attached-when-disconnected-1
        [Fact]
        public async Task RTL5l_DetachAttachedChannelWhenDisconnected()
        {
            const string ChannelName = "test-RTL5l-disconnected";

            var detachMessages = new List<ProtocolMessage>();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (connectionAttemptCount > 1)
                {
                    conn.RespondWithRefused();
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachMessages.Add(msg);
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var disconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SimulateDisconnect();
            await disconnected;

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached, "RTL5l");
            detachMessages.Should().BeEmpty("the transport is unavailable");
        }

        // UTS: realtime/unit/RTL5d/normal-detach-flow-0
        [Fact]
        public async Task RTL5d_NormalDetachFlow()
        {
            const string ChannelName = "test-RTL5d";

            var (mockWs, client, detachMessages) = ConnectedClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            ChannelState? stateDuringDetach = null;
            channel.On(change =>
            {
                if (stateDuringDetach == null)
                {
                    stateDuringDetach = change.Current;
                }
            });

            await channel.DetachAsync();

            stateDuringDetach.Should().Be(ChannelState.Detaching);
            channel.State.Should().Be(ChannelState.Detached);

            detachMessages.Should().HaveCount(1);
            detachMessages[0].Action.Should().Be(ProtocolMessage.MessageAction.Detach);
            detachMessages[0].Channel.Should().Be(ChannelName);
        }

        // UTS: realtime/unit/RTL5f/timeout-returns-previous-state-0
        [Fact]
        public async Task RTL5f_DetachTimeoutReturnsToPreviousState()
        {
            const string ChannelName = "test-RTL5f";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }

                // The DETACH goes unanswered.
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var result = await channel.DetachAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Attached, "RTL5f - back where it started");
        }

        // UTS: realtime/unit/RTL5k/attached-while-detaching-0
        //
        // DEVIATION, D42. RTL5k requires an ATTACHED arriving while DETACHING or DETACHED to
        // provoke a fresh DETACH. Measured: only the first DETACH was ever sent, so a channel whose
        // detach is answered with an ATTACHED is stranded in DETACHING. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL5k_AttachedWhileDetachingSendsNewDetach()
        {
            const string ChannelName = "test-RTL5k-detaching";

            var detachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachCount = detachCount + 1;
                    if (detachCount == 1)
                    {
                        // Instead of DETACHED, the server sends another ATTACHED.
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                    }
                    else
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                    }
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            detachCount.Should().Be(2, "RTL5k - the unexpected ATTACHED provokes another DETACH");
        }

        // UTS: realtime/unit/RTL5k/attached-while-detached-1
        //
        // DEVIATION, D42, the DETACHED half. An unsolicited ATTACHED for a channel the client has
        // already detached is ignored rather than answered with a DETACH, so the client and the
        // server disagree about whether the channel is attached. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL5k_AttachedWhileDetachedSendsDetach()
        {
            const string ChannelName = "test-RTL5k-detached";

            var detachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachCount = detachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            detachCount.Should().Be(1);

            // An unsolicited ATTACHED for a channel the client has detached.
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await UtsClients.PollUntil(
                () => detachCount >= 2,
                "RTL5k - the client detaches again");

            detachCount.Should().Be(2);
            channel.State.Should().Be(ChannelState.Detached);
        }

        // UTS: realtime/unit/RTL5/detach-state-change-events-0
        [Fact]
        public async Task RTL5_DetachEmitsStateChangeEvents()
        {
            const string ChannelName = "test-RTL5-events";

            var (_, client, _) = ConnectedClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var stateChanges = new List<ChannelStateChange>();
            channel.On(change => stateChanges.Add(change));

            await channel.DetachAsync();

            stateChanges.Should().HaveCountGreaterOrEqualTo(2);

            stateChanges[0].Current.Should().Be(ChannelState.Detaching);
            stateChanges[0].Previous.Should().Be(ChannelState.Attached);
            stateChanges[0].Event.Should().Be(ChannelEvent.Detaching);

            stateChanges[1].Current.Should().Be(ChannelState.Detached);
            stateChanges[1].Previous.Should().Be(ChannelState.Detaching);
            stateChanges[1].Event.Should().Be(ChannelEvent.Detached);
        }

        private (MockWebSocket MockWs, PubSubRealtimeClient Client, List<ProtocolMessage> DetachMessages)
            ConnectedClient(string channelName)
        {
            var detachMessages = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                }
            };

            client.Connect();
            return (mockWs, client, detachMessages);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
