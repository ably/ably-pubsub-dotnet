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
    /// Derived from uts/realtime/unit/channels/channel_connection_state.md in ably/specification.
    ///
    /// Spec points: RTL3a, RTL3b, RTL3c, RTL3d, RTL3e, RTL4c1
    ///
    /// <para>
    /// What the connection does to its channels. DISCONNECTED is deliberately invisible to them
    /// (RTL3e), FAILED and CLOSED take attached and attaching channels with them, SUSPENDED is
    /// mirrored, and a fresh CONNECTED re-attaches whatever was attached or suspended.
    /// </para>
    ///
    /// <para>
    /// Reaching SUSPENDED needs the connection to outlive <c>connectionStateTtl</c> with every
    /// reconnection refused. That interval is measured against <c>NowFunc</c>, so a
    /// <see cref="TestClock"/> advance covers it; the retry waits are real timers and are shortened
    /// in the options. The advance comes only once a reconnection attempt proves the disconnect has
    /// been recorded - advancing first leaves zero measured elapsed time.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelConnectionStateTests : UtsTestBase
    {
        public ChannelConnectionStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL3e/disconnected-attached-noop-0
        [Fact]
        public async Task RTL3e_DisconnectedHasNoEffectOnAttachedChannel()
        {
            const string ChannelName = "test-RTL3e-attached";

            var (mockWs, client, channel) = await AttachedChannel(ChannelName, holdDisconnected: true);

            channel.State.Should().Be(ChannelState.Attached);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            await DisconnectAndSettle(mockWs, client);

            channel.State.Should().Be(ChannelState.Attached, "RTL3e");
            channelStateChanges.Should().BeEmpty("RTL3e - the channel is told nothing");
        }

        // UTS: realtime/unit/RTL3e/disconnected-attaching-noop-1
        [Fact]
        public async Task RTL3e_DisconnectedHasNoEffectOnAttachingChannel()
        {
            const string ChannelName = "test-RTL3e-attaching";

            var (mockWs, client, channel) = await AttachingChannel(ChannelName, holdDisconnected: true);

            channel.State.Should().Be(ChannelState.Attaching);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            await DisconnectAndSettle(mockWs, client);

            channel.State.Should().Be(ChannelState.Attaching, "RTL3e");
            channelStateChanges.Should().BeEmpty();
        }

        // UTS: realtime/unit/RTL3a/failed-attached-to-failed-0
        [Fact]
        public async Task RTL3a_FailedConnectionFailsAttachedChannel()
        {
            const string ChannelName = "test-RTL3a-attached";

            var (mockWs, client, channel) = await AttachedChannel(ChannelName, holdDisconnected: true);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            var channelFailed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.ErrorMessage(40198, "Fatal connection error", 400));
            await channelFailed;

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40198);

            var failedChange = channelStateChanges.First(c => c.Current == ChannelState.Failed);
            failedChange.Previous.Should().Be(ChannelState.Attached);
            failedChange.Error.Should().NotBeNull();
            failedChange.Error.Code.Should().Be(40198);
        }

        // UTS: realtime/unit/RTL3a/failed-attaching-to-failed-1
        [Fact]
        public async Task RTL3a_FailedConnectionFailsAttachingChannel()
        {
            const string ChannelName = "test-RTL3a-attaching";

            var (mockWs, client, channel) = await AttachingChannel(ChannelName, holdDisconnected: true);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            var attachTask = channel.AttachAsync();

            var channelFailed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.ErrorMessage(40198, "Fatal connection error", 400));
            await channelFailed;

            var result = await attachTask;
            result.IsSuccess.Should().BeFalse("the pending attach fails with the connection");

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();

            var failedChange = channelStateChanges.First(c => c.Current == ChannelState.Failed);
            failedChange.Previous.Should().Be(ChannelState.Attaching);
        }

        // UTS: realtime/unit/RTL3a/other-states-unaffected-2
        [Fact]
        public async Task RTL3a_ChannelsInOtherStatesUnaffectedByFailedConnection()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var initializedChannel = client.Channels.Get("test-RTL3a-init");
            var detachedChannel = client.Channels.Get("test-RTL3a-detached");

            initializedChannel.State.Should().Be(ChannelState.Initialized);

            await detachedChannel.AttachAsync();
            await detachedChannel.DetachAsync();
            detachedChannel.State.Should().Be(ChannelState.Detached);

            var initChanges = new List<ChannelStateChange>();
            var detachedChanges = new List<ChannelStateChange>();
            initializedChannel.On(change => initChanges.Add(change));
            detachedChannel.On(change => detachedChanges.Add(change));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var connectionFailed = UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SendToClientAndClose(
                ProtocolMessages.ErrorMessage(40198, "Fatal connection error", 400));

            await connectionFailed;

            initializedChannel.State.Should().Be(ChannelState.Initialized, "RTL3a");
            detachedChannel.State.Should().Be(ChannelState.Detached, "RTL3a");
            initChanges.Should().BeEmpty();
            detachedChanges.Should().BeEmpty();
        }

        // UTS: realtime/unit/RTL3b/closed-attached-to-detached-0
        //
        // DEVIATION, D38 - the channel passes through DETACHING, which the application never asked
        // for, so the DETACHED transition's previous state is DETACHING rather than the ATTACHED or
        // ATTACHING RTL3b describes. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL3b_ClosedConnectionDetachesAttachedChannel()
        {
            const string ChannelName = "test-RTL3b-attached";

            var (mockWs, client, channel) = await AttachedChannel(ChannelName, holdDisconnected: false);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            await CloseConnection(mockWs, client);

            channel.State.Should().Be(ChannelState.Detached, "RTL3b");

            var detachedChange = channelStateChanges.First(c => c.Current == ChannelState.Detached);
            detachedChange.Previous.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTL3b/closed-attaching-to-detached-1
        //
        // DEVIATION, D38 - the channel passes through DETACHING, which the application never asked
        // for, so the DETACHED transition's previous state is DETACHING rather than the ATTACHED or
        // ATTACHING RTL3b describes. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL3b_ClosedConnectionDetachesAttachingChannel()
        {
            const string ChannelName = "test-RTL3b-attaching";

            var (mockWs, client, channel) = await AttachingChannel(ChannelName, holdDisconnected: false);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            var attachTask = channel.AttachAsync();

            await CloseConnection(mockWs, client);

            var result = await attachTask;
            result.IsSuccess.Should().BeFalse("the pending attach fails with the close");

            channel.State.Should().Be(ChannelState.Detached);

            var detachedChange = channelStateChanges.First(c => c.Current == ChannelState.Detached);
            detachedChange.Previous.Should().Be(ChannelState.Attaching);
        }

        // UTS: realtime/unit/RTL3c/suspended-attached-to-suspended-0
        [Fact]
        public async Task RTL3c_SuspendedConnectionSuspendsAttachedChannel()
        {
            const string ChannelName = "test-RTL3c-attached";

            var (mockWs, client, channel, clock) = await SuspendableChannel(ChannelName, attachFully: true);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            await DriveToSuspended(mockWs, client, clock);

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            channel.State.Should().Be(ChannelState.Suspended);

            var suspendedChange = channelStateChanges.First(c => c.Current == ChannelState.Suspended);
            suspendedChange.Previous.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTL3c/suspended-attaching-to-suspended-1
        [Fact]
        public async Task RTL3c_SuspendedConnectionSuspendsAttachingChannel()
        {
            const string ChannelName = "test-RTL3c-attaching";

            var (mockWs, client, channel, clock) = await SuspendableChannel(ChannelName, attachFully: false);

            channel.State.Should().Be(ChannelState.Attaching);

            var channelStateChanges = new List<ChannelStateChange>();
            channel.On(change => channelStateChanges.Add(change));

            await DriveToSuspended(mockWs, client, clock);

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            channel.State.Should().Be(ChannelState.Suspended);

            var suspendedChange = channelStateChanges.First(c => c.Current == ChannelState.Suspended);
            suspendedChange.Previous.Should().Be(ChannelState.Attaching);
        }

        // UTS: realtime/unit/RTL3d/reattach-attached-with-serial-0
        [Fact]
        public async Task RTL3d_ReattachesAttachedChannelCarryingChannelSerial()
        {
            const string ChannelName = "test-RTL3d-serial";

            var attachMessages = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                        ChannelName,
                        new Dictionary<string, JToken> { ["channelSerial"] = "serial-001" }));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            attachMessages.Should().HaveCount(1);

            var channelStates = UtsClients.RecordChannelStates(channel);

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            channel.State.Should().Be(ChannelState.Attached);
            attachMessages.Should().HaveCount(2);
            attachMessages[1].ChannelSerial.Should().Be(
                "serial-001",
                "RTL4c1 - the re-attach carries the serial it left off at");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(channelStates),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue();
        }

        // UTS: realtime/unit/RTL3d/reattach-suspended-channels-1
        [Fact]
        public async Task RTL3d_ReattachesSuspendedChannelOnConnected()
        {
            const string ChannelName = "test-RTL3d-suspended";

            var attachCount = 0;
            var refuseConnections = false;
            var clock = new TestClock();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (refuseConnections)
                {
                    conn.RespondWithRefused();
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionStateTtl: 5000,
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

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

            refuseConnections = true;
            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(30));

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            var channelStates = UtsClients.RecordChannelStates(channel);

            // Let the next suspended retry through.
            refuseConnections = false;

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(30));

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(30));

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().BeGreaterOrEqualTo(2, "RTL3d re-attached it");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(channelStates),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue();
        }

        // UTS: realtime/unit/RTL3d/init-detached-not-reattached-2
        [Fact]
        public async Task RTL3d_InitializedAndDetachedChannelsNotReattached()
        {
            var attachedNames = new List<string>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachedNames.Add(msg.Channel);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var initializedChannel = client.Channels.Get("test-RTL3d-init");
            var detachedChannel = client.Channels.Get("test-RTL3d-detached");
            var attachedChannel = client.Channels.Get("test-RTL3d-attached");

            await detachedChannel.AttachAsync();
            await detachedChannel.DetachAsync();
            await attachedChannel.AttachAsync();

            attachedNames.Clear();

            var reattached = UtsClients.NextChannelState(attachedChannel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            attachedNames.Should().Contain("test-RTL3d-attached");
            attachedNames.Should().NotContain("test-RTL3d-init", "RTL3d - INITIALIZED is left alone");
            attachedNames.Should().NotContain("test-RTL3d-detached", "RTL3d - DETACHED is left alone");

            initializedChannel.State.Should().Be(ChannelState.Initialized);
            detachedChannel.State.Should().Be(ChannelState.Detached);
        }

        // UTS: realtime/unit/RTL3d/multiple-channels-reattached-3
        [Fact]
        public async Task RTL3d_MultipleChannelsReattachedOnConnected()
        {
            var attachedNames = new List<string>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachedNames.Add(msg.Channel);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channelA = client.Channels.Get("test-RTL3d-multi-a");
            var channelB = client.Channels.Get("test-RTL3d-multi-b");
            var channelC = client.Channels.Get("test-RTL3d-multi-c");

            await channelA.AttachAsync();
            await channelB.AttachAsync();
            await channelC.AttachAsync();

            attachedNames.Clear();

            var reattached = UtsClients.NextChannelState(channelC, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(
                () => attachedNames.Count >= 3,
                "all three channels re-attached");

            attachedNames.Should().Contain("test-RTL3d-multi-a");
            attachedNames.Should().Contain("test-RTL3d-multi-b");
            attachedNames.Should().Contain("test-RTL3d-multi-c");

            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);
            channelC.State.Should().Be(ChannelState.Attached);
        }

        private async Task DisconnectAndSettle(MockWebSocket mockWs, PubSubRealtimeClient client)
        {
            var disconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Disconnected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SimulateDisconnect();

            await disconnected;
        }

        private async Task CloseConnection(MockWebSocket mockWs, PubSubRealtimeClient client)
        {
            var closed = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Closed,
                TimeSpan.FromSeconds(5));

            client.Close();

            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());

            await closed;
        }

        private async Task DriveToSuspended(
            MockWebSocket mockWs,
            PubSubRealtimeClient client,
            TestClock clock)
        {
            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// An attached channel.
        ///
        /// <para>
        /// <paramref name="holdDisconnected"/> keeps the connection in DISCONNECTED once it is
        /// dropped, which takes two things rather than one: a long
        /// <c>DisconnectedRetryTimeout</c> is not enough on its own, because a transport
        /// disconnect earns RTN15a's *immediate* retry, which ignores it. Later connection
        /// attempts are refused as well.
        /// </para>
        /// </summary>
        private async Task<(MockWebSocket MockWs, PubSubRealtimeClient Client, IRealtimeChannel Channel)>
            AttachedChannel(string channelName, bool holdDisconnected)
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (holdDisconnected && connectionAttemptCount > 1)
                {
                    conn.RespondWithRefused();
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                if (holdDisconnected)
                {
                    options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
                }
            });

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

            return (mockWs, client, channel);
        }

        /// <summary>A channel left in ATTACHING because the server never answers the ATTACH.</summary>
        private async Task<(MockWebSocket MockWs, PubSubRealtimeClient Client, IRealtimeChannel Channel)>
            AttachingChannel(string channelName, bool holdDisconnected)
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (holdDisconnected && connectionAttemptCount > 1)
                {
                    conn.RespondWithRefused();
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10);
                if (holdDisconnected)
                {
                    options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
                }
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            return (mockWs, client, channel);
        }

        private async Task<(MockWebSocket MockWs, PubSubRealtimeClient Client, IRealtimeChannel Channel, TestClock Clock)>
            SuspendableChannel(string channelName, bool attachFully)
        {
            var clock = new TestClock();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (connectionAttemptCount == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionStateTtl: 5000,
                        maxIdleInterval: 0));
                }
                else
                {
                    conn.RespondWithRefused();
                }
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10);
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            if (attachFully)
            {
                mockWs.OnMessageFromClient = msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                };
            }

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);

            if (attachFully)
            {
                await channel.AttachAsync();
            }
            else
            {
                channel.Attach();
                await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);
            }

            return (mockWs, client, channel, clock);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
