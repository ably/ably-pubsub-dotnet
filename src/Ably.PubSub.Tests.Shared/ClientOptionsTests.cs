using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Shared
{
    public class ClientOptionsTests
    {
        [Fact]
        [Trait("spec", "REC1a")]
        [Trait("spec", "REC2c1")]
        public void DefaultOptions()
        {
            var options = new ClientOptions();
            options.Endpoint.Should().BeNull();
            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("main.realtime.ably.net");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Equal(Defaults.FallbackHosts, options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        [Trait("spec", "RSC15m")]
        public void Options_WithEmptyFallbackHosts_DisablesFallback()
        {
            var options = new ClientOptions
            {
                FallbackHosts = new string[0]
            };

            Assert.Empty(options.GetFallbackHosts());
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        public void FallbackHosts_WhenUnset_ReadsNull()
        {
            var options = new ClientOptions { Endpoint = "acme" };

            options.FallbackHosts.Should().BeNull();
            options.GetFallbackHosts().Should().Equal(Defaults.GetEndpointFallbackHosts("acme"));
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        public void FallbackHosts_WhenSet_ReadsTheHostsThatWereSet()
        {
            var hosts = new[] { "custom-fallback1.ably.com", "custom-fallback2.ably.com" };
            var options = new ClientOptions { FallbackHosts = hosts };

            options.FallbackHosts.Should().BeSameAs(hosts);
            options.GetFallbackHosts().Should().Equal(hosts).And.NotBeSameAs(hosts);
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        public void FallbackHosts_WhenSetToEmpty_ReadsEmpty()
        {
            var options = new ClientOptions { FallbackHosts = new string[0] };

            options.FallbackHosts.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        public void FallbackHosts_WhenResetToNull_RestoresTheDefaults()
        {
            var options = new ClientOptions { FallbackHosts = new[] { "custom-fallback.ably.com" } };

            options.FallbackHosts = null;

            options.FallbackHosts.Should().BeNull();
            options.GetFallbackHosts().Should().Equal(Defaults.FallbackHosts);
        }

        [Fact]
        [Trait("spec", "TO3k6")]
        public void Clone_CarriesFallbackHosts()
        {
            var options = new ClientOptions { FallbackHosts = new[] { "custom-fallback.ably.com" } };

            options.Clone().FallbackHosts.Should().Equal("custom-fallback.ably.com");
            new ClientOptions().Clone().FallbackHosts.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "REC2a2")]
        public void Options_WithCustomFallbackHosts_UsesThem()
        {
            var options = new ClientOptions
            {
                FallbackHosts = new[] { "custom-fallback1.ably.com", "custom-fallback2.ably.com" }
            };

            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("main.realtime.ably.net");
            Assert.Equal(new[] { "custom-fallback1.ably.com", "custom-fallback2.ably.com" }, options.GetFallbackHosts());
        }

        [Fact]
        [Trait("spec", "REC1b4")]
        [Trait("spec", "REC2c4")]
        public void Options_WithRoutingPolicyEndpoint()
        {
            var options = new ClientOptions
            {
                Endpoint = "acme"
            };

            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("acme.realtime.ably.net");
            Assert.Equal(Defaults.GetEndpointFallbackHosts("acme"), options.GetFallbackHosts());
        }

        [Fact]
        [Trait("spec", "REC1b3")]
        [Trait("spec", "REC2c3")]
        public void Options_WithNonprodEndpoint()
        {
            var options = new ClientOptions
            {
                Endpoint = "nonprod:sandbox"
            };

            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("sandbox.realtime.ably-nonprod.net");
            options.GetFallbackHosts().Should().BeEquivalentTo(
                "sandbox.a.fallback.ably-realtime-nonprod.com",
                "sandbox.b.fallback.ably-realtime-nonprod.com",
                "sandbox.c.fallback.ably-realtime-nonprod.com",
                "sandbox.d.fallback.ably-realtime-nonprod.com",
                "sandbox.e.fallback.ably-realtime-nonprod.com");
        }

        [Fact]
        [Trait("spec", "REC1b2")]
        [Trait("spec", "REC2c2")]
        public void Options_WithHostnameEndpoint_HasNoFallbacks()
        {
            var options = new ClientOptions
            {
                Endpoint = "overridden.ably.io"
            };

            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("overridden.ably.io");
            Assert.Empty(options.GetFallbackHosts());
        }

        [Fact]
        [Trait("spec", "REC2a2")]
        public void Options_WithHostnameEndpointAndFallbackHosts_UsesCustomFallbacks()
        {
            var options = new ClientOptions
            {
                Endpoint = "custom.ably.com",
                FallbackHosts = new[] { "custom-fallback.ably.com" }
            };

            Defaults.GetPrimaryDomain(options.Endpoint).Should().Be("custom.ably.com");
            Assert.Equal(new[] { "custom-fallback.ably.com" }, options.GetFallbackHosts());
        }

        [Fact]
        [Trait("spec", "REC2c1")]
        public void Options_WithNonDefaultPorts_StillUseDefaultFallbacks()
        {
            // The pre-endpoint rule only checked the port in use, and Tls defaults to true, so
            // TlsPort must be set for this to have failed under that rule.
            var options = new ClientOptions
            {
                Port = 8080,
                TlsPort = 8081
            };

            Assert.Equal(Defaults.FallbackHosts, options.GetFallbackHosts());
        }
    }
}
