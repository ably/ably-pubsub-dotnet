using IO.Ably.Push;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Push
{
    public class RegistrationTokenTest
    {
        private const string TokenType = "Foo";
        private const string TokenValue = "Bar";

        [Fact]
        public void Constructor_PropertiesExposeConstructionValues()
        {
            var regToken = new RegistrationToken(TokenType, TokenValue);
            regToken.Type.Should().Be(TokenType);
            regToken.Token.Should().Be(TokenValue);
        }

        [Fact]
        public void ToString_Serialization()
        {
            var regToken = new RegistrationToken(TokenType, TokenValue);
            regToken.ToString().Should().Be($"RegistrationToken: Type = {TokenType}, Token = {TokenValue}");
        }
    }
}
