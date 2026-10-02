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
    /// Derived from uts/realtime/unit/presence/realtime_presence_enter.md in ably/specification.
    ///
    /// Spec points: RTP4, RTP8a, RTP8c, RTP8d, RTP8e, RTP8g, RTP8h, RTP8j, RTP9a, RTP9d, RTP10a,
    /// RTP10c, RTP14a, RTP15a, RTP15c, RTP15e, RTP15f, RTP16a, RTP16b, RTP16c
    ///
    /// <para>
    /// The spec reaches for <c>clientId: "*"</c> in six of these tests, to let one client enter on
    /// behalf of others. <c>ClientOptions.ClientId</c> rejects the wildcard outright here
    /// (<c>ClientOptions.cs:44</c>), and the spec anticipates exactly that: "clientId '*' may not be
    /// accepted by all SDKs at construction time. See top-level note for alternative auth patterns
    /// (e.g., key auth without clientId)." Those tests therefore use an unidentified client, which
    /// is the alternative the spec names and which <c>AblyAuth.ValidateClientIds</c> treats the same
    /// way - no library clientId, so nothing to conflict with.
    /// </para>
    ///
    /// <para>
    /// RTP8c/RTP9d/RTP10c are the interesting assertion in the first half: an <c>enter()</c> by an
    /// identified client must *not* put the clientId on the wire, because the server already knows
    /// it from the connection. This SDK does put it there - D33 - so those three are gated.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceEnterTests : UtsTestBase
    {
        private const string ChannelName = "test-presence-enter";

        public RealtimePresenceEnterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP8a/enter-sends-presence-enter-0
        //
        // DEVIATION, D33 - this SDK sends the clientId on the wire where the spec says it must not
        // be present. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP8a_EnterSendsPresenceEnter()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.EnterAsync();

            captured.Should().HaveCount(1);
            captured[0].Action.Should().Be(ProtocolMessage.MessageAction.Presence);
            captured[0].Channel.Should().Be(ChannelName);
            captured[0].Presence.Should().HaveCount(1);
            captured[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[0].Presence[0].ClientId.Should().BeNull(
                "RTP8c - the connection already carries the clientId");
        }

        // UTS: realtime/unit/RTP8e/enter-with-data-0
        [Fact]
        public async Task RTP8e_EnterWithData()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.EnterAsync("hello world");

            captured.Should().HaveCount(1);
            captured[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[0].Presence[0].Data.Should().Be("hello world");
        }

        // UTS: realtime/unit/RTP8d/enter-implicitly-attaches-0
        [Fact]
        public async Task RTP8d_EnterImplicitlyAttachesChannel()
        {
            var captured = new List<ProtocolMessage>();
            var (_, client, mockWs) = ConnectedClient(captured);

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            await channel.Presence.EnterAsync();

            channel.State.Should().Be(ChannelState.Attached, "RTP8d");
        }

        // UTS: realtime/unit/RTP8g/enter-detached-failed-errors-0
        [Fact]
        public async Task RTP8g_EnterOnFailedChannelErrors()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        50000,
                        "Channel error",
                        500));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            channel.Attach();
            await failed;

            channel.State.Should().Be(ChannelState.Failed);

            var result = await channel.Presence.EnterAsync();

            result.IsSuccess.Should().BeFalse("RTP8g - a FAILED channel cannot take presence");
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP8j/enter-null-clientid-errors-0
        //
        // DEVIATION. RTP8j says that with a null clientId - "the client is anonymous and is not
        // permitted to associate a client identifier with the operations it performs" - the enter
        // request "results in an error immediately". This SDK sends it: Presence.EnterAsync passes
        // its own `_clientId`, which is empty, straight through to EnterClientAsync with no check
        // (Presence.cs:307). The practical outcome is a round trip and the service's NACK under
        // RTP8i rather than a local failure, so this is a wrong-place-to-fail rather than a wrong
        // outcome. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP8j_EnterWithNoClientIdErrors()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured, clientId: null);

            var result = await channel.Presence.EnterAsync();

            result.IsSuccess.Should().BeFalse(
                "RTP8j - an unidentified client has no identity to enter with");
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP8j/enter-wildcard-clientid-errors-1
        //
        // The spec builds this one with clientId "*". This SDK refuses the wildcard at construction
        // - ClientOptions.ClientId throws for it (ClientOptions.cs:42-48) - so the refusal happens
        // earlier and more bluntly than the spec describes, and that is what is asserted. The
        // outcome RTP8j is protecting against, a client entering presence with no usable identity,
        // cannot arise.
        [Fact]
        public void RTP8j_WildcardClientIdIsRejectedAtConstruction()
        {
            Action act = () => new ClientOptions(UtsClients.ValidKey) { ClientId = "*" };

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Wildcard clientIds are not support*");
        }

        // UTS: realtime/unit/RTP8h/nack-presence-permission-denied-0
        [Fact]
        public async Task RTP8h_NackForMissingPresencePermission()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    mockWs.SendToClient(ProtocolMessages.NackMessage(
                        msg.MsgSerial,
                        40160,
                        "Permission denied",
                        401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            var result = await channel.Presence.EnterAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error.Code.Should().Be(40160);
        }

        // UTS: realtime/unit/RTP9a/update-sends-presence-update-0
        //
        // DEVIATION, D33 - this SDK sends the clientId on the wire where the spec says it must not
        // be present. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP9a_UpdateSendsPresenceUpdate()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.UpdateAsync("new-status");

            captured.Should().HaveCount(1);
            captured[0].Presence[0].Action.Should().Be(PresenceAction.Update);
            captured[0].Presence[0].Data.Should().Be("new-status");
            captured[0].Presence[0].ClientId.Should().BeNull("RTP9d");
        }

        // UTS: realtime/unit/RTP10a/leave-sends-presence-leave-0
        //
        // DEVIATION, D33 - this SDK sends the clientId on the wire where the spec says it must not
        // be present. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTP10a_LeaveSendsPresenceLeave()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.LeaveAsync();

            captured.Should().HaveCount(1);
            captured[0].Presence[0].Action.Should().Be(PresenceAction.Leave);
            captured[0].Presence[0].ClientId.Should().BeNull("RTP10c");
        }

        // UTS: realtime/unit/RTP10a/leave-with-data-1
        [Fact]
        public async Task RTP10a_LeaveWithData()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.LeaveAsync("goodbye");

            captured[0].Presence[0].Action.Should().Be(PresenceAction.Leave);
            captured[0].Presence[0].Data.Should().Be("goodbye");
        }

        // UTS: realtime/unit/RTP14a/enterclient-on-behalf-0
        [Fact]
        public async Task RTP14a_EnterClientOnBehalfOfAnother()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured, clientId: null);

            await channel.Presence.EnterClientAsync("user-alice", "alice-data");
            await channel.Presence.EnterClientAsync("user-bob", "bob-data");

            captured.Should().HaveCount(2);

            captured[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[0].Presence[0].ClientId.Should().Be("user-alice");
            captured[0].Presence[0].Data.Should().Be("alice-data");

            captured[1].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[1].Presence[0].ClientId.Should().Be("user-bob");
            captured[1].Presence[0].Data.Should().Be("bob-data");
        }

        // UTS: realtime/unit/RTP15a/updateclient-leaveclient-0
        [Fact]
        public async Task RTP15a_UpdateClientAndLeaveClient()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured, clientId: null);

            await channel.Presence.EnterClientAsync("user-1", "entered");
            await channel.Presence.UpdateClientAsync("user-1", "updated");
            await channel.Presence.LeaveClientAsync("user-1", "leaving");

            captured.Should().HaveCount(3);

            captured[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[0].Presence[0].ClientId.Should().Be("user-1");
            captured[0].Presence[0].Data.Should().Be("entered");

            captured[1].Presence[0].Action.Should().Be(PresenceAction.Update);
            captured[1].Presence[0].ClientId.Should().Be("user-1");
            captured[1].Presence[0].Data.Should().Be("updated");

            captured[2].Presence[0].Action.Should().Be(PresenceAction.Leave);
            captured[2].Presence[0].ClientId.Should().Be("user-1");
            captured[2].Presence[0].Data.Should().Be("leaving");
        }

        // UTS: realtime/unit/RTP15e/enterclient-implicitly-attaches-0
        [Fact]
        public async Task RTP15e_EnterClientImplicitlyAttaches()
        {
            var captured = new List<ProtocolMessage>();
            var (_, client, _) = ConnectedClient(captured, clientId: null);

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            await channel.Presence.EnterClientAsync("user-1", null);

            channel.State.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTP15f/enterclient-mismatched-clientid-0
        [Fact]
        public async Task RTP15f_EnterClientWithMismatchedClientIdErrors()
        {
            var captured = new List<ProtocolMessage>();
            var (client, channel) = await AttachedChannel(captured, clientId: "my-client");

            var result = await channel.Presence.EnterClientAsync("other-client", null);

            result.IsSuccess.Should().BeFalse("RTP15f");
            result.Error.Should().NotBeNull();

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "the failure is local and does not disturb the connection");
            channel.State.Should().Be(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTP16a/presence-sent-when-attached-0
        [Fact]
        public async Task RTP16a_PresenceSentWhenAttached()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            await channel.Presence.EnterAsync();

            captured.Should().HaveCount(1, "RTP16a - sent straight away on an attached channel");
        }

        // UTS: realtime/unit/RTP16b/presence-queued-when-attaching-0
        [Fact]
        public async Task RTP16b_PresenceQueuedWhenAttaching()
        {
            var captured = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = "my-client");

            var attachSeen = false;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    // Held, so the channel stays ATTACHING.
                    attachSeen = true;
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    captured.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);
            await UtsClients.PollUntil(() => attachSeen, "the ATTACH went out");

            var enterTask = channel.Presence.EnterAsync();

            captured.Should().BeEmpty("RTP16b - queued, not sent");

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            var result = await enterTask;

            result.IsSuccess.Should().BeTrue();
            captured.Should().HaveCount(1, "the queue is flushed once attached");
        }

        // UTS: realtime/unit/RTP16c/presence-errors-other-states-0
        [Fact]
        public async Task RTP16c_PresenceErrorsInOtherChannelStates()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured);

            var mockWs = _lastMock;
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

            var result = await channel.Presence.EnterAsync();

            result.IsSuccess.Should().BeFalse("RTP16c");
            result.Error.Should().NotBeNull();
        }

        // UTS: realtime/unit/RTP15c/enterclient-no-side-effects-0
        [Fact]
        public async Task RTP15c_EnterClientHasNoSideEffectsOnNormalEnter()
        {
            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannel(captured, clientId: null);

            await channel.Presence.EnterAsync("main-client");
            await channel.Presence.EnterClientAsync("other-user", "other-data");
            await channel.Presence.LeaveClientAsync("other-user", null);

            captured.Should().HaveCount(3);

            captured[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[0].Presence[0].Data.Should().Be("main-client");
            captured[0].Presence[0].ClientId.Should().BeNull("the connection's own identity is used");

            captured[1].Presence[0].Action.Should().Be(PresenceAction.Enter);
            captured[1].Presence[0].ClientId.Should().Be("other-user");

            captured[2].Presence[0].Action.Should().Be(PresenceAction.Leave);
            captured[2].Presence[0].ClientId.Should().Be("other-user");
        }

        // UTS: realtime/unit/RTP4/bulk-enterclient-same-connection-0
        [Fact]
        public async Task RTP4_BulkEnterClientSameConnection()
        {
            const int MemberCount = 50;

            var captured = new List<ProtocolMessage>();
            var (_, channel) = await AttachedChannelEchoing(captured, "conn-1", clientId: null);

            var receivedEnters = new List<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Enter, msg => receivedEnters.Add(msg));

            for (var i = 0; i < MemberCount; i++)
            {
                await channel.Presence.EnterClientAsync($"user-{i}", $"data-{i}");
            }

            captured.Should().HaveCount(MemberCount);

            await UtsClients.PollUntil(
                () => receivedEnters.Count >= MemberCount,
                "every ENTER echoed back to the subscriber",
                TimeSpan.FromSeconds(10));

            receivedEnters.Should().HaveCount(MemberCount);

            var members = (await channel.Presence.GetAsync(waitForSync: false)).ToList();
            members.Should().HaveCount(MemberCount);

            for (var i = 0; i < MemberCount; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == $"user-{i}");
                member.Should().NotBeNull();
                member.Data.Should().Be($"data-{i}");
            }
        }

        // UTS: realtime/unit/RTP4/bulk-enterclient-diff-connections-1
        [Fact]
        public async Task RTP4_BulkMembersFromDifferentConnections()
        {
            const int MemberCount = 50;

            var captured = new List<ProtocolMessage>();

            // Attached *with* HAS_PRESENCE, so the sync below is the one the channel is waiting
            // for. Without it the channel completes an empty sync at attach time and get() can
            // return before the 50 members have been applied.
            var (mockWs, channel) = await AttachedChannelAwaitingSync(captured);

            // Fifty members, each on its own connection, delivered as a complete sync.
            var syncEntries = new JArray();
            for (var i = 0; i < MemberCount; i++)
            {
                syncEntries.Add(ProtocolMessages.PresenceEntry(
                    1,
                    $"user-{i}",
                    $"conn-{i}",
                    $"conn-{i}:0:0",
                    100,
                    $"data-{i}"));
            }

            mockWs.SendToClient(ProtocolMessages.SyncMessage(ChannelName, "seq1:", syncEntries));

            // The sync carries an empty cursor, so get() returns once it has been applied.
            var members = (await channel.Presence.GetAsync()).ToList();

            members.Should().HaveCount(MemberCount);

            for (var i = 0; i < MemberCount; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == $"user-{i}");
                member.Should().NotBeNull();
                member.ConnectionId.Should().Be($"conn-{i}");
                member.Data.Should().Be($"data-{i}");
            }
        }

        private MockWebSocket _lastMock;

        private (List<ProtocolMessage> Captured, PubSubRealtimeClient Client, MockWebSocket MockWs)
            ConnectedClient(List<ProtocolMessage> captured, string clientId = "my-client")
        {
            var mockWs = ConnectingMock();
            _lastMock = mockWs;

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = clientId);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    captured.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            return (captured, client, mockWs);
        }

        private async Task<(PubSubRealtimeClient Client, IRealtimeChannel Channel)> AttachedChannel(
            List<ProtocolMessage> captured,
            string clientId = "my-client")
        {
            var (_, client, mockWs) = ConnectedClient(captured, clientId);

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            _lastMock = mockWs;
            return (client, channel);
        }

        /// <summary>
        /// As <see cref="AttachedChannel"/>, but the server also echoes each presence entry back -
        /// which is how members reach the presence map. See RealtimePresenceReentryTests' note.
        /// </summary>
        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannelEchoing(
            List<ProtocolMessage> captured,
            string connectionId,
            string clientId)
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(connectionId, maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, configure: options => options.ClientId = clientId);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    captured.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));

                    for (var index = 0; index < msg.Presence.Length; index++)
                    {
                        var entry = msg.Presence[index];
                        mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                            ChannelName,
                            new JArray
                            {
                                ProtocolMessages.PresenceEntry(
                                    (int)entry.Action,
                                    entry.ClientId,
                                    connectionId,
                                    $"{connectionId}:{msg.MsgSerial}:{index}",
                                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    entry.Data),
                            }));
                    }
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            _lastMock = mockWs;
            return (mockWs, channel);
        }

        /// <summary>
        /// A channel attached with HAS_PRESENCE and no sync delivered yet, so a later SYNC is the
        /// one <c>get()</c> waits for.
        /// </summary>
        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)>
            AttachedChannelAwaitingSync(List<ProtocolMessage> captured)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.ClientId = null);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                {
                    captured.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            _lastMock = mockWs;
            return (mockWs, channel);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("conn-1", maxIdleInterval: 0));
            });
    }
}
