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
    /// Derived from uts/realtime/unit/presence/realtime_presence_channel_state.md in
    /// ably/specification.
    ///
    /// Spec points: RTP1, RTP5a, RTP5b, RTP5f, RTP13, RTP19a, RTL9, RTL9a, RTL11, RTL11a
    ///
    /// <para>
    /// How the presence map reacts to the channel moving under it: an attach with or without
    /// HAS_PRESENCE, a detach, a failure, a suspension, and what happens to presence actions queued
    /// while the channel was still attaching.
    /// </para>
    ///
    /// <para>
    /// <c>syncComplete</c> is <c>Presence.SyncComplete</c>, which reads
    /// <c>MembersMap.SyncCompleted &amp;&amp; !SyncInProgress</c> (<c>Presence.cs:45</c>).
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceChannelStateTests : UtsTestBase
    {
        private const string ChannelName = "test-presence-channel-state";

        public RealtimePresenceChannelStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP1/has-presence-triggers-sync-0
        [Fact]
        public async Task RTP1_HasPresenceTriggersSync()
        {
            var (_, channel) = await AttachedChannel(withPresence: true, sync: new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
            });

            var members = (await channel.Presence.GetAsync()).ToList();

            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");
            RealtimePresenceOf(channel).SyncComplete.Should().BeTrue();
        }

        // UTS: realtime/unit/RTP1/no-has-presence-empty-1
        [Fact]
        public async Task RTP1_NoHasPresenceMeansEmptyPresence()
        {
            var (_, channel) = await AttachedChannel(withPresence: false);

            var members = (await channel.Presence.GetAsync()).ToList();

            members.Should().BeEmpty();
            RealtimePresenceOf(channel).SyncComplete.Should().BeTrue(
                "RTP1 - with no HAS_PRESENCE the map is in sync immediately");
        }

        // UTS: realtime/unit/RTP1/no-has-presence-clears-existing-2
        [Fact]
        public async Task RTP1_NoHasPresenceClearsExistingMembers()
        {
            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    if (attachCount == 1)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray
                        {
                            ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
                            ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100),
                        }));
                    }
                    else
                    {
                        // The reattach promises no presence at all.
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                    }
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            (await channel.Presence.GetAsync()).Should().HaveCount(2);

            var leaveEvents = new List<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, msg => leaveEvents.Add(msg));

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(() => leaveEvents.Count >= 2, "a LEAVE for each stale member");

            (await channel.Presence.GetAsync()).Should().BeEmpty();

            leaveEvents.Should().HaveCount(2);
            leaveEvents.Should().Contain(e => e.ClientId == "alice");
            leaveEvents.Should().Contain(e => e.ClientId == "bob");
            leaveEvents.Should().OnlyContain(e => e.Id == null, "RTP19a");
        }

        // UTS: realtime/unit/RTP5a/detached-clears-presence-maps-0
        [Fact]
        public async Task RTP5a_DetachedClearsPresenceMaps()
        {
            var (mockWs, channel) = await AttachedChannel(withPresence: true, sync: new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
            });

            (await channel.Presence.GetAsync()).Should().HaveCount(1);

            var leaveEvents = new List<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, msg => leaveEvents.Add(msg));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            var detached = UtsClients.AwaitChannelState(channel, ChannelState.Detached);
            channel.Detach();
            await detached;

            channel.State.Should().Be(ChannelState.Detached);
            leaveEvents.Should().BeEmpty("RTP5a - clearing the map on DETACHED emits nothing");

            // Read straight off the map rather than through get(). The spec's version calls
            // get(waitForSync: false) on the detached channel, which under RTP11e/RTL33b would
            // re-attach and wait; this SDK instead raises 90001 for a DETACHED channel. That
            // disagreement is about RTP11e, not RTP5a, and is noted with D28 - RTP5a's subject is
            // simply that the maps were cleared, which is what is asserted.
            var presence = RealtimePresenceOf(channel);
            presence.MembersMap.Values.Should().BeEmpty();
            presence.InternalMembersMap.Values.Should().BeEmpty("RTP5a clears both maps");
        }

        // UTS: realtime/unit/RTP5a/failed-clears-presence-maps-1
        [Fact]
        public async Task RTP5a_FailedClearsPresenceMaps()
        {
            var (mockWs, channel) = await AttachedChannel(withPresence: true, sync: new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
            });

            (await channel.Presence.GetAsync()).Should().HaveCount(1);

            var leaveEvents = new List<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, msg => leaveEvents.Add(msg));

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 50000, "Channel error", 500));
            await failed;

            channel.State.Should().Be(ChannelState.Failed);
            leaveEvents.Should().BeEmpty("RTP5a - clearing the map on FAILED emits nothing");
        }

        // UTS: realtime/unit/RTP5b/attached-sends-queued-presence-0
        [Fact]
        public async Task RTP5b_AttachedSendsQueuedPresence()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            ProtocolMessage attachMessage = null;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    // Held back, so the presence action below is queued rather than sent.
                    attachMessage = msg;
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            await UtsClients.PollUntil(() => attachMessage != null, "the ATTACH reached the server");

            var enterTask = channel.Presence.EnterAsync("queued");

            capturedPresence.Should().BeEmpty("nothing goes out while the channel is attaching");

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            var result = await enterTask;
            result.IsSuccess.Should().BeTrue();

            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            capturedPresence[0].Presence[0].Data.Should().Be("queued");
        }

        // UTS: realtime/unit/RTP5f/suspended-maintains-presence-map-0
        [Fact]
        public async Task RTP5f_SuspendedMaintainsPresenceMap()
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
                        ProtocolMessages.PresenceEntry(1, "bob", "c2", "c2:0:0", 100),
                    }));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            (await channel.Presence.GetAsync()).Should().HaveCount(2);

            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            var membersDuringSuspended = await channel.Presence.GetAsync(waitForSync: false);

            membersDuringSuspended.Should().HaveCount(
                2,
                "RTP5f - the map survives a suspension so the caller can see who was there");
        }

        // UTS: realtime/unit/RTP13/sync-complete-attribute-0
        [Fact]
        public async Task RTP13_SyncCompleteAttribute()
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

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            RealtimePresenceOf(channel).SyncComplete.Should().BeFalse("the sync has not arrived");

            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", new JArray
            {
                ProtocolMessages.PresenceEntry(1, "alice", "c1", "c1:0:0", 100),
            }));

            await UtsClients.PollUntil(
                () => RealtimePresenceOf(channel).SyncComplete,
                "the sync completed");

            RealtimePresenceOf(channel).SyncComplete.Should().BeTrue();
        }

        // UTS: realtime/unit/RTL9/presence-attribute-0
        [Fact]
        public void RTL9_PresenceAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());
            var channel = client.Channels.Get(ChannelName);

            channel.Presence.Should().NotBeNull();
            channel.Presence.Should().BeOfType<Ably.PubSub.Realtime.Presence>();
            channel.Presence.Should().BeSameAs(channel.Presence, "RTL9a - the same instance");
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-detached-0
        [Fact]
        public async Task RTL11_PresenceFailsOnDetachedChannel()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(withPresence: false);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);
                }
            };

            var detached = UtsClients.AwaitChannelState(channel, ChannelState.Detached);
            channel.Detach();
            await detached;

            channel.State.Should().Be(ChannelState.Detached);

            var result = await channel.Presence.EnterAsync("queued-enter");

            result.IsSuccess.Should().BeFalse("RTL11 - a DETACHED channel cannot take presence");
            result.Error.Should().NotBeNull();
            capturedPresence.Should().BeEmpty("nothing reached the wire");
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-suspended-1
        [Fact]
        public async Task RTL11_QueuedPresenceFailsOnSuspended()
        {
            var capturedPresence = new List<ProtocolMessage>();
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
                options.ClientId = "my-client";
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            // The ATTACH is never answered, so the channel stays ATTACHING and the presence actions
            // queue behind it.
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            var enterTask = channel.Presence.EnterAsync("queued-enter");
            var updateTask = channel.Presence.UpdateAsync("queued-update");

            capturedPresence.Should().BeEmpty();

            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            capturedPresence.Should().BeEmpty("nothing reached the wire");

            var enterResult = await enterTask;
            var updateResult = await updateTask;

            enterResult.IsSuccess.Should().BeFalse();
            enterResult.Error.Should().NotBeNull();
            updateResult.IsSuccess.Should().BeFalse();
            updateResult.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-failed-2
        [Fact]
        public async Task RTL11_QueuedPresenceFailsOnFailed()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            var enterTask = channel.Presence.EnterAsync("queued-enter");

            capturedPresence.Should().BeEmpty();

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 50000, "Channel error", 500));
            await failed;

            capturedPresence.Should().BeEmpty();

            var result = await enterTask;
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTL11a/ack-nack-unaffected-by-state-0
        [Fact]
        public async Task RTL11a_AckNackUnaffectedByChannelStateChanges()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var (mockWs, channel) = await AttachedChannel(withPresence: false);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    // Captured and deliberately not acked yet.
                    capturedPresence.Add(msg);
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            var enterTask = channel.Presence.EnterAsync("in-flight");

            await UtsClients.PollUntil(() => capturedPresence.Count >= 1, "the ENTER went out");

            // The channel moves out from under the in-flight message.
            var detached = UtsClients.AwaitChannelState(channel, ChannelState.Detached);
            channel.Detach();
            await detached;

            // The ACK still settles it: RTL11a scopes the queue-failing to *queued* messages, not to
            // ones already on the wire.
            mockWs.SendToClient(ProtocolMessages.AckMessage(capturedPresence[0].MsgSerial));

            var result = await enterTask;

            result.IsSuccess.Should().BeTrue(
                "RTL11a - an ACK for a message already sent is honoured whatever the channel did");
        }

        private static Ably.PubSub.Realtime.Presence RealtimePresenceOf(IRealtimeChannel channel)
            => (Ably.PubSub.Realtime.Presence)channel.Presence;

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            bool withPresence,
            JArray sync = null)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    if (withPresence)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                        if (sync != null)
                        {
                            mockWs.SendToClient(
                                ProtocolMessages.SyncMessage(ChannelName, "seq1:", sync));
                        }
                    }
                    else
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                    }
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            return (mockWs, channel);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
