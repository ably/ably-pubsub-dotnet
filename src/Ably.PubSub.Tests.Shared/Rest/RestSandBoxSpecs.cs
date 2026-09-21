using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    [Collection("AblyRest SandBox Collection")]
    [Trait("type", "integration")]
    public class RestSandBoxSpecs : SandboxSpecs
    {
        public RestSandBoxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output) { }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSC6a")]
        public async Task GettingStats_ShouldReturnValidPaginatedResultOfStats(Protocol protocol)
        {
            var client = await GetRestClient(protocol);

            var stats = await client.StatsAsync(new StatsRequestParams());

            stats.Should().NotBeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSC16")]
        public async Task Time_ShouldReturnAValidDateTimeOffset(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var serverTime = await client.TimeAsync();

            // server time should be similar to the system time
            // here we allow the system clock to be 15 minutes fast or slow
            serverTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));

            // server time is UTC so there should be no time zone offset
            serverTime.Offset.Ticks.Should().Be(0);
        }

        [Collection("AblyRest SandBox Collection")]
        [Trait("requires", "sandbox")]
        public class WithTokenAuthAndInvalidToken : RestSandBoxSpecs
        {
            public WithTokenAuthAndInvalidToken(AblySandboxFixture fixture, ITestOutputHelper output)
                : base(fixture, output) { }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RSC10")]
            public async Task WhenTokenIsRenewable_ShouldRenewToken(Protocol protocol)
            {
                var authClient = await GetRestClient(protocol);
                var almostExpiredToken = await authClient.Auth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromSeconds(1) });
                await Task.Delay(TimeSpan.FromSeconds(2));

                // Add this to fool the client it is a valid token
                almostExpiredToken.Expires = DateTimeOffset.UtcNow.AddHours(1);

                // Trying again with the new token
                var client = await GetRestClient(protocol, options =>
                {
                    options.TokenDetails = almostExpiredToken;
                    options.ClientId = "123";
                    options.Key = string.Empty;
                    options.AuthCallback = request => authClient.AblyAuth.RequestTokenAsync(request).Convert();
                });

                await client.StatsAsync();
                var now = DateTimeOffset.UtcNow;
                client.AblyAuth.CurrentToken.IsValidToken(now).Should().BeTrue();
            }
        }
    }
}
