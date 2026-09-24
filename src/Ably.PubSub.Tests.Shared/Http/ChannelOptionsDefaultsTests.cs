using System;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.NETFramework.Rest
{
    public class ChannelOptionsDefaultsTests
    {
        [Fact]
        [Trait("spec", "TO3l6")]
        [Trait("spec", "TO3l4")]
        [Trait("spec", "TO3l3")]
        public void ShouldHaveCorrectHttpDefaults()
        {
            var options = new ClientOptions();

            options.HttpMaxRetryDuration.Should().Be(TimeSpan.FromSeconds(15)); // TO3l6

            options.HttpMaxRetryCount.Should().Be(3); // (TO3l5)
            options.HttpRequestTimeout.Should().Be(TimeSpan.FromSeconds(10)); // (TO3l4)
            options.HttpOpenTimeout.Should().Be(TimeSpan.FromSeconds(4)); // (TO3l3)
        }
    }
}
