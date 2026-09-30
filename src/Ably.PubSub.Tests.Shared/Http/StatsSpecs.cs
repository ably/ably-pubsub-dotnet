using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static Xunit.Assert;

namespace Ably.PubSub.Tests
{
    public class StatsSpecs : MockHttpRestSpecs
    {
        [Fact]
        public async Task ShouldCreateRequestToCorrectPath()
        {
            var rest = GetRestClient();

            await rest.StatsAsync();

            Equal(HttpMethod.Get, LastRequest.Method);
            Equal("/stats", LastRequest.Url);
        }

        [Fact]
        public async Task ShouldSetCorrectHeaders()
        {
            var rest = GetRestClient();

            var query = new StatsRequestParams
            {
                Start = Now.AddHours(-1),
                End = Now,
                Direction = QueryDirection.Forwards,
                Limit = 1000
            };

            await rest.StatsAsync(query);

            LastRequest.AssertContainsParameter("start", query.Start.Value.ToUnixTimeInMilliseconds().ToString());
            LastRequest.AssertContainsParameter("end", query.End.Value.ToUnixTimeInMilliseconds().ToString());
            LastRequest.AssertContainsParameter("direction", query.Direction.ToString().ToLower());
            LastRequest.AssertContainsParameter("limit", query.Limit.Value.ToString());
        }

        [Fact]
        public async Task ShouldReturnCorrectFirstAndNextLinks()
        {
            // Arrange
            var rest = GetRestClient(request =>
            {
                var response = new AblyResponse
                {
                    Headers = DataRequestQueryTests.GetSampleStatsRequestHeaders(),
                    TextResponse = "[{}]"
                };
                return response.ToTask();
            });

            // Act
            var result = await rest.StatsAsync();

            // Assert
            NotNull(result.NextQueryParams);
            NotNull(result.FirstQueryParams);
        }

        private async Task ExecuteStatsQuery(StatsRequestParams query)
        {
            var rest = GetRestClient();

            await rest.StatsAsync(query);
        }

        [Theory]
        [Trait("spec", "RSC6b1")]
        [InlineData(0, 0, QueryDirection.Backwards)]
        [InlineData(0, 0, QueryDirection.Forwards)]
        [InlineData(0, 1, QueryDirection.Forwards)]
        [InlineData(0, 1, QueryDirection.Backwards)]
        public async Task ShouldAcceptStartAndEndDateTimes(int startOffset, int endOffset, QueryDirection direction)
        {
            var query = new StatsRequestParams
            {
                Start = Now.AddHours(startOffset),
                End = Now.AddHours(endOffset),
                Direction = direction
            };

            await ExecuteStatsQuery(query);

            LastRequest.AssertContainsParameter("start", query.Start.Value.ToUnixTimeInMilliseconds().ToString());
            LastRequest.AssertContainsParameter("end", query.End.Value.ToUnixTimeInMilliseconds().ToString());
        }

        [Theory]
        [Trait("spec", "RSC6b1")]
        [InlineData(1, 0, QueryDirection.Forwards)]
        [InlineData(1, 0, QueryDirection.Backwards)]
        public void ShouldThrowIfStartIsGreaterThanEnd(int startOffset, int endOffset, QueryDirection direction)
        {
            var query = new StatsRequestParams
            {
                Start = Now.AddHours(startOffset),
                End = Now.AddHours(endOffset),
                Direction = direction
            };

            ThrowsAsync<AblyException>(() => ExecuteStatsQuery(query));
        }

        [Theory]
        [InlineData(QueryDirection.Forwards)]
        [InlineData(QueryDirection.Backwards)]
        [InlineData(null)]
        [Trait("spec", "RSC6b2")]
        public async Task ShouldPassDirectionToRequestWithBackwardsAsDefault(QueryDirection? direction)
        {
            var query = new StatsRequestParams
            {
                Start = Now,
                End = Now
            };
            if (direction.HasValue)
            {
                query.Direction = direction.Value;
            }

            await ExecuteStatsQuery(query);

            var expectedDirection = direction.GetValueOrDefault(QueryDirection.Backwards).ToString().ToLower();
            LastRequest.AssertContainsParameter("direction", expectedDirection);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(1000)]
        [InlineData(null)]
        [Trait("spec", "RSC6b3")]
        public async Task ShouldPassLimitWithDefaultof100(int? limit)
        {
            var query = new StatsRequestParams();
            if (limit.HasValue)
            {
                query.Limit = limit.Value;
            }

            await ExecuteStatsQuery(query);

            LastRequest.AssertContainsParameter("limit", limit.GetValueOrDefault(100).ToString());
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(1001)]
        [Trait("spec", "RSCb3")]
        public void ShouldThrowIfLimitExceeds1000OrLessThan0(int limit)
        {
            ThrowsAsync<AblyException>(() => ExecuteStatsQuery(new StatsRequestParams { Limit = limit }));
        }

