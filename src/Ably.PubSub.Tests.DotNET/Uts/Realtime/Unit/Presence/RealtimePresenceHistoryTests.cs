using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Presence
{
    /// <summary>
    /// Derived from uts/realtime/unit/presence/realtime_presence_history.md in ably/specification.
    ///
    /// Spec points: RTP12, RTP12a, RTP12c, RTP12d
    ///
    /// <para>
    /// Both tests need two seams at once: the WebSocket mock to get the channel attached, and the
    /// HTTP mock because <c>presence.history()</c> is a REST call made by the realtime client.
    /// <c>UtsClients.RealtimeClient</c> takes both.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>history(start:, end:, direction:, limit:)</c> is
    /// <c>HistoryAsync(PaginatedRequestParams)</c> here, the same shape the REST presence tests use.
    /// Its <c>start</c> and <c>end</c> are <c>DateTimeOffset?</c>, so the spec's millisecond values
    /// are passed as the instants they denote and asserted against the same numbers on the wire.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimePresenceHistoryTests : UtsTestBase
    {
        private const string ChannelName = "test-RTP12";

        public RealtimePresenceHistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP12a/history-supports-rest-params-0
        [Fact]
        public async Task RTP12a_HistorySupportsRestParams()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var channel = await AttachedChannel(mockHttp);

            await channel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(2000),
                Direction = QueryDirection.Backwards,
                Limit = 50,
            });

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be($"/channels/{ChannelName}/presence/history");
            request.Url.QueryParams["start"].Should().Be("1000");
            request.Url.QueryParams["end"].Should().Be("2000");
            request.Url.QueryParams["direction"].Should().Be("backwards");
            request.Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: realtime/unit/RTP12c/history-returns-paginated-result-0
        [Fact]
        public async Task RTP12c_HistoryReturnsPaginatedResult()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new object[]
                {
                    new { action = 2, clientId = "alice" },
                    new { action = 4, clientId = "bob" },
                    new { action = 3, clientId = "carol" },
                }));

            var channel = await AttachedChannel(mockHttp);

            var result = await channel.Presence.HistoryAsync();

            result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
            result.Items.Should().HaveCount(3);
            result.Items[0].ClientId.Should().Be("alice");
            result.Items[0].Action.Should().Be(PresenceAction.Enter);
            result.Items[2].Action.Should().Be(PresenceAction.Leave);
        }

        private async Task<IRealtimeChannel> AttachedChannel(MockHttpClient mockHttp)
        {
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
            var attached = UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.Attach();
            await attached;

            return channel;
        }
    }
}
