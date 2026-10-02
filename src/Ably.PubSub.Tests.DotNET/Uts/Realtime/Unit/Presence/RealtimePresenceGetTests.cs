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

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Presence
{
    /// <summary>
    /// Derived from uts/realtime/unit/presence/realtime_presence_get.md in ably/specification.
    ///
    /// Spec points: RTP11, RTP11a, RTP11b, RTP11c, RTP11c1, RTP11c2, RTP11c3, RTP11d
    ///
    /// <para>
    /// The whole file turns on one behaviour: by default <c>get()</c> waits for the presence SYNC to
    /// finish, so the tests hold the SYNC back, check the call has not resolved, and then release
    /// it. A SYNC whose <c>channelSerial</c> cursor is empty is the last one.
    /// </para>
    ///
    /// <para>
    /// <c>get()</c> is <c>Presence.GetAsync</c>, whose <c>(clientId, connectionId, waitForSync)</c>
    /// overload takes the spec's three named arguments directly. It returns
    /// <c>IEnumerable&lt;PresenceMessage&gt;</c> rather than a list, so the assertions enumerate once
    /// into a local.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceGetTests : UtsTestBase
    {
        private const string ChannelName = "test-RTP11";

        public RealtimePresenceGetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP11a/get-returns-members-single-sync-0
        [Fact]
        public async Task RTP11a_GetReturnsMembersSingleSync()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            // ATTACHED promises a sync but none is sent yet, so get() has to wait for it.
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                }
            };

            var channel = await AttachedChannel(client, mockWs);

            var getTask = channel.Presence.GetAsync();

            getTask.IsCompleted.Should().BeFalse("the sync has not arrived");

            // An empty cursor after the colon marks the sync complete.
            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100, "a"),
                ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100, "b"),
            }));

            var members = (await getTask).ToList();

            members.Should().HaveCount(2);
            members.Select(m => m.ClientId).OrderBy(id => id)
                .Should().Equal("alice", "bob");
        }

        // UTS: realtime/unit/RTP11a/get-waits-for-multi-sync-1
        [Fact]
        public async Task RTP11a_GetWaitsForMultiSync()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                }
            };

            var channel = await AttachedChannel(client, mockWs);

            var getTask = channel.Presence.GetAsync();

            getTask.IsCompleted.Should().BeFalse();

            // A non-empty cursor means more SYNC messages are coming.
            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:cursor1", new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
            }));

            getTask.IsCompleted.Should().BeFalse("RTP11c1 - the sync is not finished");

            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray
            {
                ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100),
            }));

            var members = (await getTask).ToList();

            members.Should().HaveCount(2, "one member came from each SYNC message");
            members.Select(m => m.ClientId).OrderBy(id => id)
                .Should().Equal("alice", "bob");
        }

        // UTS: realtime/unit/RTP11c1/get-no-wait-returns-immediately-0
        [Fact]
        public async Task RTP11c1_GetNoWaitReturnsImmediately()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));

                    // A partial sync: the cursor is non-empty, so it is still in progress.
                    mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:cursor1", new JArray
                    {
                        ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
                    }));
                }
            };

            var channel = await AttachedChannel(client, mockWs);

            var members = (await channel.Presence.GetAsync(waitForSync: false)).ToList();

            members.Should().HaveCount(1, "whatever is known so far, without waiting");
            members[0].ClientId.Should().Be("alice");
        }

        // UTS: realtime/unit/RTP11c2/get-filtered-by-clientid-0
        [Fact]
        public async Task RTP11c2_GetFilteredByClientId()
        {
            var channel = await ChannelWithCompleteSync(new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
                ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100),
                ProtocolMessages.PresenceEntry(1, "alice", "c3", "c3:0:0", 100),
            });

            var members = (await channel.Presence.GetAsync(clientId: "alice")).ToList();

            members.Should().HaveCount(2);
            members.Should().OnlyContain(m => m.ClientId == "alice");
        }

        // UTS: realtime/unit/RTP11c3/get-filtered-by-connectionid-0
        [Fact]
        public async Task RTP11c3_GetFilteredByConnectionId()
        {
            var channel = await ChannelWithCompleteSync(new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
                ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100),
                ProtocolMessages.PresenceEntry(1, "carol", "c1", "c1:0:1", 100),
            });

            var members = (await channel.Presence.GetAsync(connectionId: "c1")).ToList();

            members.Should().HaveCount(2);
            members.Should().OnlyContain(m => m.ConnectionId == "c1");
        }

        // UTS: realtime/unit/RTP11b/get-implicitly-attaches-0
        //
        // DEVIATION. RTP11b has been replaced by RTP11e (features.md:937), which requires get() to
        // run the ensure-active-channel procedure, RTL33. For a channel in INITIALIZED, RTL33b is
        // explicit: "perform an implicit attach per RTL4 **and wait for it to complete**". This SDK
        // starts the attach and returns without waiting - measured, the channel is still ATTACHING
        // when get(waitForSync: false) has resolved. The attach itself does happen, so this is a
        // missing wait rather than a missing attach. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP11b_GetImplicitlyAttaches()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized, "nothing has attached it yet");

            var members = await channel.Presence.GetAsync(waitForSync: false);

            channel.State.Should().Be(ChannelState.Attached, "RTP11b - get() attaches the channel");
            members.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP11d/get-suspended-errors-default-0
        [Fact]
        public async Task RTP11d_GetSuspendedErrorsByDefault()
        {
            var channel = await SuspendedChannel();

            Func<Task> act = () => channel.Presence.GetAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Should().NotBeNull();
            error.Code.Should().Be(91005);
        }

        // UTS: realtime/unit/RTP11d/get-suspended-no-wait-returns-1
        [Fact]
        public async Task RTP11d_GetSuspendedNoWaitReturnsMembers()
        {
            var channel = await SuspendedChannel();

            var members = await channel.Presence.GetAsync(waitForSync: false);

            members.Should().NotBeNull(
                "RTP11d - waitForSync:false returns what is known rather than erroring");
        }

        /// <summary>
        /// A channel attached with a presence sync already delivered and complete, which is the
        /// starting point several of these tests share.
        /// </summary>
        private async Task<IRealtimeChannel> ChannelWithCompleteSync(JArray presence)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                    mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", presence));
                }
            };

            return await AttachedChannel(client, mockWs);
        }

        /// <summary>
        /// A channel that reached SUSPENDED, which is what RTP11d is about. The connection is taken
        /// to SUSPENDED by refusing every reconnection attempt and letting the TestClock run past
        /// connectionStateTtl, and the channel follows it there per RTL3c.
        /// </summary>
        private async Task<IRealtimeChannel> SuspendedChannel()
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
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                    mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray
                    {
                        ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
                    }));
                }
            };

            var channel = await AttachedChannel(client, mockWs);

            mockWs.ActiveConnection.SimulateDisconnect();

            // The advance comes after the disconnect has been recorded - connectionStateTtl is
            // measured from that instant against NowFunc.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            return channel;
        }

        private async Task<IRealtimeChannel> AttachedChannel(
            PubSubRealtimeClient client,
            MockWebSocket mockWs)
        {
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            return channel;
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
