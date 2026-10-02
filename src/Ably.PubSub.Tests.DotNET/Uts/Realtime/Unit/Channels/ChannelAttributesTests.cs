using System;
using System.Collections.Generic;
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
    /// Derived from uts/realtime/unit/channels/channel_attributes.md in ably/specification.
    ///
    /// Spec points: RTL4c, RTL14, RTL23, RTL24
    ///
    /// <para>
    /// The spec's <c>AWAIT channel.attach() FAILS WITH error</c> is a returned <c>Result</c> here,
    /// not a thrown exception: <c>IRealtimeChannel.AttachAsync</c> is <c>Task&lt;Result&gt;</c>, so a
    /// refused attach completes with <c>IsSuccess == false</c> and the error on the result.
    /// Idiomatic, not a deviation - the failure is reported, just not by throwing.
    /// </para>
    ///
    /// <para>
    /// <c>channel.errorReason</c> is <c>IRealtimeChannel.ErrorReason</c>. The tests that wait for a
    /// channel to fail read the transition out of a recorder where the state is short-lived, for
    /// the reason set out on the connection tests, but a FAILED channel is terminal and stays put,
    /// so these sample the property as the spec writes it.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelAttributesTests : UtsTestBase
    {
        public ChannelAttributesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL23/name-attribute-0
        [Fact]
        public void RTL23_NameAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Get("my-channel").Name.Should().Be("my-channel");
            client.Channels.Get("namespace:channel-name").Name.Should().Be("namespace:channel-name");
        }

        // UTS: realtime/unit/RTL24/error-reason-channel-error-0
        [Fact]
        public async Task RTL24_ErrorReasonSetOnChannelError()
        {
            const string ChannelName = "test-RTL24-error";

            var (mockWs, channel) = await AttachedChannel(ChannelName);

            channel.ErrorReason.Should().BeNull("nothing has gone wrong yet");

            var failed = UtsClients.AwaitChannelState(channel, ChannelState.Failed);
            mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(
                ChannelName,
                90001,
                "Channel error occurred",
                500));
            await failed;

            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(90001);
            ((int)channel.ErrorReason.StatusCode.Value).Should().Be(500);
            channel.ErrorReason.Message.Should().Be("Channel error occurred");
        }

        // UTS: realtime/unit/RTL24/error-reason-attach-failure-1
        [Fact]
        public async Task RTL24_ErrorReasonSetOnAttachFailure()
        {
            const string ChannelName = "test-RTL24-attach-fail";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                        ChannelName,
                        40160,
                        "Not permitted to attach",
                        401));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse("the server refused the attach");

            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int)channel.ErrorReason.StatusCode.Value).Should().Be(401);
        }

        // UTS: realtime/unit/RTL4c/error-cleared-on-attach-0
        [Fact]
        public async Task RTL4c_ErrorReasonClearedOnSuccessfulAttach()
        {
            const string ChannelName = "test-RTL24-clear-attach";

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
                        mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                            ChannelName,
                            50000,
                            "Temporary error",
                            500));
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

            var firstAttach = await channel.AttachAsync();

            firstAttach.IsSuccess.Should().BeFalse();

            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(50000);

            await channel.AttachAsync();

            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull("RTL4c - a successful attach clears the error");
        }

        private async Task<(MockWebSocket MockWs, IRealtimeChannel Channel)> AttachedChannel(
            string channelName)
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