        [Theory]
        [InlineData(StatsIntervalGranularity.Month)]
        [InlineData(StatsIntervalGranularity.Day)]
        [InlineData(StatsIntervalGranularity.Hour)]
        [InlineData(StatsIntervalGranularity.Minute)]
        [InlineData(null)]
        [Trait("spec", "RSC6b4")]
        public async Task ShouldPassStatsByToQueryWithDefaultOfMinute(StatsIntervalGranularity? statsGranularity)
        {
            var query = new StatsRequestParams();
            if (statsGranularity.HasValue)
            {
                query.Unit = statsGranularity.Value;
            }

            await ExecuteStatsQuery(query);

            LastRequest.AssertContainsParameter("by", statsGranularity.GetValueOrDefault(StatsIntervalGranularity.Minute).ToString().ToLower());
        }

        [Fact]
        [Trait("spec", "RSC6a")]
        public async Task WithNoParams_ShouldSendOnlyDefaultQueryParameters()
        {
            var rest = GetRestClient();

            await rest.StatsAsync();

            Equal(HttpMethod.Get, LastRequest.Method);
            Equal("/stats", LastRequest.Url);

            // Only the documented defaults are sent; no start/end or other parameters leak in.
            Equal(
                new[] { "by", "direction", "limit" },
                LastRequest.QueryParameters.Keys.OrderBy(k => k).ToArray());
            LastRequest.AssertContainsParameter("direction", "backwards");
            LastRequest.AssertContainsParameter("limit", "100");
            LastRequest.AssertContainsParameter("by", "minute");
        }

        [Fact]
        [Trait("spec", "RSC6a")]
        public async Task WithEmptyResponseBody_ShouldReturnEmptyPage()
        {
            var rest = GetRestClient(request => new AblyResponse { TextResponse = "[]" }.ToTask());

            var result = await rest.StatsAsync();

            NotNull(result);
            Empty(result.Items);
        }

        [Fact]
        [Trait("spec", "RSC6a")]
        public async Task WhenServerReturnsError_ShouldPropagateAblyExceptionWithParsedErrorInfo()
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":40000,\"statusCode\":400,\"message\":\"Invalid stats query\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            var options = new ClientOptions(ValidKey) { UseBinaryProtocol = false, HttpClient = new HttpClient(new FakeHttpMessageHandler(response)) };
            var rest = new PubSubHttpClient(options);

            var ex = await ThrowsAsync<AblyException>(() => rest.StatsAsync());

            Equal(40000, ex.ErrorInfo.Code);
            Equal(HttpStatusCode.BadRequest, ex.ErrorInfo.StatusCode);
            Contains("Invalid stats query", ex.ErrorInfo.Message);
        }

        [Fact]
        [Trait("spec", "RSC7e")]
        public async Task ShouldPinRequestToProtocolVersion2()
        {
            var rest = GetRestClient();

            await rest.StatsAsync();

            Equal("2", LastRequest.Headers["X-Ably-Version"]);
        }

        [Fact]
        [Trait("spec", "RSC7e")]
        public async Task PinnedVersionShouldOverrideClientDefaultOnTheWire_WhileOtherRequestsKeepDefault()
        {
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath == "/time" ? "[1234]" : "[]", System.Text.Encoding.UTF8, "application/json"),
            });
            var options = new ClientOptions(ValidKey) { UseBinaryProtocol = false, HttpClient = new HttpClient(handler) };
            var rest = new PubSubHttpClient(options);

            await rest.StatsAsync();
            await rest.TimeAsync();

            var statsVersions = handler.Requests.First(r => r.RequestUri.AbsolutePath == "/stats").Headers.GetValues("X-Ably-Version").ToArray();
            Equal(new[] { "2" }, statsVersions);
            var otherVersions = handler.Requests.First(r => r.RequestUri.AbsolutePath == "/time").Headers.GetValues("X-Ably-Version").ToArray();
            Equal(new[] { Defaults.ProtocolVersion }, otherVersions);
        }

        public StatsSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
