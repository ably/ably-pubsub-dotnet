using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Shared
{
    public class ClientOptionsTests
    {
        [Fact]
        [Trait("spec", "RSC15e")]
        [Trait("spec", "RSC15g3")]
        public void DefaultOptions()
        {
            var options = new ClientOptions();
            options.FullRealtimeHost().Should().Be("realtime.ably.io");
            options.FullRestHost().Should().Be("rest.ably.io");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Equal(Defaults.FallbackHosts, options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RSC15h")]
        public void Options_WithProductionEnvironment()
        {
            var options = new ClientOptions
            {
                Environment = "production"
            };
            options.FullRealtimeHost().Should().Be("realtime.ably.io");
            options.FullRestHost().Should().Be("rest.ably.io");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Equal(Defaults.FallbackHosts, options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RSC15g2")]
        [Trait("spec", "RTC1e")]
        public void Options_WithCustomEnvironment()
        {
            var options = new ClientOptions
            {
                Environment = "sandbox"
            };
            options.FullRealtimeHost().Should().Be("sandbox-realtime.ably.io");
            options.FullRestHost().Should().Be("sandbox-rest.ably.io");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Equal(Defaults.GetEnvironmentFallbackHosts("sandbox"), options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RSC11b")]
        [Trait("spec", "RTN17b")]
        [Trait("spec", "RTC1e")]
        public void Options_WithCustomEnvironment_And_NonDefaultPorts()
        {
            var options = new ClientOptions
            {
                Environment = "local",
                Port = 8080,
                TlsPort = 8081
            };

            options.FullRealtimeHost().Should().Be("local-realtime.ably.io");
            options.FullRestHost().Should().Be("local-rest.ably.io");
            options.Port.Should().Be(8080);
            options.TlsPort.Should().Be(8081);
            Assert.Empty(options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RSC11")]
        public void Options_WithCustomRestHost()
        {
            var options = new ClientOptions
            {
                RestHost = "test.org"
            };

            options.FullRestHost().Should().Be("test.org");
            options.FullRealtimeHost().Should().Be("test.org");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Empty(options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "RSC11")]
        public void Options_WithCustomRestHost_And_RealtimeHost()
        {
            var options = new ClientOptions
            {
                RestHost = "test.org",
                RealtimeHost = "ws.test.org"
            };

            options.FullRestHost().Should().Be("test.org");
            options.FullRealtimeHost().Should().Be("ws.test.org");
            options.Port.Should().Be(80);
            options.TlsPort.Should().Be(443);
            Assert.Empty(options.GetFallbackHosts());
            options.Tls.Should().BeTrue();
        }
    }
}
