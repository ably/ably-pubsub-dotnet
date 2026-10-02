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
    /// Derived from uts/realtime/unit/channels/channel_additional_attached.md in ably/specification.
    ///
    /// Spec point: RTL12
    ///
    /// <para>
    /// A second ATTACHED on an already-attached channel is not a state change. Without the RESUMED
    /// flag it means continuity was lost and an UPDATE is emitted carrying whatever error the
    /// message brought; with it, nothing is emitted at all.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelAdditionalAttachedTests : UtsTestBase
    {
        private const int ResumedFlag = 1 << 2;

        public ChannelAdditionalAttachedTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL12/update-emits-with-error-0
        [Fact]
        public async Task RTL12_AdditionalAttachedWithoutResumedEmitsUpdate()
        {
            const string ChannelName = "test-RTL12-update";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var updateEvents = new List<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            var attached = ProtocolMessages.AttachedMessage(ChannelName);
            attached["error"] = ProtocolMessages.ErrorObject(50000, "Continuity lost", 500);
            mockWs.SendToClient(attached);

            await UtsClients.PollUntil(() => updateEvents.Count >= 1, "the UPDATE event");

            channel.State.Should().Be(ChannelState.Attached);

            updateEvents.Should().HaveCount(1);
            updateEvents[0].Event.Should().Be(ChannelEvent.Update);
            updateEvents[0].Current.Should().Be(ChannelState.Attached);
            updateEvents[0].Previous.Should().Be(ChannelState.Attached);
            updateEvents[0].Resumed.Should().BeFalse();
            updateEvents[0].Error.Code.Should().Be(50000);
        }

        // UTS: realtime/unit/RTL12/resumed-no-update-1
        [Fact]
        public async Task RTL12_AdditionalAttachedWithResumedEmitsNothing()
        {
            const string ChannelName = "test-RTL12-no-update";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var updateEvents = new List<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                ChannelName,
                new Dictionary<string, JToken> { ["flags"] = ResumedFlag }));

            // Nothing to wait for, so round-trip the workflow to give it the chance to emit.
            await channel.Presence.GetAsync(waitForSync: false);

            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Should().BeEmpty("RTL12 - a resumed re-attach lost nothing");
        }

        // UTS: realtime/unit/RTL12/no-error-null-reason-2
        [Fact]
        public async Task RTL12_AdditionalAttachedWithoutErrorHasNullReason()
        {
            const string ChannelName = "test-RTL12-no-error";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            var updateEvents = new List<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await UtsClients.PollUntil(() => updateEvents.Count >= 1, "the UPDATE event");

            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Should().HaveCount(1);
            updateEvents[0].Error.Should().BeNull("the ATTACHED carried no error");
        }

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            string channelName)
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs);

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

            return (mockWs, channel);
        }
    }
}
