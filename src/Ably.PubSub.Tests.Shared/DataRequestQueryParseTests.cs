using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class DataRequestQueryParseTests
    {
        private const string HeaderValue = "?start=1380794880000&end=1380794881058&limit=100&by=minute&direction=forwards&format=json&first_start=1380794880000";

        private readonly PaginatedRequestParams _query;

        public DataRequestQueryParseTests()
        {
            _query = PaginatedRequestParams.Parse(HeaderValue);
        }

        [Fact]
        public void Parse_WithQueryString_SetsCorrectStartAndEndDates()
        {
            // Arrange
            var startDate = long.Parse("1380794880000").FromUnixTimeInMilliseconds();
            var endDate = long.Parse("1380794881058").FromUnixTimeInMilliseconds();

            // Assert
            _query.Start.Should().Be(startDate);
            _query.End.Should().Be(endDate);
        }

        [Fact]
        public void Parse_WithQueryString_SetsCorrectLimit()
        {
            // Assert
            _query.Limit.Should().Be(100);
        }

        [Fact]
        public void Parse_SetsCorrectDirection()
        {
            // Assert
            _query.Direction.Should().Be(QueryDirection.Forwards);
        }

        [Fact]
        public void Parse_HasTwoExtraParameters()
        {
            // Assert
            _query.ExtraParameters.Count.Should().Be(3);
            _query.ExtraParameters.ContainsKey("by").Should().BeTrue();
            _query.ExtraParameters.ContainsKey("first_start").Should().BeTrue();
            _query.ExtraParameters.ContainsKey("format").Should().BeTrue();
        }
    }
}
