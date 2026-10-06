using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Shared
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
        [Trait("spec", "CSV2")]
        public void Defaults_DeclaresWireProtocolVersion()
        {
            // Documents the declared wire protocol version (CSV2c/CSV2d). The constant reaches the
            // wire in exactly two places, both of which reference it symbolically:
            // - TransportParams.cs: the websocket "v" query parameter;
            // - AblyHttpRequester.cs: the "X-Ably-Version" HTTP request header.
            // A deliberate protocol-version bump is made by changing Defaults.ProtocolVersion; this
            // test intentionally does not pin the literal value, only its shape.
            int.TryParse(Defaults.ProtocolVersion, out var declaredVersion)
                .Should().BeTrue("the declared protocol version must be an integer string");
            declaredVersion.Should().BePositive();
        }

        [Fact]
        public void Defaults_ProtocolIsJson()
        {
            Defaults.Protocol.Should().Be(Protocol.Json);
        }

        [Theory]
        [InlineData("2.0.0-beta.1+abc123", "2.0.0-beta.1")] // strip SourceLink metadata, keep prerelease label
        [InlineData("2.0.0+abc123", "2.0.0")] // strip metadata off a GA version
        [InlineData("2.0.0-rc.1", "2.0.0-rc.1")] // no metadata: unchanged
        [InlineData("2.0.0", "2.0.0")] // plain GA: unchanged
        public void NormalizeInformationalVersion_StripsBuildMetadataAndKeepsPrereleaseLabel(string input, string expected)
        {
            Defaults.NormalizeInformationalVersion(input).Should().Be(expected);
        }

        [Fact]
        public void GetVersion_DoesNotCarrySourceLinkBuildMetadata()
        {
            // The wire agent must never contain the '+<commit>' SourceLink suffix.
            Defaults.GetVersion().Should().NotContain("+");
        }
    }
}
