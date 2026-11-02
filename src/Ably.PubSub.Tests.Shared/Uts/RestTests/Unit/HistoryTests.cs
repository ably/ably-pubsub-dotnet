// REST Channel History Tests (RSL2, RSL2a, RSL2b, RSL2b1, RSL2b2, RSL2b3)
// Derived from uts/rest/unit/channel/history.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    /// <summary>
    /// The uts/rest/unit/channel/history.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="HistoryTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: ChannelHistoryTests (realtime/unit/RTL10a/supports-rest-params-0).
    /// </summary>
    public abstract class HistoryTestsBase<TClient, TChannel> : UtsUnitTestBase
    {
        protected HistoryTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the client under test.</summary>
        protected abstract TClient NewClient(ClientOptions options);

        /// <summary>The spec's <c>client.channels.get(name)</c>.</summary>
        protected abstract TChannel GetChannel(TClient client, string channelName);

        /// <summary>The spec's <c>channel.history()</c>.</summary>
        protected abstract Task<PaginatedResult<Message>> HistoryAsync(TChannel channel);

        /// <summary>The spec's <c>channel.history(params)</c>.</summary>
        protected abstract Task<PaginatedResult<Message>> HistoryAsync(TChannel channel, PaginatedRequestParams query);

        // UTS: rest/unit/RSL2a/returns-paginated-result-0
        [UtsFact]
        [Trait("spec", "RSL2a")]
        public async Task RSL2a_ReturnsPaginatedResult()
        {
            // Setup
            var channelName = "test-RSL2a-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject { ["id"] = "msg1", ["name"] = "event1", ["data"] = "data1", ["timestamp"] = 1000 },
                        new JObject { ["id"] = "msg2", ["name"] = "event2", ["data"] = "data2", ["timestamp"] = 2000 },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var result = await HistoryAsync(channel);

            // Assertions
            result.Should().BeAssignableTo<PaginatedResult<Message>>();
            result.Items.Should().BeAssignableTo<IEnumerable<Message>>();
            result.Items.Should().HaveCount(2);

            result.Items[0].Should().BeAssignableTo<Message>();
            result.Items[0].Id.Should().Be("msg1");
            result.Items[0].Name.Should().Be("event1");
            result.Items[0].Data.Should().Be("data1");
        }

        // UTS: rest/unit/RSL2b/query-parameters-0
        [UtsFact]
        [Trait("spec", "RSL2b")]
        public async Task RSL2b_QueryParameters()
        {
            // Setup
            var channelName = "test-RSL2b-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Cases
            var testCases = new[]
            {
                new { Parameter = "start", Value = "1234567890000" },
                new { Parameter = "end", Value = "1234567899999" },
                new { Parameter = "direction", Value = "backwards" },
                new { Parameter = "direction", Value = "forwards" },
                new { Parameter = "limit", Value = "50" },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();

                // NOTE: the spec's params dictionary is the SDK's typed PaginatedRequestParams (Start/End as DateTimeOffset, Direction as QueryDirection, Limit as int)
                var query = new PaginatedRequestParams();
                switch (testCase.Parameter)
                {
                    case "start":
                        query.Start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(testCase.Value));
                        break;
                    case "end":
                        query.End = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(testCase.Value));
                        break;
                    case "direction":
                        query.Direction = testCase.Value == "forwards" ? QueryDirection.Forwards : QueryDirection.Backwards;
                        break;
                    case "limit":
                        query.Limit = int.Parse(testCase.Value);
                        break;
                }

                await HistoryAsync(channel, query);

                var request = capturedRequests[0];
                request.QueryParams[testCase.Parameter].Should().Be(testCase.Value);
            }
        }

        // UTS: rest/unit/RSL2b1/default-direction-backwards-0
        [UtsFact]
        [Trait("spec", "RSL2b1")]
        public async Task RSL2b1_DefaultDirectionBackwards()
        {
            // Setup
            var channelName = "test-RSL2b1-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            await HistoryAsync(channel); // No direction specified

            // Assertions
            var request = capturedRequests[0];

            // Either direction param is absent (server default) or explicitly "backwards"
            if (request.QueryParams.ContainsKey("direction"))
            {
                request.QueryParams["direction"].Should().Be("backwards");
            }

            // If absent, server defaults to backwards per spec
        }

        // UTS: rest/unit/RSL2b2/limit-parameter-0
        [UtsFact]
        [Trait("spec", "RSL2b2")]
        public async Task RSL2b2_LimitParameter()
        {
            // Setup
            var channelName = "test-RSL2b2-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject { ["id"] = "msg1", ["name"] = "e", ["data"] = "d", ["timestamp"] = 1000 },
                        new JObject { ["id"] = "msg2", ["name"] = "e", ["data"] = "d", ["timestamp"] = 2000 },
                        new JObject { ["id"] = "msg3", ["name"] = "e", ["data"] = "d", ["timestamp"] = 3000 },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            await HistoryAsync(channel, new PaginatedRequestParams { Limit = 10 });

            var request = capturedRequests[0];
            request.QueryParams["limit"].Should().Be("10");
        }

        // UTS: rest/unit/RSL2b3/default-limit-hundred-0
        [UtsFact]
        [Trait("spec", "RSL2b3")]
        public async Task RSL2b3_DefaultLimitHundred()
        {
            // Setup
            var channelName = "test-RSL2b3-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            await HistoryAsync(channel); // No limit specified

            // Assertions
            var request = capturedRequests[0];

            // Either limit param is absent (server default) or explicitly "100"
            if (request.QueryParams.ContainsKey("limit"))
            {
                request.QueryParams["limit"].Should().Be("100");
            }

            // If absent, server defaults to 100 per spec
        }

        // UTS: rest/unit/RSL2/request-url-format-0
        [UtsFact]
        [Trait("spec", "RSL2")]
        public async Task RSL2_RequestUrlFormat()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Cases
            var testCases = new[]
            {
                new { ChannelName = "test-RSL2-simple-" + Guid.NewGuid().ToString("N") },
                new { ChannelName = "test-RSL2-with:colon-" + Guid.NewGuid().ToString("N") },
                new { ChannelName = "test-RSL2-with/slash-" + Guid.NewGuid().ToString("N") },
                new { ChannelName = "test-RSL2-with space-" + Guid.NewGuid().ToString("N") },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();
                requestCount = 0;

                var channel = GetChannel(client, testCase.ChannelName);
                await HistoryAsync(channel);

                requestCount.Should().Be(1);
                var request = capturedRequests[0];
                request.Method.Should().Be("GET");
                request.Path.Should().Be($"/channels/{Uri.EscapeDataString(testCase.ChannelName)}/messages");
            }
        }

        // UTS: rest/unit/RSL2/history-time-range-1
        [UtsFact]
        [Trait("spec", "RSL2")]
        public async Task RSL2_HistoryTimeRange()
        {
            // Setup
            var channelName = "test-RSL2-timerange-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject { ["id"] = "msg1", ["name"] = "e", ["data"] = "d", ["timestamp"] = 1500 },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            await HistoryAsync(channel, new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(2000),
            });

            // Assertions
            var request = capturedRequests[0];
            request.QueryParams["start"].Should().Be("1000");
            request.QueryParams["end"].Should().Be("2000");
        }
    }

    /// <summary>The uts/rest/unit/channel/history.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class HistoryTests : HistoryTestsBase<PubSubHttpClient, IHttpChannel>
    {
        public HistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override PubSubHttpClient NewClient(ClientOptions options) => Rest(options);

        protected override IHttpChannel GetChannel(PubSubHttpClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<PaginatedResult<Message>> HistoryAsync(IHttpChannel channel) => channel.HistoryAsync();

        protected override Task<PaginatedResult<Message>> HistoryAsync(IHttpChannel channel, PaginatedRequestParams query) => channel.HistoryAsync(query);
    }
}
