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
    /// Derived from uts/realtime/unit/presence/realtime_presence_reentry.md in ably/specification.
    ///
    /// Spec points: RTP17a, RTP17e, RTP17g, RTP17g1, RTP17i
    ///
    /// <para>
    /// After a connection is lost and remade, the members this client put into presence are its own
    /// to restore - the server has forgotten them. That is what the internal presence map
    /// (<see cref="LocalPresenceMapTests"/>) is for, and this file is about what the client does
    /// with it on a fresh ATTACHED.
    /// </para>
    ///
    /// <para>
    /// A RESUMED ATTACHED means the server still has the state, so there is nothing to restore;
    /// that case is the one test here that asserts an absence.
    /// </para>
    ///
    /// <para>
    /// Four of the six tests here are gated on one defect, D32: the automatic re-entry is built but
    /// never reaches the wire, because <c>Presence.ChannelAttached</c> flushes the pending queue
    /// before it enqueues the re-entries, and the channel is still ATTACHING at that point so they
    /// are queued rather than sent. See Uts/deviations.md.
    /// </para>
    ///
    /// <para>
    /// Every mock in this file echoes each presence message back to the client after acking it,
    /// because that is what fills the local map. The spec's own setup says so - "the LocalPresenceMap
    /// (RTP17) ... is keyed by server echoes, not by the client's own enter() calls" - and this SDK
    /// works the same way: <c>Presence.OnPresence</c> is the only writer of
    /// <c>InternalMembersMap</c>. An <c>enter()</c> that is merely acked leaves the map empty and
    /// nothing is re-entered, which is a false pass waiting to happen rather than a finding.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceReentryTests : UtsTestBase
    {
        private const string ChannelName = "test-RTP17";

        public RealtimePresenceReentryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP17i/auto-reentry-on-attached-0
        //
        // DEVIATION, D32 - the automatic re-entry never reaches the wire. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP17i_AutoReentryOnAttached()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var (mockWs, channel, _) = await AttachedChannelWithPresenceCapture(capturedPresence);

            await channel.Presence.EnterAsync("hello");

            capturedPresence.Should().HaveCount(1);
            capturedPresence.Clear();

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(
                () => capturedPresence.Any(m => m.Presence[0].Action == PresenceAction.Enter),
                "RTP17i - the member is re-entered on a non-RESUMED attach");

            capturedPresence.Should().Contain(m => m.Presence[0].Action == PresenceAction.Enter);
        }

        // UTS: realtime/unit/RTP17g/reentry-publishes-enter-with-data-0
        //
        // DEVIATION, D32 - the automatic re-entry never reaches the wire. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP17g_ReentryPublishesEnterWithStoredData()
        {
            var capturedPresence = new List<ProtocolMessage>();

            // Unidentified, not "admin" as the spec writes it. enterClient on behalf of someone
            // else is checked locally here - AblyAuth.ValidateClientIds rejects a message whose
            // clientId differs from the library's - so a client identified as "admin" cannot enter
            // alice at all, and the spec's own note anticipates this ("some SDKs reject wildcard
            // clientId"). An unidentified client has no clientId to conflict with, which is the
            // closest this SDK comes to the wildcard identity the test is really describing.
            var (mockWs, channel, _) = await AttachedChannelWithPresenceCapture(
                capturedPresence,
                clientId: null);

            await channel.Presence.EnterClientAsync("alice", "alice-data");
            await channel.Presence.EnterClientAsync("bob", "bob-data");

            capturedPresence.Should().HaveCount(2);
            capturedPresence.Clear();

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(
                () => PresenceItems(capturedPresence).Count >= 2,
                "both members re-entered");

            var items = PresenceItems(capturedPresence);

            var alice = items.FirstOrDefault(p => p.ClientId == "alice");
            alice.Should().NotBeNull();
            alice.Action.Should().Be(PresenceAction.Enter);
            alice.Data.Should().Be("alice-data");

            var bob = items.FirstOrDefault(p => p.ClientId == "bob");
            bob.Should().NotBeNull();
            bob.Action.Should().Be(PresenceAction.Enter);
            bob.Data.Should().Be("bob-data");
        }

        // UTS: realtime/unit/RTP17g1/reentry-omits-id-new-connid-0
        //
        // DEVIATION, D32 - the automatic re-entry never reaches the wire. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP17g1_ReentryOmitsIdWhenConnectionIdChanged()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var connectionCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionCount = connectionCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    $"conn-{connectionCount}",
                    $"key-{connectionCount}",
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            WireUpAttachAndEcho(mockWs, capturedPresence, () => $"conn-{connectionCount}");

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            await channel.Presence.EnterAsync("hello");

            connectionCount.Should().Be(1);
            capturedPresence.Clear();

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(
                () => PresenceItems(capturedPresence).Any(),
                "the re-entry message");

            connectionCount.Should().Be(2, "a new connection, so a new connectionId");

            var reentry = PresenceItems(capturedPresence)[0];
            reentry.Action.Should().Be(PresenceAction.Enter);
            reentry.Id.Should().BeNull("RTP17g1 - the old id belongs to a connection that is gone");
            reentry.Data.Should().Be("hello");
        }

        // UTS: realtime/unit/RTP17i/no-reentry-with-resumed-flag-1
        //
        // Passing, but read it with D32 in mind: while the re-entry path is broken nothing is ever
        // re-entered, so this test cannot currently fail. It is kept as written - it is correct,
        // and it becomes a real assertion the moment D32 is fixed, which is exactly when a
        // regression here would matter. The SDK does have the RESUMED check
        // (ChannelMessageProcessor.cs:72-79 only calls ChannelAttached for an already-attached
        // channel when the RESUMED flag is absent), so there is reason to expect it to hold.
        [Fact]
        public async Task RTP17i_NoReentryWithResumedFlag()
        {
            var capturedPresence = new List<ProtocolMessage>();
            var (mockWs, channel, _) = await AttachedChannelWithPresenceCapture(capturedPresence);

            await channel.Presence.EnterAsync("hello");
            capturedPresence.Clear();

            // An ATTACHED carrying RESUMED: the server still holds this connection's presence.
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                ChannelName,
                new Dictionary<string, JToken> { ["flags"] = ResumedFlag }));

            // Nothing to wait for, so give the workflow a round trip to have acted on it.
            await channel.Presence.GetAsync(waitForSync: false);

            capturedPresence.Should().BeEmpty(
                "RTP17i - a RESUMED attach means there is nothing to re-enter");
        }

        // UTS: realtime/unit/RTP17e/failed-reentry-emits-update-error-0
        //
        // DEVIATION, D32 - the automatic re-entry never reaches the wire. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP17e_FailedReentryEmitsUpdateWithError()
        {
            var connectionCount = 0;
            var capturedPresence = new List<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionCount = connectionCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    $"conn-{connectionCount}",
                    $"key-{connectionCount}",
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);

                    if (connectionCount == 1)
                    {
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));

                        // Echoed, so the member reaches the local map and there is something to
                        // re-enter later. See the class note.
                        mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                            ChannelName,
                            new JArray
                            {
                                ProtocolMessages.PresenceEntry(
                                    (int)msg.Presence[0].Action,
                                    "my-client",
                                    "conn-1",
                                    $"conn-1:{msg.MsgSerial}:0",
                                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    msg.Presence[0].Data),
                            }));
                    }
                    else
                    {
                        // The re-entry is refused.
                        mockWs.SendToClient(ProtocolMessages.NackMessage(
                            msg.MsgSerial,
                            40160,
                            "Channel denied access",
                            401));
                    }
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            await channel.Presence.EnterAsync("hello");

            // The ATTACHED itself can emit an UPDATE, so the listener filters on the code RTP17e
            // names rather than taking the first event it sees.
            var reentryFailures = new List<ChannelStateChange>();
            channel.On(change =>
            {
                if (change.Error != null && change.Error.Code == 91004)
                {
                    reentryFailures.Add(change);
                }
            });

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached);
            mockWs.ActiveConnection.SimulateDisconnect();
            await reattached;

            await UtsClients.PollUntil(
                () => reentryFailures.Count >= 1,
                "the 91004 update for the refused re-entry",
                TimeSpan.FromSeconds(10));

            var update = reentryFailures[0];
            update.Resumed.Should().BeTrue();
            update.Error.Should().NotBeNull();
            update.Error.Code.Should().Be(91004);
            update.Error.Message.Should().Contain("my-client");
        }

        // UTS: realtime/unit/RTP17a/server-publishes-without-subscribe-0
        [Fact]
        public async Task RTP17a_ServerPublishesMemberWithoutSubscribeCapability()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("conn-1", maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));

                    // The server echoes the member back whatever the subscribe capability is.
                    mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(ChannelName, new JArray
                    {
                        ProtocolMessages.PresenceEntry(2, "my-client", "conn-1", "conn-1:0:0", 1000),
                    }));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            await channel.Presence.EnterAsync("hello");

            await UtsClients.PollUntil(
                () => RealtimePresenceOf(channel).MembersMap.Values.Any(),
                "the echoed member reached the public map");

            var presence = RealtimePresenceOf(channel);

            presence.MembersMap.Values.Should().Contain(m => m.ClientId == "my-client");
            presence.InternalMembersMap.Values.Should().Contain(
                m => m.ClientId == "my-client",
                "RTP17a - the member is in the internal set too");

            var members = (await channel.Presence.GetAsync(waitForSync: false)).ToList();
            members.Should().Contain(m => m.ClientId == "my-client");
        }

        private const int ResumedFlag = 1 << 2;

        private static Ably.PubSub.Realtime.Presence RealtimePresenceOf(IRealtimeChannel channel)
            => (Ably.PubSub.Realtime.Presence)channel.Presence;

        private static List<PresenceMessage> PresenceItems(List<ProtocolMessage> captured)
            => captured
                .Where(m => m.Action == ProtocolMessage.MessageAction.Presence && m.Presence != null)
                .SelectMany(m => m.Presence)
                .ToList();

        /// <summary>
        /// Answers ATTACH with ATTACHED, and every presence message with an ACK followed by the
        /// server's echo of each entry - see the class note for why the echo matters.
        /// </summary>
        private static void WireUpAttachAndEcho(
            MockWebSocket mockWs,
            List<ProtocolMessage> capturedPresence,
            Func<string> currentConnectionId,
            string defaultClientId = "my-client")
            => mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    capturedPresence.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));

                    var connectionId = currentConnectionId();
                    for (var index = 0; index < msg.Presence.Length; index++)
                    {
                        var entry = msg.Presence[index];
                        mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                            ChannelName,
                            new JArray
                            {
                                ProtocolMessages.PresenceEntry(
                                    (int)entry.Action,
                                    entry.ClientId ?? defaultClientId,
                                    connectionId,
                                    $"{connectionId}:{msg.MsgSerial}:{index}",
                                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    entry.Data),
                            }));
                    }
                }
            };

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel, PubSubRealtimeClient Client)>
            AttachedChannelWithPresenceCapture(
                List<ProtocolMessage> capturedPresence,
                string clientId = "my-client")
        {
            var connectionCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionCount = connectionCount + 1;
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    $"conn-{connectionCount}",
                    $"key-{connectionCount}",
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = clientId);

            WireUpAttachAndEcho(
                mockWs,
                capturedPresence,
                () => $"conn-{connectionCount}",
                clientId ?? "my-client");

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            return (mockWs, channel, client);
        }
    }
}
