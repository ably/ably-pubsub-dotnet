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
    /// Derived from uts/realtime/unit/channels/channel_history.md in ably/specification.
    ///
    /// Spec points: RTL10a, RTL10c
    ///
    /// <para>
    /// RTL10a is a pointer rather than a test of its own - its whole body says that
    /// <c>RealtimeChannel#history</c> uses the same REST endpoint as <c>RestChannel#history</c> and
    /// that the RSL2 tests "should be used to verify that all the same behaviour, parameters, and
    /// return types apply when called on a `RealtimeChannel` instance". The test below does exactly
    /// that: the same parameters the REST tests assert, through a realtime channel.
    /// </para>
    ///
    /// <para>
    /// RTL10b's two tests are not translated: there is no <c>untilAttach</c> overload on
    /// <c>IRealtimeChannel.HistoryAsync</c>. See Uts/coverage.md.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelHistoryTests : UtsTestBase
    {
        private const string ChannelName = "test-RTL10a";

        public ChannelHistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL10a/supports-rest-params-0
        [Fact]
        public async Task RTL10a_SupportsRestHistoryParams()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { name = "event", data = "payload" },
                    });
                });

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });

            var client = RealtimeClient(mockWs, mockHttp);

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
            await channel.AttachAsync();

            var result = await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(2000),
                Direction = QueryDirection.Forwards,
                Limit = 25,
            });

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be($"/channels/{ChannelName}/messages");
            request.Url.QueryParams["start"].Should().Be("1000");
            request.Url.QueryParams["end"].Should().Be("2000");
            request.Url.QueryParams["direction"].Should().Be("forwards");
            request.Url.QueryParams["limit"].Should().Be("25");

            result.Should().BeOfType<PaginatedResult<Message>>("RTL10c");
            result.Items.Should().HaveCount(1);
            result.Items[0].Name.Should().Be("event");
        }
    }
}
