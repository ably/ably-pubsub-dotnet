using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channel_error.md in ably/specification.
    ///
    /// Spec point: RTL14
    ///
    /// <para>
    /// A channel-scoped ERROR fails that channel and leaves everything else alone - the connection
    /// stays up, other channels stay attached, and any pending channel retry is abandoned.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>AWAIT channel.attach() FAILS WITH error</c> is a returned <c>Result</c> here -
    /// see the note on <see cref="ChannelAttributesTests"/>.
    /// </para>
    ///
    /// <para>
    /// The last test's <c>enable_fake_timers()</c> splits the way the skill's Timers rule
    /// prescribes: the attach deadline and the channel retry are both scheduled on real
    /// <c>CountdownTimer</c>s with no injection point, so they become shortened client options
    /// (<c>RealtimeRequestTimeout</c>, <c>ChannelRetryTimeout</c>) and the spec's
    /// <c>ADVANCE_TIME</c> becomes a real wait long enough to cover them.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelErrorTests : UtsTestBase
    {
        public ChannelErrorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL14/attached-to-failed-0
        [Fact]
        public async Task RTL14_AttachedChannelGoesToFailed()
        {
            const string ChannelName = "test-RTL14-attached";

            var (mockWs, client, channel) = await AttachedChannel(ChannelName);

            channel.State.Should().Be(ChannelState.Attached);

            var stateChanges = new List<ChannelStateChange>();
            channel.On(change => stateChanges.Add(change));

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 40160, "Not permitted", 401));
            await failed;

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int)channel.ErrorReason.StatusCode.Value).Should().Be(401);
            channel.ErrorReason.Message.Should().Contain("Not permitted");

            stateChanges.Should().HaveCount(1);
            stateChanges[0].Current.Should().Be(ChannelState.Failed);
            stateChanges[0].Previous.Should().Be(ChannelState.Attached);
            stateChanges[0].Error.Should().NotBeNull();
            stateChanges[0].Error.Code.Should().Be(40160);

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "a channel-scoped ERROR does not touch the connection");
        }

        // UTS: realtime/unit/RTL14/attaching-to-failed-1
        [Fact]
        public async Task RTL14_AttachingChannelGoesToFailed()
        {
            const string ChannelName = "test-RTL14-attaching";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        40160,
                        "Not permitted",
                        401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Code.Should().Be(40160, "the attach fails with the channel's error");

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTL14/pending-detach-error-2
        [Fact]
        public async Task RTL14_PendingDetachCompletesWithError()
        {
            const string ChannelName = "test-RTL14-detaching";

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
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                        ChannelName,
                        90198,
                        "Detach failed",
                        500));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var result = await channel.DetachAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Code.Should().Be(90198);

            channel.State.Should().Be(ChannelState.Failed, "FAILED, not DETACHED");
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(90198);

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTL14/other-channels-unaffected-3
        [Fact]
        public async Task RTL14_OtherChannelsUnaffected()
        {
            const string ChannelA = "test-RTL14-a";
            const string ChannelB = "test-RTL14-b";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channelA = client.Channels.Get(ChannelA);
            var channelB = client.Channels.Get(ChannelB);

            await channelA.AttachAsync();
            await channelB.AttachAsync();

            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            var failed = UtsClients.AwaitChannelState(channelA, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelA, 40160, "Not permitted", 401));
            await failed;

            channelA.State.Should().Be(ChannelState.Failed);
            channelA.ErrorReason.Should().NotBeNull();

            channelB.State.Should().Be(ChannelState.Attached, "the error named channel A only");
            channelB.ErrorReason.Should().BeNull();

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTL14/cancels-pending-timers-4
        //
        // DEVIATION. RTL14 requires a channel ERROR to cancel any pending channel retry. Nothing
        // can cancel this one: RealtimeChannel.ReattachAfterTimeout (RealtimeChannel.cs:785-804)
        // fires a detached `Task.Run(async () => { await Task.Delay(retryTimeout); ... })` with no
        // handle kept, and its only guard before reattaching is
        // `Connection.State == ConnectionState.Connected` - it never looks at the channel's own
        // state. Measured: the channel reached FAILED, then a second later was back in SUSPENDED,
        // having been reattached out of its terminal state. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL14_CancelsPendingChannelRetryTimer()
        {
            const string ChannelName = "test-RTL14-timers";

            var attachCount = 0;
            var mockWs = ConnectingMock();

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                options.ChannelRetryTimeout = TimeSpan.FromMilliseconds(200);
            });

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    if (attachCount == 1)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                    }

                    // Later attaches go unanswered, so the reattach times out into SUSPENDED.
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            attachCount.Should().Be(1);

            // A server-initiated DETACHED provokes a reattach, which times out into SUSPENDED and
            // arms the channel retry timer.
            var suspended = UtsClients.AwaitChannelState(
                channel,
                ChannelState.Suspended,
                TimeSpan.FromSeconds(10));

            mockWs.SendToClient(
                ProtocolMessages.ServerDetachedMessage(ChannelName, 90198, "Detach", 500));

            await suspended;

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(
                ProtocolMessages.ChannelErrorMessage(ChannelName, 40160, "Not permitted", 401));
            await failed;

            var attachCountAfterError = attachCount;

            // Well past channelRetryTimeout. If the retry were still armed it would fire in here.
            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            channel.State.Should().Be(ChannelState.Failed, "no retry was attempted");
            attachCount.Should().Be(
                attachCountAfterError,
                "RTL14 - the channel ERROR cancels the pending retry");
        }

        private async Task<(MockWebSocket MockWs, PubSubRealtimeClient Client, IRealtimeChannel Channel)>
            AttachedChannel(string channelName)
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
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();

            return (mockWs, client, channel);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
