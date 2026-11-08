using System.Net.Http;
using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests
{
    public class HttpUtilityTests
    {
        [Theory]
        [InlineData("::1", "[::1]")]
        [InlineData("2001:db8::1", "[2001:db8::1]")]
        public void HostForUrl_Ipv6Literal_IsBracketed(string host, string expected)
        {
            HttpUtility.HostForUrl(host).Should().Be(expected);
        }

        [Theory]
        [InlineData("[::1]")]
        [InlineData("127.0.0.1")]
        [InlineData("localhost")]
        [InlineData("main.realtime.ably.net")]
        public void HostForUrl_OtherHosts_AreUnchanged(string host)
        {
            HttpUtility.HostForUrl(host).Should().Be(host);
        }

        [Fact]
        [Trait("spec", "REC1b2")]
        public void GetRequestUrl_Ipv6Host_AddressesHostAndPort()
        {
            var client = new AblyHttpRequester(new AblyHttpOptions { Host = "::1", IsSecure = false, Port = 8080 });

            var url = client.GetRequestUrl(new AblyRequest("/time", HttpMethod.Get));

            url.ToString().Should().Be("http://[::1]:8080/time");
            url.Host.Should().Be("[::1]");
            url.Port.Should().Be(8080);
        }
    }
}
