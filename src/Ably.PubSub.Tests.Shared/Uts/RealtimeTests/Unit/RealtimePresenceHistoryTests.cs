// RealtimePresence History Tests (RTP12, RTP12a, RTP12c, RTP12d)
// Derived from uts/realtime/unit/presence/realtime_presence_history.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key stand-in "fake.key:secret" as the spec sets it; the spec's MockRest is the infra MockHttpClient (REST history runs through the realtime client's REST client); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimePresenceHistoryTests : UtsUnitTestBase
    {
        public RealtimePresenceHistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTP12a/history-supports-rest-params-0
        [UtsFact]
        [Trait("spec", "RTP12a")]
        public async Task RTP12a_HistorySupportsRestParams()
        {
            // Setup
            var channelName = "test-RTP12a-" + Guid.NewGuid().ToString("N");

            var capturedHistoryRequests = new CaptureList<PendingHttpRequest>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            // Mock the REST history endpoint
            var mockRest = new MockHttpClient(onRequest: req =>
            {
                capturedHistoryRequests.Add(req);
                req.RespondWith(200, new JArray());
            });
            InstallMock(mockRest);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var historyTask = channel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(2000),
                Direction = QueryDirection.Backwards,
                Limit = 50,
            });
            var result = await UtsWait.WithTimeout(historyTask, TimeSpan.FromSeconds(10), "HistoryAsync");

            // Assertions
            capturedHistoryRequests.Should().HaveCount(1);
            capturedHistoryRequests[0].Path.Should().Be($"/channels/{Uri.EscapeDataString(channelName)}/presence/history");
            capturedHistoryRequests[0].QueryParams["start"].Should().Be("1000");
            capturedHistoryRequests[0].QueryParams["end"].Should().Be("2000");
            capturedHistoryRequests[0].QueryParams["direction"].Should().Be("backwards");
            capturedHistoryRequests[0].QueryParams["limit"].Should().Be("50");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP12c/history-returns-paginated-result-0
        [UtsFact]
        [Trait("spec", "RTP12c")]
        public async Task RTP12c_HistoryReturnsPaginatedResult()
        {
            // Setup
            var channelName = "test-RTP12c-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            // Note: The REST API returns presence actions as numeric values on the wire
            // (ABSENT=0, PRESENT=1, ENTER=2, LEAVE=3, UPDATE=4). Mock responses should use
            // the format appropriate for the SDK's REST layer. Assertions use symbolic names
            // which correspond to the SDK's public API representation.
            var mockRest = new MockHttpClient(onRequest: req =>
            {
                req.RespondWith(200, new JArray(
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Enter, "alice", timestamp: 1000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Update, "alice", timestamp: 2000),
                    ProtocolMessages.PresenceEntry((int)PresenceAction.Leave, "alice", timestamp: 3000)));
            });
            InstallMock(mockRest);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var result = await UtsWait.WithTimeout(channel.Presence.HistoryAsync(), TimeSpan.FromSeconds(10), "HistoryAsync");

            // Assertions
            result.Should().BeAssignableTo<PaginatedResult<PresenceMessage>>();
            result.Items.Should().HaveCount(3);
            result.Items[0].ClientId.Should().Be("alice");
            result.Items[0].Action.Should().Be(PresenceAction.Enter);
            result.Items[2].Action.Should().Be(PresenceAction.Leave);

            await CloseClient(client);
        }
    }
}
