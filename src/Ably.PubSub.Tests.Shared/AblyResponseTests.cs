using System;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class AblyResponseTests
    {
        [Theory]
        [InlineData("application/json", ResponseType.Json)]
        [InlineData("application/x-msgpack", ResponseType.Binary)]
        [InlineData("", ResponseType.Binary)]
        public void Ctor_WithContentType_SetsTypeCorrectly(string type, object responseType)
        {
            // Arrange

            // Act
            var response = new AblyResponse(string.Empty, type, Array.Empty<byte>());

            // Assert
            ((ResponseType)responseType).Should().Be(response.Type);
        }

        [Theory]
        [InlineData("utf-7", "utf-7")]
        [InlineData("", "utf-8")]
        public void Ctor_WithEncoding_SetsEncodingCorrectly(string encoding, string expected)
        {
            // Arrange

            // Act
            var response = new AblyResponse(encoding, string.Empty, Array.Empty<byte>());

            // Assert
            expected.Should().Be(response.Encoding);
        }

        [Fact]
        public void Ctor_WhenTypeIsJson_SetsTextResponse()
        {
            // Arrange
            const string text = "Test";

            // Act
            var response = new AblyResponse(string.Empty, "application/json", text.GetBytes());

            // Assert
            text.Should().Be(response.TextResponse);
        }
    }
}
