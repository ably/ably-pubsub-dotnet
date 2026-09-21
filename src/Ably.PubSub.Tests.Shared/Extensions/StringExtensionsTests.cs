using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Extensions
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData(" both-sides ", "both-sides")]
        [InlineData(" one-side", "one-side")]
        [InlineData("one-side ", "one-side")]
        [InlineData(" tab    ", "tab")]
        [InlineData(" newline\r\n", "newline")]
        [InlineData(" newline\n", "newline")]
        [InlineData(" newline\r", "newline")]
        public void SafeTrim_ShouldCorrectlyTrimValue(string input, string expected)
        {
            input.SafeTrim().Should().Be(expected);
        }
    }
}
