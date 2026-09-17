using System;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Shared
{
    public class AblyInsecureRequestExceptionTests
    {
        [Fact]
        public void Construct_WithMessage()
        {
            var e = new AblyInsecureRequestException("Something bad happened.");
            e.Message.Should().Contain("Something bad happened.");
        }

        [Fact]
        public void Construct_WithMessageAndInnerException()
        {
            var inner = new Exception("Troll in the dungeon!");
            var e = new AblyInsecureRequestException("Something bad happened.", inner);
            e.Message.Should().Contain("Something bad happened.");
            e.InnerException.Should().Be(inner);
        }
    }
}
