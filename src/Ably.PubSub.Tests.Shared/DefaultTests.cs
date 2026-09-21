using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Shared
{
    public class DefaultTests
    {
        [Fact]
        [Trait("spec", "RSC15h")]
        public void Defaults_ReturnsFallbackHosts()
        {
            var expectedFallBackHosts = new[]
            {
                "a.ably-realtime.com",
                "b.ably-realtime.com",
                "c.ably-realtime.com",
                "d.ably-realtime.com",
                "e.ably-realtime.com",
            };
            var fallbackHosts = Defaults.FallbackHosts;
            Assert.Equal(expectedFallBackHosts, fallbackHosts);
        }

        [Fact]
        [Trait("spec", "RSC15i")]
        public void Defaults_WithEnvironment_ReturnsEnvironmentFallbackHosts()
        {
            var expectedFallBackHosts = new[]
            {
                "sandbox-a-fallback.ably-realtime.com",
                "sandbox-b-fallback.ably-realtime.com",
                "sandbox-c-fallback.ably-realtime.com",
                "sandbox-d-fallback.ably-realtime.com",
                "sandbox-e-fallback.ably-realtime.com",
            };
            var fallbackHosts = Defaults.GetEnvironmentFallbackHosts("sandbox");
            Assert.Equal(expectedFallBackHosts, fallbackHosts);
        }

        [Fact]
        public void Defaults_ProtocolIsJson()
        {
            Defaults.Protocol.Should().Be(Protocol.Json);
        }
    }
}
