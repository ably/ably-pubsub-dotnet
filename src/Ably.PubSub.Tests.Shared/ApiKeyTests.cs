using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class ApiKeyTests
    {
        [Theory]
        [InlineData("InvalidKey")]
        [InlineData("")]
        [InlineData(null)]
        public void Parse_WithKeyNotContaining3PartsSeparatedByColon_ThrowsInvalidKeyException(string key)
        {
            Assert.Throws<AblyException>(delegate { ApiKey.Parse(key); });
        }

        [Fact]
        public void Parse_WithValidKeyReturns_ApiKeyWithAppIdKeyAndValue()
        {
            var key = ApiKey.Parse("123.456:789");

            key.AppId.Should().Be("123");
            key.KeyName.Should().Be("123.456");
            key.KeySecret.Should().Be("789");
        }

        [Fact]
        public void Parse_WithValidKeyWithWhiteSpaceOnBothSides_ReturnsValidApiKeyObject()
        {
            var key = ApiKey.Parse(" 123.456:789 ");

            key.AppId.Should().Be("123");
            key.KeyName.Should().Be("123.456");
            key.KeySecret.Should().Be("789");
        }
    }
}
