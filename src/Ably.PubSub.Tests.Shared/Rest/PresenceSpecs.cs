using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using IO.Ably.Rest;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class PresenceSpecs : MockHttpRestSpecs
    {
        [Fact]
        [Trait("spec", "RSP1")]
        [Trait("spec", "RSP3a")]
        public async Task Presence_CreatesGetRequestWithCorrectPath()
        {
            var rest = GetRestClient();

            var channel = rest.Channels.Get("Test");

            var presence = await channel.Presence.GetAsync();

            presence.Should().BeOfType<PaginatedResult<PresenceMessage>>();

            LastRequest.Method.Should().Be(HttpMethod.Get);
            LastRequest.Url.Should().Be($"/channels/{channel.Name}/presence");
        }

        public class GetSpecs : PresenceSpecs
        {
            private AblyRest _client;
            private readonly IRestChannel _channel;

            [Theory]
            [InlineData(null, "100", false)]
            [InlineData(500, "500", false)]
            [InlineData(-1, "", true)]
            [InlineData(1001, "", true)]
            [Trait("spec", "RSP3a2")]
            public async Task WithLimitParameter_ShouldSetLimitHeaderOrThrowForInvalidValues(int? limit, string expectedLimitHeader, bool throws)
            {
                if (throws)
                {
                    _ = await Assert.ThrowsAsync<ArgumentException>(() => _channel.Presence.GetAsync(limit));
                }
                else
                {
                    _ = await _channel.Presence.GetAsync(limit);

                    LastRequest.AssertContainsParameter("limit", expectedLimitHeader);
                }
            }

            [Fact]
            [Trait("spec", "RSP3a2")]
            [Trait("spec", "RSP3a3")]
            public async Task WithClientIdAndConnectionId_ShouldSetQueryParameters()
            {
                const string clientId = "123";
                const string connectionId = "333";
                await _channel.Presence.GetAsync(clientId: clientId, connectionId: connectionId);

                LastRequest.AssertContainsParameter("clientId", clientId);
                LastRequest.AssertContainsParameter("connectionId", connectionId);
            }

            [Fact]
            [Trait("spec", "RSP4a")]
            public async Task History_WithNoRequestQuery_CreateGetRequestWithValidPath()
            {
                var result = await _channel.Presence.HistoryAsync();

                result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
                LastRequest.Method.Should().Be(HttpMethod.Get);
                LastRequest.Url.Should().Be($"/channels/{_channel.Name}/presence/history");
            }

            [Fact]
            [Trait("spec", "RSP4a")]
            public async Task History_WithRequestQuery_CreateGetRequestWithValidPath()
            {
                var result = await _channel.Presence.HistoryAsync(new PaginatedRequestParams());

                result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
                LastRequest.Method.Should().Be(HttpMethod.Get);
                LastRequest.Url.Should().Be($"/channels/{_channel.Name}/presence/history");
            }

            [Fact]
            [Trait("spec", "RSP4")]
            public async Task History_WithRequestQuery_AddsParametersToRequest()
            {
                var query = new PaginatedRequestParams();
                var now = DateTimeOffset.Now;
                query.Start = now.AddHours(-1);
                query.End = now;
                query.Direction = QueryDirection.Forwards;
                query.Limit = 1000;
                await _channel.Presence.HistoryAsync(query);

                LastRequest.AssertContainsParameter("start", query.Start.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("end", query.End.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("direction", query.Direction.ToString().ToLower());
                LastRequest.AssertContainsParameter("limit", query.Limit.Value.ToString());
            }

            [Fact]
            [Trait("spec", "RSP4b1")]
            public async Task History_WithStartBeforeEnd_Throws()
            {
                await Assert.ThrowsAsync<AblyException>(() =>
                        _channel.Presence.HistoryAsync(new PaginatedRequestParams { Start = Now, End = Now.AddHours(-1) }));
            }

            [Fact]
            [Trait("spec", "RSP4b2")]
            public async Task History_WithoutDirection_ShouldDefaultToBackwards()
            {
                await _channel.Presence.HistoryAsync();

                LastRequest.AssertContainsParameter("direction", QueryDirection.Backwards.ToString().ToLower());
            }

            [Fact]
            [Trait("spec", "RSP4b3")]
            public async Task History_WithOutLimit_ShouldUseDefaultOf100()
            {
                await _channel.Presence.HistoryAsync();

                LastRequest.AssertContainsParameter("limit", "100");
            }

            [Theory]
            [InlineData(-1)]
            [InlineData(1001)]
            [Trait("spec", "RSP4b3")]
            public async Task History_WithLimitLessThan0andMoreThan1000_ShouldThrow(int limit)
            {
                _ = await
                    Assert.ThrowsAsync<AblyException>(() => _channel.Presence.HistoryAsync(new PaginatedRequestParams { Limit = limit }));
            }

            [Fact]
            public async Task History_WithInvalidStartOrEnd_Throws()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                foreach (object[] dates in InvalidHistoryDates)
                {
                    var query = new PaginatedRequestParams { Start = (DateTimeOffset?)dates.First(), End = (DateTimeOffset)dates.Last() };

                    await Assert.ThrowsAsync<AblyException>(async () => await channel.HistoryAsync(query));
                }
            }

            private static IEnumerable<object[]> InvalidHistoryDates
            {
                get
                {
                    yield return new object[] { new DateTimeOffset(1969, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Now };
                    yield return new object[] { new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero) };
                    yield return new object[] { null, new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero) };
                }
            }

            [Fact]
            public async Task History_WithPartialResult_ReturnsCorrectFirstCurrentAndNextLinks()
            {
                // Arrange
                var rest = GetRestClient(request => new AblyResponse
                {
                    Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                    TextResponse = "[]"
                }.ToTask());

                var channel = rest.Channels.Get("test");

                // Act
                var result = await channel.HistoryAsync();

                // Assert
                result.NextQueryParams.Should().NotBeNull();
                result.CurrentQueryParams.Should().NotBeNull();
                result.FirstQueryParams.Should().NotBeNull();
            }

            public GetSpecs(ITestOutputHelper output)
                : base(output)
            {
                _client = GetRestClient();
                _channel = _client.Channels.Get("test");
            }
        }

        public PresenceSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
