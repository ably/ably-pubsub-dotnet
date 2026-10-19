using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Shared
{
    public class DefaultTests
    {
        [Fact]
        [Trait("spec", "REC1a")]
        [Trait("spec", "REC2c1")]
        public void DefaultEndpoint()
        {
            Defaults.GetPrimaryDomain(null).Should().Be("main.realtime.ably.net");
            var expectedFallbackHosts = new[]
            {
                "main.a.fallback.ably-realtime.com",
                "main.b.fallback.ably-realtime.com",
                "main.c.fallback.ably-realtime.com",
                "main.d.fallback.ably-realtime.com",
                "main.e.fallback.ably-realtime.com",
            };
            Assert.Equal(expectedFallbackHosts, Defaults.GetEndpointFallbackHosts(null));
            Assert.Equal(Defaults.GetEndpointFallbackHosts(null), Defaults.FallbackHosts);
        }

        [Fact]
        [Trait("spec", "REC1b4")]
        [Trait("spec", "REC2c4")]
        public void RoutingPolicyEndpoint()
        {
            Defaults.GetPrimaryDomain("acme").Should().Be("acme.realtime.ably.net");
            var expectedFallbackHosts = new[]
            {
                "acme.a.fallback.ably-realtime.com",
                "acme.b.fallback.ably-realtime.com",
                "acme.c.fallback.ably-realtime.com",
                "acme.d.fallback.ably-realtime.com",
                "acme.e.fallback.ably-realtime.com",
            };
            Assert.Equal(expectedFallbackHosts, Defaults.GetEndpointFallbackHosts("acme"));
        }

        [Fact]
        [Trait("spec", "REC1b3")]
        [Trait("spec", "REC2c3")]
        public void NonprodRoutingPolicyEndpoint()
        {
            Defaults.GetPrimaryDomain("nonprod:sandbox").Should().Be("sandbox.realtime.ably-nonprod.net");
            var expectedFallbackHosts = new[]
            {
                "sandbox.a.fallback.ably-realtime-nonprod.com",
                "sandbox.b.fallback.ably-realtime-nonprod.com",
                "sandbox.c.fallback.ably-realtime-nonprod.com",
                "sandbox.d.fallback.ably-realtime-nonprod.com",
                "sandbox.e.fallback.ably-realtime-nonprod.com",
            };
            Assert.Equal(expectedFallbackHosts, Defaults.GetEndpointFallbackHosts("nonprod:sandbox"));
        }

        [Theory]
        [Trait("spec", "REC1b2")]
        [Trait("spec", "REC2c2")]
        [InlineData("foo.example.com")]
        [InlineData("localhost")]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        public void HostnameEndpoint(string hostname)
        {
            Defaults.GetPrimaryDomain(hostname).Should().Be(hostname);
            Defaults.GetEndpointFallbackHosts(hostname).Should().BeEmpty();
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
