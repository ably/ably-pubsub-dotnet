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
    /// Derived from uts/realtime/unit/channels/channel_properties.md in ably/specification.
    ///
    /// Spec points: RTL15b, RTL15b2, RTL15c
    ///
    /// <para>
    /// The two serials on <c>channel.properties</c> move on different rules.
    /// <c>attachSerial</c> is set from an ATTACHED, but only one that is not a resumed re-attach.
    /// <c>channelSerial</c> tracks the stream: ATTACHED, MESSAGE and PRESENCE update it when they
    /// carry one, other actions never do, and it is cleared on DETACHED and FAILED but kept through
    /// SUSPENDED so a reattach can pick up where it left off.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelPropertiesTests : UtsTestBase
    {
        private const int ResumedFlag = 1 << 2;

        public ChannelPropertiesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL15c/attach-serial-from-attached-0
        [Fact]
        public async Task RTL15c_AttachSerialFromAttached()
        {
            const string ChannelName = "test-RTL15c";

            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(AttachedWithSerial(ChannelName, $"attach-serial-{attachCount}"));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            var channel0 = client.Channels.Get(ChannelName);
            channel0.Properties.AttachSerial.Should().BeNull("nothing has attached yet");

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            await channel0.AttachAsync();
            channel0.Properties.AttachSerial.Should().Be("attach-serial-1");

            await channel0.DetachAsync();
            await channel0.AttachAsync();

            channel0.Properties.AttachSerial.Should().Be("attach-serial-2");
        }

        // UTS: realtime/unit/RTL15c/attach-serial-server-reattach-1
        [Fact]
        public async Task RTL15c_AttachSerialUpdatedOnServerReattach()
        {
            const string ChannelName = "test-RTL15c-update";

            var (mockWs, channel) = await AttachedChannel(ChannelName, "initial-serial");

            channel.Properties.AttachSerial.Should().Be("initial-serial");

            // An unsolicited ATTACHED with no RESUMED flag: continuity was lost, so the serial
            // moves with it.
            mockWs.SendToClient(AttachedWithSerial(ChannelName, "updated-serial"));

            await UtsClients.PollUntil(
                () => channel.Properties.AttachSerial == "updated-serial",
                "the attachSerial moved");

            channel.Properties.AttachSerial.Should().Be("updated-serial");
        }

        // UTS: realtime/unit/RTL15c/attach-serial-not-updated-resumed-2
        //
        // DEVIATION, D36. RTL15c scopes the update to an ATTACHED "whose RTL2f `resumed` attribute
        // is `false`"; ChannelMessageProcessor.cs:59 assigns it from every ATTACHED with no such
        // check. Measured: a RESUMED re-attach moved attachSerial. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL15c_AttachSerialNotUpdatedWhenResumed()
        {
            const string ChannelName = "test-RTL15c-resumed";

            var (mockWs, channel) = await AttachedChannel(ChannelName, "initial-serial");

            channel.Properties.AttachSerial.Should().Be("initial-serial");

            var resumedAttached = AttachedWithSerial(ChannelName, "resumed-serial");
            resumedAttached["flags"] = ResumedFlag;
            mockWs.SendToClient(resumedAttached);

            // channelSerial moves regardless of RESUMED (RTL15b), which is the signal that the
            // message has been processed.
            await UtsClients.PollUntil(
                () => channel.Properties.ChannelSerial == "resumed-serial",
                "the message was processed");

            channel.Properties.AttachSerial.Should().Be(
                "initial-serial",
                "RTL15c - a resumed re-attach does not move attachSerial");
        }

        // UTS: realtime/unit/RTL15b/channel-serial-from-attached-0
        [Fact]
        public async Task RTL15b_ChannelSerialFromAttached()
        {
            const string ChannelName = "test-RTL15b-attached";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(AttachedWithSerial(ChannelName, "serial-001"));
                }
            };

            var channel = client.Channels.Get(ChannelName);
            channel.Properties.ChannelSerial.Should().BeNull();

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            await channel.AttachAsync();

            channel.Properties.ChannelSerial.Should().Be("serial-001");
        }

        // UTS: realtime/unit/RTL15b/channel-serial-from-messages-1
        [Fact]
        public async Task RTL15b_ChannelSerialFromMessageAndPresence()
        {
            const string ChannelName = "test-RTL15b-messages";

            var (mockWs, channel) = await AttachedChannel(ChannelName, "serial-001");

            channel.Properties.ChannelSerial.Should().Be("serial-001");

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { new JObject { ["name"] = "event", ["data"] = "payload" } },
                new Dictionary<string, JToken> { ["channelSerial"] = "serial-002" }));

            await UtsClients.PollUntil(
                () => channel.Properties.ChannelSerial == "serial-002",
                "a MESSAGE moved the serial");

            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                ChannelName,
                new JArray { ProtocolMessages.PresenceEntry(2, "alice", "c1", "c1:0:0", 100) },
                new Dictionary<string, JToken> { ["channelSerial"] = "serial-003" }));

            await UtsClients.PollUntil(
                () => channel.Properties.ChannelSerial == "serial-003",
                "a PRESENCE moved the serial");

            channel.Properties.ChannelSerial.Should().Be("serial-003");
        }

        // UTS: realtime/unit/RTL15b/serial-not-updated-empty-2
        [Fact]
        public async Task RTL15b_ChannelSerialNotUpdatedWhenFieldAbsent()
        {
            const string ChannelName = "test-RTL15b-noupdate";

            var (mockWs, channel) = await AttachedChannel(ChannelName, "serial-001");

            var received = new List<Message>();
            channel.Subscribe(message => received.Add(message));

            mockWs.SendToClient(ProtocolMessages.MessageProtocolMessage(
                ChannelName,
                new JArray { new JObject { ["name"] = "event", ["data"] = "payload" } }));

            await UtsClients.PollUntil(() => received.Count >= 1, "the message arrived");

            channel.Properties.ChannelSerial.Should().Be(
                "serial-001",
                "RTL15b - a message with no channelSerial leaves it alone");
        }

        // UTS: realtime/unit/RTL15b/serial-not-updated-irrelevant-3
        [Fact]
        public async Task RTL15b_ChannelSerialNotUpdatedFromIrrelevantActions()
        {
            const string ChannelName = "test-RTL15b-irrelevant";

            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(AttachedWithSerial(ChannelName, "serial-001"));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // A server DETACHED carrying a channelSerial of its own. RTL13a reattaches; the
            // DETACHED's serial must never be adopted.
            var detached = ProtocolMessages.ServerDetachedMessage(ChannelName, 90198, "Detach", 500);
            detached["channelSerial"] = "detached-serial";
            mockWs.SendToClient(detached);

            await UtsClients.PollUntil(
                () => attachCount >= 2,
                "RTL13a reattached",
                TimeSpan.FromSeconds(10));

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(10));

            attachCount.Should().Be(2);
            channel.Properties.ChannelSerial.Should().Be(
                "serial-001",
                "from the new ATTACHED, never from the DETACHED");
        }

        // UTS: realtime/unit/RTL15b2/serial-cleared-detached-0
        [Fact]
        public async Task RTL15b2_ChannelSerialClearedOnDetached()
        {
            const string ChannelName = "test-RTL15b2-detached";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(AttachedWithSerial(ChannelName, "serial-001"));
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
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            await channel.DetachAsync();

            channel.State.Should().Be(ChannelState.Detached);
            channel.Properties.ChannelSerial.Should().BeNull("RTL15b2");
        }

        // UTS: realtime/unit/RTL15b2/serial-retained-suspended-1
        //
        // DEVIATION, D37, and not where it looks. The RTL15b2 clear itself is implemented exactly
        // right - RealtimeChannel.cs:692 clears only on DETACHED and FAILED, with the clause
        // quoted in a comment. What defeats it is the route: a server-initiated DETACHED takes the
        // channel *through* DETACHED before reattaching, where RTL13a says it should transition
        // straight to ATTACHING. Measured state sequence from ATTACHED:
        // Attaching, Detached, Suspended - and the serial is gone by the time SUSPENDED is
        // reached. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL15b2_ChannelSerialRetainedInSuspended()
        {
            const string ChannelName = "test-RTL15b2-suspended";

            var attachCount = 0;
            var mockWs = ConnectingMock();

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200);
                options.ChannelRetryTimeout = TimeSpan.FromMinutes(10);
            });

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    if (attachCount == 1)
                    {
                        mockWs.SendToClient(AttachedWithSerial(ChannelName, "serial-001"));
                    }

                    // The reattach goes unanswered, so it times out into SUSPENDED.
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            var states = UtsClients.RecordChannelStates(channel);

            var suspended = UtsClients.AwaitChannelState(
                channel,
                ChannelState.Suspended,
                TimeSpan.FromSeconds(10));

            mockWs.SendToClient(
                ProtocolMessages.ServerDetachedMessage(ChannelName, 90198, "Detach", 500));

            await suspended;

            channel.State.Should().Be(ChannelState.Suspended);
            channel.Properties.ChannelSerial.Should().Be(
                "serial-001",
                "RTL15b2 - SUSPENDED keeps the serial so a reattach can resume from it");
        }

        // UTS: realtime/unit/RTL15b2/serial-cleared-failed-2
        [Fact]
        public async Task RTL15b2_ChannelSerialClearedOnFailed()
        {
            const string ChannelName = "test-RTL15b2-failed";

            var (mockWs, channel) = await AttachedChannel(ChannelName, "serial-001");

            channel.Properties.ChannelSerial.Should().Be("serial-001");

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 40160, "Not permitted", 401));
            await failed;

            channel.State.Should().Be(ChannelState.Failed);
            channel.Properties.ChannelSerial.Should().BeNull("RTL15b2");
        }

        private static JObject AttachedWithSerial(string channelName, string serial)
            => ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["channelSerial"] = serial });

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            string channelName,
            string serial)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(AttachedWithSerial(channelName, serial));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();

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
