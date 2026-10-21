// RealtimeChannel History Tests (RTL10, RTL10a, RTL10b, RTL10c)
// Derived from uts/realtime/unit/channels/channel_history.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: delegating spec (RTL10a): the uts/rest/unit/channel/history.md tests run through an inherited client-agnostic base (HistoryTestsBase); realtime client built with AutoConnect = false (infra-driving: only the HTTP mock is exercised)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Tests.Uts.RestTests.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelHistoryTests : HistoryTestsBase<PubSubRealtimeClient, IRealtimeChannel>
    {
        public ChannelHistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL10a/supports-rest-params-0
        // NOTE: the spec runs the uts/rest/unit/channel/history.md tests against a realtime channel in place of a REST channel. They are
        // inherited unchanged from HistoryTestsBase (RestTests/Unit/HistoryTests.cs) and run here through the overrides below; the
        // realtime client is built with AutoConnect = false so only the HTTP mock is exercised (realtime-mapping.md section 12).
        // RTL10c (a PaginatedResult page of the first messages) is covered by the inherited rest/unit/RSL2a/returns-paginated-result-0.
        protected override PubSubRealtimeClient NewClient(ClientOptions options)
        {
            options.AutoConnect = false;
            return Realtime(options);
        }

        protected override IRealtimeChannel GetChannel(PubSubRealtimeClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<PaginatedResult<Message>> HistoryAsync(IRealtimeChannel channel) => channel.HistoryAsync();

        protected override Task<PaginatedResult<Message>> HistoryAsync(IRealtimeChannel channel, PaginatedRequestParams query) => channel.HistoryAsync(query);

        // UTS: realtime/unit/RTL10b/adds-from-serial-0
        [UtsFact]
        [Trait("spec", "RTL10b")]
        public async Task RTL10b_AddsFromSerial()
        {
            // Setup
            var channelName = "test-RTL10b-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var attachSerial = "serial-abc:0";

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(
                            channelName,
                            new Dictionary<string, JToken> { ["channelSerial"] = attachSerial }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: RealtimeChannelOptions(attachOnSubscribe: false) is dropped: the SDK has no attachOnSubscribe (TB4) and this test never subscribes, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // DEVIATION: RealtimeChannel#history(untilAttach) absent; using internal RealtimeChannel.AddUntilAttachParameter + HistoryAsync(query)
            // AWAIT channel.history(untilAttach: true)
            var query = new PaginatedRequestParams();
            ((RealtimeChannel)channel).AddUntilAttachParameter(query);
            await UtsWait.WithTimeout(channel.HistoryAsync(query), TimeSpan.FromSeconds(10), "HistoryAsync");

            // Assertions
            var request = capturedRequests[0];
            request.Url.QueryParams["fromSerial"].Should().Be(attachSerial);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL10b/errors-when-not-attached-1
        [UtsFact]
        [Trait("spec", "RTL10b")]
        public async Task RTL10b_ErrorsWhenNotAttached()
        {
            // Setup
            var channelName = "test-RTL10b-err-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: RealtimeChannelOptions(attachOnSubscribe: false) is dropped: the SDK has no attachOnSubscribe (TB4) and this test never subscribes, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            Exception error = null;
            try
            {
                // DEVIATION: RealtimeChannel#history(untilAttach) absent; using internal RealtimeChannel.AddUntilAttachParameter + HistoryAsync(query)
                // AWAIT channel.history(untilAttach: true)
                var query = new PaginatedRequestParams();
                ((RealtimeChannel)channel).AddUntilAttachParameter(query);
                await channel.HistoryAsync(query);
            }
            catch (Exception e)
            {
                error = e;
            }

            // Assertions
            error.Should().BeOfType<AblyException>();
            await CloseClient(client);
        }
    }
}
