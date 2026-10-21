// Auth Integration Tests (RSA4, RSA8, RSC10)
// Derived from uts/rest/integration/auth.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); the spec's "Token Formats" header (run with both JWTs and native tokens) is covered by the per-format tests the spec lists, not by a theory

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class AuthTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public AuthTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSA4/basic-auth-key-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA4")]
        public async Task RSA4_BasicAuthKey()
        {
            // Setup
            var channelName = "test-RSA4-" + RandomId();
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Test Steps
            // Use channel status endpoint (requires authentication)
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            // Just verify the request succeeded - don't check response body
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);
        }

        // UTS: rest/integration/RSA8/token-auth-jwt-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_TokenAuthJwt()
        {
            // Setup
            // Generate a valid JWT using a third-party library
            var jwt = GenerateJwt(
                ExtractKeyName(ApiKey),
                ExtractKeySecret(ApiKey),
                ttl: TimeSpan.FromMilliseconds(3600000));

            var channelName = "test-RSA8-jwt-" + RandomId();
            var client = Rest(new ClientOptions { Token = jwt });

            // Test Steps
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);
        }

        // UTS: rest/integration/RSA8/token-auth-native-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_TokenAuthNative()
        {
            // Setup
            // First client with API key to obtain token
            var keyClient = Rest(new ClientOptions { Key = ApiKey });

            // Test Steps
            // Obtain a native token
            var tokenDetails = await keyClient.Auth.RequestTokenAsync();

            // Create new client using only the token
            var channelName = "test-RSA8-native-" + RandomId();
            var tokenClient = Rest(new ClientOptions { Token = tokenDetails.Token });

            // Verify token works
            var result = await tokenClient.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            // ASSERT token_details.token IS String: guaranteed at compile time (TokenDetails.Token is a string)
            tokenDetails.Token.Should().NotBeNull();
            tokenDetails.Token.Length.Should().BeGreaterThan(0);
            tokenDetails.Expires.Should().BeAfter(DateTimeOffset.UtcNow);
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);
        }

        // UTS: rest/integration/RSA8/auth-callback-token-request-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_AuthCallbackTokenRequest()
        {
            // Setup
            // Client that generates token requests
            var tokenRequestClient = Rest(new ClientOptions { Key = ApiKey });

            // authCallback that creates and returns a TokenRequest
            // NOTE: CreateTokenRequestAsync returns the signed request as a JSON string; parsed into a TokenRequest (rest-mapping.md section 6)
            Func<TokenParams, Task<object>> authCallback = async tokenParams =>
            {
                var tokenRequestJson = await tokenRequestClient.Auth.CreateTokenRequestAsync(tokenParams);
                return JsonHelper.Deserialize<TokenRequest>(tokenRequestJson);
            };

            var channelName = "test-RSA8-callback-" + RandomId();
            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);
        }

        // UTS: rest/integration/RSA8/auth-callback-jwt-3
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_AuthCallbackJwt()
        {
            // Setup
            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            Func<TokenParams, Task<object>> authCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(
                ExtractKeyName(ApiKey),
                ExtractKeySecret(ApiKey),
                clientId: tokenParams?.ClientId,
                ttl: tokenParams?.Ttl ?? TimeSpan.FromMilliseconds(3600000))));

            var channelName = "test-RSA8-jwt-callback-" + RandomId();
            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);
        }

        // UTS: rest/integration/RSA4/invalid-credentials-rejected-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA4")]
        public async Task RSA4_InvalidCredentialsRejected()
        {
            // Setup
            var channelName = "test-RSA4-invalid-" + RandomId();

            // Use the real app_id with a fabricated key name. The server returns HTTP 401
            // with Ably error code 40400 (key not found).
            var invalidKey = App.AppId + ".invalidKey:invalidSecret";

            var client = Rest(new ClientOptions { Key = invalidKey });

            // Test Steps
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);
            ((int)result.StatusCode).Should().Be(401);
            result.ErrorCode.Should().Be(40400);
        }

        // UTS: rest/integration/RSC10/token-renewal-expired-jwt-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSC10", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC10")]
        public async Task RSC10_TokenRenewalExpiredJwt()
        {
            // Setup
            // Track how many times the callback is invoked
            var callbackCount = 0;

            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            Func<TokenParams, Task<object>> authCallback = tokenParams =>
            {
                if (Interlocked.Increment(ref callbackCount) == 1)
                {
                    // First call: return an already-expired JWT (expired 5 seconds ago)
                    return Task.FromResult<object>(new TokenDetails(GenerateJwt(
                        ExtractKeyName(ApiKey),
                        ExtractKeySecret(ApiKey),
                        expiresAt: DateTimeOffset.UtcNow.AddSeconds(-5))));
                }

                // Subsequent calls: return a valid JWT
                return Task.FromResult<object>(new TokenDetails(GenerateJwt(
                    ExtractKeyName(ApiKey),
                    ExtractKeySecret(ApiKey),
                    ttl: TimeSpan.FromMilliseconds(3600000))));
            };

            var channelName = "test-RSC10-renewal-" + RandomId();
            var client = Rest(new ClientOptions { AuthCallback = authCallback });

            // Test Steps
            // Make a REST request — first token is expired, should trigger renewal
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            // Assertions
            // The request succeeded (token was renewed and retried)
            ((int)result.StatusCode).Should().BeGreaterThanOrEqualTo(200);
            ((int)result.StatusCode).Should().BeLessThan(300);

            // The authCallback was called twice: once for expired token, once for renewal
            Volatile.Read(ref callbackCount).Should().Be(2);
        }

        // UTS: rest/integration/RSA8/capability-restriction-4
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_CapabilityRestriction()
        {
            // Setup
            // Create a JWT with capability restricted to a specific channel
            var allowedChannel = "test-RSA8-cap-allowed-" + RandomId();
            var deniedChannel = "test-RSA8-cap-denied-" + RandomId();

            var jwt = GenerateJwt(
                ExtractKeyName(ApiKey),
                ExtractKeySecret(ApiKey),
                capability: "{\"" + allowedChannel + "\":[\"publish\",\"subscribe\"]}",
                ttl: TimeSpan.FromMilliseconds(3600000));

            var client = Rest(new ClientOptions { Token = jwt });

            // Test Steps
            // Publish to allowed channel should succeed — the JWT grants "publish" capability.
            // Note: Do NOT use client.request("GET", "/channels/...") here — that is a channel
            // status request which requires "channel-metadata" capability, not "publish".
            await client.Channels.Get(allowedChannel).PublishAsync("test", "hello");

            // Publish to denied channel should fail with 40160 (capability refused)
            Func<Task> publishDenied = () => client.Channels.Get(deniedChannel).PublishAsync("test", "hello");
            var error = (await publishDenied.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Code.Should().Be(40160);
            ((int?)error.StatusCode).Should().Be(401);
        }
    }
}
