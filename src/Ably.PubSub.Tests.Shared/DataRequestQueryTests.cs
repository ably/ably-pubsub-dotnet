using System.Net.Http.Headers;
using Xunit;

namespace IO.Ably.Tests
{
    public class DataRequestQueryTests
    {
        private const string FirstQueryString = "?start=1380794880000&end=1380794881058&limit=100&by=minute&direction=forwards&format=json&first_start=1380794880000";
        private const string CurrentQueryString = "?start=1380794880000&end=1380794881058&limit=100&by=minute&direction=forwards&format=json&first_start=1380794880000";
        private const string NextQueryString = "?start=1380794881111&end=1380794881058&limit=100&by=minute&direction=forwards&format=json&first_start=1380794880000";

        public static HttpHeaders GetSampleHistoryRequestHeaders()
        {
            var headers = new TestHttpHeaders
            {
                { "Link", $"<./history{FirstQueryString}>; rel=\"first\"" },
                { "Link", $"<./history{NextQueryString}>; rel=\"next\"" },
                { "Link", $"<./history{CurrentQueryString}>; rel=\"current\"" },
            };

            return headers;
        }

        public static HttpHeaders GetSampleStatsRequestHeaders()
        {
            var headers = new TestHttpHeaders
            {
                { "Link", $"<./stats{FirstQueryString}>; rel=\"first\"" },
                { "Link", $"<./stats{NextQueryString}>; rel=\"next\"" },
            };

            return headers;
        }

        [Fact]
        public void GetLinkQuery_WithHeadersAndAskingForNextLink_ReturnsCorrectRequestQuery()
        {
            // Arrange
            var nextDataRequest = PaginatedRequestParams.Parse(NextQueryString);

            // Act
            var actual = PaginatedRequestParams.GetLinkQuery(GetSampleHistoryRequestHeaders(), "next");

            // Assert
            Assert.Equal(nextDataRequest, actual);
        }

        [Fact]
        public void GetLinkQuery_WithHeadersAndAskingForFirstLink_ReturnsCorrectRequestQuery()
        {
            // Arrange
            var firstDataRequest =
                PaginatedRequestParams.Parse(
                    FirstQueryString);

            // Act
            var actual = PaginatedRequestParams.GetLinkQuery(GetSampleHistoryRequestHeaders(), "first");

            // Assert
            Assert.Equal(firstDataRequest, actual);
        }

        private class TestHttpHeaders : HttpHeaders
        {
        }
    }
}
