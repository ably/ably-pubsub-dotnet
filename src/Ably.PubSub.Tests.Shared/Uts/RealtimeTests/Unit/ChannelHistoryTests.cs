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
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#history(untilAttach) (RTL10b); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL10b")]
        public async Task RTL10b_AddsFromSerial()
        {
            // NOTE: IRealtimeChannel.HistoryAsync has no untilAttach option (realtime-mapping.md, Missing APIs); the whole test depends on it.
            // channel_name = "test-RTL10b-${random_id()}"
            // attach_serial = "serial-abc:0"
            // mock_http answers history with 200 [] and captures requests; the mock WebSocket answers ATTACH with
            // ATTACHED(channel: channel_name, channelSerial: attach_serial)
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false), ...)
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))
            // client.connect()
            // AWAIT_STATE connection == CONNECTED
            // AWAIT channel.attach()
            // ASSERT channel.state == ATTACHED
            // AWAIT channel.history(untilAttach: true)
            // request = captured_requests[0]
            // ASSERT request.url.query_params["fromSerial"] == attach_serial
            await Task.CompletedTask;
        }

        // UTS: realtime/unit/RTL10b/errors-when-not-attached-1
        [UtsFact(Skip = "Missing SDK public API: RealtimeChannel#history(untilAttach) (RTL10b); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTL10b")]
        public async Task RTL10b_ErrorsWhenNotAttached()
        {
            // NOTE: IRealtimeChannel.HistoryAsync has no untilAttach option (realtime-mapping.md, Missing APIs); the whole test depends on it.
            // channel_name = "test-RTL10b-err-${random_id()}"
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false), webSocketClient: mock_ws)
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))
            // client.connect()
            // AWAIT_STATE connection == CONNECTED
            // ASSERT channel.state == INITIALIZED
            // error = null
            // TRY:
            //   AWAIT channel.history(untilAttach: true)
            // CATCH e:
            //   error = e
            // ASSERT error IS AblyException
            await Task.CompletedTask;
        }
    }
}
