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
    /// Derived from uts/realtime/unit/channels/channel_state_events.md in ably/specification.
    ///
    /// Spec points: RTL2, RTL2a, RTL2b, RTL2d, RTL2g, RTL4c, RTL12, RTL24, TH1, TH2, TH3, TH5
    ///
    /// <para>
    /// <c>realtime/unit/RTL2i/has-backlog-flag-true-0</c> and
    /// <c>RTL2i/has-backlog-flag-false-1</c> are not translated: <c>ChannelStateChange</c> has no
    /// <c>hasBacklog</c> member. The HAS_BACKLOG flag exists on <c>ProtocolMessage.Flag</c> but is
    /// never surfaced on the state change. See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// This file's last two tests cover the same ground as <see cref="ChannelAttributesTests"/>'s
    /// RTL24 and RTL4c under their own spec test ids; they are translated rather than
    /// cross-referenced so every id has a derived test.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelStateEventsTests : UtsTestBase
    {
        private const int ResumedFlag = 1 << 2;

        public ChannelStateEventsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL2b/channel-state-attribute-0
        [Fact]
        public void RTL2b_ChannelStateAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());

            var channel = client.Channels.Get("test-RTL2b");

            // The spec's "state IS ChannelState" is a type check in a dynamically typed language;
            // here it is the declared type of the property, so the value is what there is to assert.
            channel.State.Should().Be(ChannelState.Initialized);
        }

        // UTS: realtime/unit/RTL2b/initial-state-initialized-1
        [Fact]
        public void RTL2b_InitialStateIsInitialized()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Get("test-RTL2b-init").State.Should().Be(ChannelState.Initialized);
        }

        // UTS: realtime/unit/RTL2a/state-change-events-emitted-0
        [Fact]
        public async Task RTL2a_StateChangeEventsEmitted()
        {
            const string ChannelName = "test-RTL2a";

            var (_, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var stateChanges = new List<ChannelStateChange>();
            channel.On(change => stateChanges.Add(change));

            await channel.AttachAsync();

            stateChanges.Should().HaveCountGreaterOrEqualTo(2);
            stateChanges[0].Current.Should().Be(ChannelState.Attaching);
            stateChanges[0].Previous.Should().Be(ChannelState.Initialized);
            stateChanges[1].Current.Should().Be(ChannelState.Attached);
            stateChanges[1].Previous.Should().Be(ChannelState.Attaching);
        }

        // UTS: realtime/unit/RTL2d/state-change-object-structure-0
        [Fact]
        public async Task RTL2d_StateChangeObjectStructure()
        {
            const string ChannelName = "test-RTL2d";

            var (_, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            ChannelStateChange capturedChange = null;
            channel.On(change =>
            {
                if (capturedChange == null)
                {
                    capturedChange = change;
                }
            });

            await channel.AttachAsync();

            capturedChange.Should().NotBeNull();
            capturedChange.Should().BeOfType<ChannelStateChange>();
            capturedChange.Current.Should().Be(ChannelState.Attaching);
            capturedChange.Previous.Should().Be(ChannelState.Initialized);
            capturedChange.Event.Should().Be(ChannelEvent.Attaching);
        }

        // UTS: realtime/unit/RTL2d/state-change-error-reason-1
        [Fact]
        public async Task RTL2d_StateChangeIncludesErrorReason()
        {
            const string ChannelName = "test-RTL2d-error";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        40160,
                        "Channel denied",
                        401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            ChannelStateChange capturedChange = null;
            channel.On(change =>
            {
                if (change.Current == ChannelState.Failed)
                {
                    capturedChange = change;
                }
            });

            var result = await channel.AttachAsync();
            result.IsSuccess.Should().BeFalse();

            capturedChange.Should().NotBeNull();
            capturedChange.Current.Should().Be(ChannelState.Failed);
            capturedChange.Error.Should().NotBeNull();
            capturedChange.Error.Code.Should().Be(40160);
            capturedChange.Error.Message.Should().Be("Channel denied");
        }

        // UTS: realtime/unit/RTL2/filtered-event-subscription-0
        [Fact]
        public async Task RTL2_FilteredEventSubscription()
        {
            const string ChannelName = "test-RTL2-filtered";

            var (_, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var attachedEvents = new List<ChannelStateChange>();
            channel.On(ChannelEvent.Attached, change => attachedEvents.Add(change));

            await channel.AttachAsync();

            // AttachAsync completes on the attach awaiter; the filtered emit can land a beat
            // later, so wait for it rather than assuming it is already there.
            await UtsClients.PollUntil(() => attachedEvents.Count >= 1, "the ATTACHED event");

            attachedEvents.Should().HaveCount(1, "ATTACHING was filtered out");
            attachedEvents[0].Current.Should().Be(ChannelState.Attached);
            attachedEvents[0].Event.Should().Be(ChannelEvent.Attached);
        }

        // UTS: realtime/unit/RTL2g/update-event-condition-change-0
        [Fact]
        public async Task RTL2g_UpdateEventForConditionChange()
        {
            const string ChannelName = "test-RTL2g";

            var (mockWs, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var updateEvents = new List<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await UtsClients.PollUntil(() => updateEvents.Count >= 1, "the UPDATE");

            channel.State.Should().Be(ChannelState.Attached, "the state did not change");
            updateEvents.Should().HaveCount(1);
            updateEvents[0].Event.Should().Be(ChannelEvent.Update);
            updateEvents[0].Current.Should().Be(ChannelState.Attached);
            updateEvents[0].Previous.Should().Be(ChannelState.Attached);
            updateEvents[0].Resumed.Should().BeFalse();
        }

        // UTS: realtime/unit/RTL2g/no-duplicate-state-events-1
        [Fact]
        public async Task RTL2g_NoDuplicateStateEvents()
        {
            const string ChannelName = "test-RTL2g-nodup";

            var (mockWs, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var allEvents = new List<ChannelStateChange>();
            channel.On(change => allEvents.Add(change));

            await channel.AttachAsync();

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await UtsClients.PollUntil(
                () => allEvents.Any(e => e.Event == ChannelEvent.Update),
                "the second ATTACHED produced an UPDATE");

            var attachedStateEvents = allEvents
                .Where(e => e.Current == ChannelState.Attached && e.Event == ChannelEvent.Attached)
                .ToList();

            attachedStateEvents.Should().HaveCount(
                1,
                "RTL2g - the second ATTACHED is an UPDATE, not another ATTACHED state event");
        }

        // UTS: realtime/unit/RTL2d/resumed-flag-propagated-2
        [Fact]
        public async Task RTL2d_ResumedFlagPropagated()
        {
            const string ChannelName = "test-RTL2d-resumed";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                        ChannelName,
                        new Dictionary<string, JToken> { ["flags"] = ResumedFlag }));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            ChannelStateChange capturedChange = null;
            channel.On(ChannelEvent.Attached, change => capturedChange = change);

            await channel.AttachAsync();

            await UtsClients.PollUntil(() => capturedChange != null, "the ATTACHED event");

            capturedChange.Resumed.Should().BeTrue();
        }

        // UTS: realtime/unit/RTL24/error-reason-populated-0
        [Fact]
        public async Task RTL24_ErrorReasonPopulated()
        {
            const string ChannelName = "test-RTL24-populated";

            var (mockWs, client) = AttachingClient(ChannelName);
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 40160, "Not authorized", 401));
            await failed;

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            channel.ErrorReason.Message.Should().Be("Not authorized");
        }

        // UTS: realtime/unit/RTL4c/error-reason-cleared-attach-0
        [Fact]
        public async Task RTL4c_ErrorReasonClearedOnAttach()
        {
            const string ChannelName = "test-RTL4c-cleared";

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
                        mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                            ChannelName,
                            40160,
                            "Denied",
                            401));
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

            var first = await channel.AttachAsync();
            first.IsSuccess.Should().BeFalse();
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();

            await channel.AttachAsync();

            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull("RTL4c");
        }

        private (MockWebSocket MockWs, PubSubRealtimeClient Client) AttachingClient(string channelName)
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
            return (mockWs, client);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
