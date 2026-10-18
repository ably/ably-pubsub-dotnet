// RealtimeChannel History Tests (RTL10, RTL10a, RTL10b, RTL10c)
// Derived from uts/realtime/unit/channels/channel_history.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    public class ChannelHistoryTests : UtsUnitTestBase
    {
        public ChannelHistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL10a/supports-rest-params-0
        [UtsFact(Skip = "Mock infrastructure limitation: the shared REST cases helper for uts/rest/unit/channel/history.md (HistoryCases) does not exist yet; see deviations.md")]
        [Trait("spec", "RTL10a")]
        public async Task RTL10a_SupportsRestParams()
        {
            // NOTE: delegating spec: the tests in uts/rest/unit/channel/history.md (RSL2) are to be run against a
            // RealtimeChannel (channel.HistoryAsync) through the shared helper HistoryCases beside the REST suite;
            // that REST spec is not translated in this run, so the helper does not exist.
            // RTL10a: Supports all the same params as RestChannel#history
            // RTL10c: Returns a PaginatedResult page containing the first page of messages
            await Task.CompletedTask;
        }

        // UTS: realtime/unit/RTL10b/adds-from-serial-0
        [UtsFact(Skip = "Missing SDK API: the SDK has no untilAttach history parameter (RealtimeChannel history takes no untilAttach); see deviations.md (Failing Tests)")]
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
        [UtsFact(Skip = "Missing SDK API: the SDK has no untilAttach history parameter (RealtimeChannel history takes no untilAttach); see deviations.md (Failing Tests)")]
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
