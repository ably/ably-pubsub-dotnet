using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Rest
{
    [Collection("AblyRest SandBox Collection")]
    [Trait("type", "integration")]
    public class JwtSandboxSpec : SandboxSpecs
    {
        private const string EchoServer = "https://echo.ably.io/createJWT";

        private readonly HttpClient _httpClient = new HttpClient();

        public JwtSandboxSpec(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact]
        public async Task CanGetJwtTokenStringFromEchoServer()
        {
            var token = await GetJwtStringAsync();
            token.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8g")]
        [Trait("spec", "RSA8c")]
        public async Task WithJwtToken_CanMakeRequest(Protocol protocol)
        {
            var jwt = await GetJwtTokenAsync();
            var client = await GetRestClient(protocol, options =>
            {
                options.TokenDetails = jwt;
            });

            var stats = await client.StatsAsync();
            stats.Should().NotBeNull();

            // show that the token was not renewed
            client.AblyAuth.CurrentToken.Should().BeEquivalentTo(jwt);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSC1")]
        [Trait("spec", "RSC1c")]
        [Trait("spec", "RSA3d")]
        public async Task Jwt_WithEmbeddedJwtToken_CanMakeRequest(Protocol protocol)
        {
            var jwt = await GetJwtTokenAsync(jwtType: "embedded");
            var client = await GetRestClient(protocol, options =>
            {
                options.TokenDetails = jwt;
            });

            var stats = await client.StatsAsync();
            stats.Should().NotBeNull();

            // show that the token was not renewed
            client.AblyAuth.CurrentToken.Should().BeEquivalentTo(jwt);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSC1")]
        [Trait("spec", "RSC1c")]
        [Trait("spec", "RSA3d")]
        public async Task Jwt_WithEmbeddedAndEncryptedJwtToken_CanMakeRequest(Protocol protocol)
        {
            var jwt = await GetJwtTokenAsync(jwtType: "embedded", encrypted: 1);
            var client = await GetRestClient(protocol, options =>
            {
                options.TokenDetails = jwt;
            });

            var stats = await client.StatsAsync();
            stats.Should().NotBeNull();

            // show that the token was not renewed
            client.AblyAuth.CurrentToken.Should().BeEquivalentTo(jwt);
        }

        [Theory]
        [ProtocolData]
        public async Task Jwt_WithInvalidJwtToken_CanNotMakeRequest(Protocol protocol)
        {
            var jwt = await GetJwtTokenAsync(invalid: true);
            var client = await GetRestClient(protocol, options =>
            {
                options.TokenDetails = jwt;
                options.Key = string.Empty; // prevents auto-renew
            });

            var didError = false;
            try
            {
                _ = await client.StatsAsync();
                throw new Exception("This should not be reached");
            }
            catch (AblyException e)
            {
                didError = true;
                e.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "StatusCode should be 401"); // 401
            }

            didError.Should().BeTrue();
            client.AblyAuth.CurrentToken.Should().BeEquivalentTo(jwt);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4f")]
        [Trait("spec", "RSA8c")]
        public async Task Jwt_Request_ReturnType(Protocol protocol)
        {
            var defaultOptions = (await AblySandboxFixture.GetSettings()).CreateDefaultOptions();
            var keyParts = defaultOptions.Key.Split(':');
            var key = keyParts[0];
            var secret = keyParts[1];

            var authParams = new Dictionary<string, string>
            {
                ["environment"] = "sandbox",
                ["keyName"] = key,
                ["keySecret"] = secret,
                ["returnType"] = "jwt"
            };

            var client = await GetRestClient(protocol, options =>
            {
                options.AuthUrl = new Uri(EchoServer);
                options.AuthParams = authParams;
                options.Key = string.Empty;
            });

            var stats = await client.StatsAsync();
            stats.Should().NotBeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8g")]
        public async Task Jwt_Request_AuthCallback(Protocol protocol)
        {
            var jwt = await GetJwtTokenAsync();
            var client = await GetRestClient(protocol, options =>
                {
                    options.AuthCallback = _ => Task.FromResult<object>(jwt);
                });

            var stats = await client.StatsAsync();
            stats.Should().NotBeNull();

            client.AblyAuth.CurrentToken.Should().BeEquivalentTo(jwt);
        }

        private async Task<TokenDetails> GetJwtTokenAsync(
            bool invalid = false,
            int expiresIn = 3600,
            string clientId = "testClientIdDotNet",
            string capability = "{\"*\":[\"*\"]}",
            string jwtType = "",
            int encrypted = 0)
        {
            var jwtStr = await GetJwtStringAsync(invalid, expiresIn, clientId, capability, jwtType, encrypted);
            var token = new TokenDetails(jwtStr) { Expires = DateTimeOffset.UtcNow.AddSeconds(expiresIn) };
            return token;
        }

        private async Task<string> GetJwtStringAsync(
            bool invalid = false,
            int expiresIn = 3600,
            string clientId = "testClientIdDotNet",
            string capability = "{\"*\":[\"*\"]}",
            string jwtType = "",
            int encrypted = 0)
        {
            var settings = await AblySandboxFixture.GetSettings();
            var defaultOptions = settings.CreateDefaultOptions();
            var keyParts = defaultOptions.Key.Split(':');
            if (keyParts.Length != 2)
            {
                throw new Exception($"Cannot create JWT Token. API Key '{defaultOptions.Key}' is not valid.");
            }

            var key = keyParts[0];
            var secret = keyParts[1];

            if (invalid)
            {
                secret = "invalid";
            }

            var builder = new UriBuilder(EchoServer);

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["keyName"] = key;
            query["keySecret"] = secret;
            query["expiresIn"] = expiresIn.ToString();
            query["clientId"] = clientId;
            query["capability"] = capability;
            query["jwtType"] = jwtType;
            query["encrypted"] = encrypted.ToString();
            query["environment"] = "sandbox";
            builder.Query = query.ToQueryString();

            var jwtStr = await _httpClient.GetStringAsync(builder.Uri);
            return jwtStr;
        }

        protected override void Dispose(bool disposing)
        {
            _httpClient.Dispose();
            base.Dispose(disposing);
        }
    }
}
