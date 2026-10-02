using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Channel
{
    /// <summary>
    /// Derived from uts/rest/unit/channel/history.md in ably/specification.
    ///
    /// Spec points: RSL2, RSL2a, RSL2b, RSL2b1, RSL2b2, RSL2b3
    ///
    /// Two translation notes that apply to the whole file:
    ///
    /// The spec passes history parameters as a map of unix milliseconds. The .NET equivalent is
    /// <see cref="PaginatedRequestParams"/>, whose Start and End are DateTimeOffset, so each spec
    /// value is converted with DateTimeOffset.FromUnixTimeMilliseconds. The assertions are still made
    /// against the millisecond string the spec expects on the wire, which is what
    /// PaginatedRequestParams.GetParameters emits.
    ///
    /// RSL2b1 and RSL2b3 are written as "either absent or this value" in the spec. This SDK always
    /// emits both direction and limit, so those two tests assert the parameter is present as well as
    /// its value: translating the spec's optionality literally would let the assertion pass whatever
    /// the SDK sent, and a change to omitting them is an observable change worth catching.
    /// </summary>
    public class HistoryTests : UtsTestBase
    {
        public HistoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL2a/returns-paginated-result-0
        [Fact]
        public async Task RSL2a_HistoryReturnsPaginatedResult()
        {
            var channelName = "test-RSL2a-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { id = "msg1", name = "event1", data = "data1", timestamp = 1000 },
                        new { id = "msg2", name = "event2", data = "data2", timestamp = 2000 },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var result = await channel.HistoryAsync();

            // "result IS PaginatedResult" and "items[0] IS Message" are static guarantees here:
            // HistoryAsync is typed PaginatedResult<Message> and Items is typed List<Message>, so the
            // runtime type assertions cannot fail. BeOfType still pins the concrete type returned.
            result.Should().BeOfType<PaginatedResult<Message>>();
            result.Items.Should().HaveCount(2);

            result.Items[0].Id.Should().Be("msg1");
            result.Items[0].Name.Should().Be("event1");
            result.Items[0].Data.Should().Be("data1");

            capturedRequests.Should().HaveCount(1);
        }

        // UTS: rest/unit/RSL2b/query-parameters-0
        [Fact]
        public async Task RSL2b_HistoryQueryParameters()
        {
            var channelName = "test-RSL2b-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            // The spec's five-row table, in order. Each row gets a fresh params object carrying only
            // that one parameter, which is the spec's params[test_case.parameter] = test_case.value.
            await AssertQueryParameter(
                channel,
                capturedRequests,
                new PaginatedRequestParams { Start = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000) },
                "start",
                "1234567890000");

            await AssertQueryParameter(
                channel,
                capturedRequests,
                new PaginatedRequestParams { End = DateTimeOffset.FromUnixTimeMilliseconds(1234567899999) },
                "end",
                "1234567899999");

            await AssertQueryParameter(
                channel,
                capturedRequests,
                new PaginatedRequestParams { Direction = QueryDirection.Backwards },
                "direction",
                "backwards");

            await AssertQueryParameter(
                channel,
                capturedRequests,
                new PaginatedRequestParams { Direction = QueryDirection.Forwards },
                "direction",
                "forwards");

            await AssertQueryParameter(
                channel,
                capturedRequests,
                new PaginatedRequestParams { Limit = 50 },
                "limit",
                "50");
        }

        // UTS: rest/unit/RSL2b1/default-direction-backwards-0
        [Fact]
        public async Task RSL2b1_DefaultDirectionBackwards()
        {
            var channelName = "test-RSL2b1-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.HistoryAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            // The spec allows the parameter to be absent and the server default to apply. This SDK
            // always sends it (PaginatedRequestParams.GetParameters), so assert both the presence and
            // the value, rather than leaving an assertion that would pass whatever the SDK sent.
            request.Url.QueryParams.Should().ContainKey("direction");
            request.Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSL2b2/limit-parameter-0
        [Fact]
        public async Task RSL2b2_LimitParameter()
        {
            var channelName = "test-RSL2b2-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { id = "msg1", name = "e", data = "d", timestamp = 1000 },
                        new { id = "msg2", name = "e", data = "d", timestamp = 2000 },
                        new { id = "msg3", name = "e", data = "d", timestamp = 3000 },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.HistoryAsync(new PaginatedRequestParams { Limit = 10 });

            // NOTE: the spec's prose says limit "restricts the number of returned items", but its only
            // assertion is on the query parameter, because enforcing the limit is the server's job and
            // there is no server here. Translated as written.
            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("10");
        }

        // UTS: rest/unit/RSL2b3/default-limit-hundred-0
        [Fact]
        public async Task RSL2b3_DefaultLimitHundred()
        {
            var channelName = "test-RSL2b3-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.HistoryAsync();

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            // As with RSL2b1: the spec allows absence, this SDK always sends the default of 100.
            request.Url.QueryParams.Should().ContainKey("limit");
            request.Url.QueryParams["limit"].Should().Be("100");
        }

        // UTS: rest/unit/RSL2/request-url-format-0
        [Fact]
        public async Task RSL2_RequestUrlFormat()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp);

            // The spec's four-row table. The expected paths are the spec's literal percent-encodings
            // rather than a call back into the SDK's own encoder, so the assertion is not a tautology.
            var suffix = UtsSandbox.RandomId();

            await AssertHistoryPath(
                client,
                capturedRequests,
                "test-RSL2-simple-" + suffix,
                "/channels/test-RSL2-simple-" + suffix + "/messages");

            await AssertHistoryPath(
                client,
                capturedRequests,
                "test-RSL2-with:colon-" + suffix,
                "/channels/test-RSL2-with%3Acolon-" + suffix + "/messages");

            await AssertHistoryPath(
                client,
                capturedRequests,
                "test-RSL2-with/slash-" + suffix,
                "/channels/test-RSL2-with%2Fslash-" + suffix + "/messages");

            await AssertHistoryPath(
                client,
                capturedRequests,
                "test-RSL2-with space-" + suffix,
                "/channels/test-RSL2-with%20space-" + suffix + "/messages");
        }

        // UTS: rest/unit/RSL2/history-time-range-1
        [Fact]
        public async Task RSL2_HistoryTimeRange()
        {
            var channelName = "test-RSL2-timerange-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { id = "msg1", name = "e", data = "d", timestamp = 1500 },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(2000),
            });

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            request.Url.QueryParams["start"].Should().Be("1000");
            request.Url.QueryParams["end"].Should().Be("2000");
        }

        private static async Task AssertQueryParameter(
            IHttpChannel channel,
            List<PendingHttpRequest> capturedRequests,
            PaginatedRequestParams query,
            string parameter,
            string expected)
        {
            capturedRequests.Clear();

            await channel.HistoryAsync(query);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.QueryParams.Should().ContainKey(parameter);
            capturedRequests[0].Url.QueryParams[parameter].Should().Be(expected);
        }

        private static async Task AssertHistoryPath(
            PubSubHttpClient client,
            List<PendingHttpRequest> capturedRequests,
            string channelName,
            string expectedPath)
        {
            capturedRequests.Clear();

            var channel = client.Channels.Get(channelName);
            await channel.HistoryAsync();

            capturedRequests.Should().HaveCount(1, "the spec asserts request_count == 1");
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be(expectedPath);
        }
    }
}
