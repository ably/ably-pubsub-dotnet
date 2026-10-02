using System;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/auth.md in ably/specification.
    ///
    /// Spec points: RSA4, RSA8, RSC10
    ///
    /// The spec file's header lists only RSA4 and RSA8, but the file also carries one RSC10 section
    /// with a full test body; it is derived here under its own spec point.
    ///
    /// Four file-wide translation decisions:
    ///
    /// SandboxOptions seeds Key from the provisioned app, so every test whose spec ClientOptions
    /// carries only a token or only an authCallback clears Key explicitly. Left set, AblyAuth would
    /// choose basic auth and the credential under test would never reach the server.
    ///
    /// The spec's "AWAIT client.request(method, path)" is PubSubHttpClient.Request(HttpMethod, path).
    /// That path sets AblyRequest.NoExceptionOnHttpError, so a 4xx comes back as an
    /// HttpPaginatedResponse carrying StatusCode and ErrorCode rather than throwing, which is what
    /// makes the spec's "ASSERT result.statusCode == 401" assertable at all.
    ///
    /// Where a spec authCallback returns a JWT, the derived callback hands back a TokenDetails that
    /// wraps the JWT string. .NET's AuthCallback returns an object, and AblyAuth.GetTokenRequest
    /// reads a bare string result as a serialised TokenRequest rather than as a token, so
    /// TokenDetails is this SDK's spelling of "return this token string". The missing third form is
    /// recorded as an API gap rather than silently worked around.
    ///
    /// The "Token Formats" preamble asks that every test run with both JWTs and Ably native tokens.
    /// The spec's own test list already splits the two where the distinction bites - token-auth-jwt
    /// against token-auth-native, auth-callback-jwt against auth-callback-token-request - so the
    /// tests are not additionally doubled here.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class AuthTests : UtsIntegrationTestBase
    {
        public AuthTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSA4/basic-auth-key-0
        [Fact]
        public async Task RSA4_BasicAuthKey()
        {
            var channelName = "test-RSA4-" + UtsSandbox.RandomId();

            // The specs' app_config.keys[0].key_str is SandboxRestClient's default key.
            var client = await SandboxRestClient();

            // The channel status endpoint requires authentication, which is the point of the test.
            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
        }

        // UTS: rest/integration/RSA8/token-auth-jwt-0
        [Fact]
        public async Task RSA8_TokenAuthJwt()
        {
            var sandbox = await Sandbox();
            var apiKey = sandbox.KeyStr;

            var jwt = UtsSandbox.GenerateJwt(
                UtsSandbox.ExtractKeyName(apiKey),
                UtsSandbox.ExtractKeySecret(apiKey),
                ttl: TimeSpan.FromMilliseconds(3600000));

            var channelName = "test-RSA8-jwt-" + UtsSandbox.RandomId();
            var client = await SandboxRestClient(configure: options =>
            {
                options.Key = null;
                options.Token = jwt;
            });

            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
        }

        // UTS: rest/integration/RSA8/token-auth-native-1
        [Fact]
        public async Task RSA8_TokenAuthNative()
        {
            var keyClient = await SandboxRestClient();

            var tokenDetails = await keyClient.Auth.RequestTokenAsync();

            var channelName = "test-RSA8-native-" + UtsSandbox.RandomId();
            var tokenClient = await SandboxRestClient(configure: options =>
            {
                options.Key = null;
                options.Token = tokenDetails.Token;
            });

            var result = await tokenClient.Request(HttpMethod.Get, "/channels/" + channelName);

            // "token IS String" is a static guarantee here - TokenDetails.Token is typed string, so a
            // runtime type assertion cannot fail. The length assertion the spec pairs it with is what
            // carries the coverage.
            tokenDetails.Token.Should().NotBeNullOrEmpty();
            tokenDetails.Expires.Should().BeAfter(DateTimeOffset.UtcNow);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
        }

        // UTS: rest/integration/RSA8/auth-callback-token-request-2
        [Fact]
        public async Task RSA8_AuthCallbackTokenRequest()
        {
            var tokenRequestClient = await SandboxRestClient();

            var channelName = "test-RSA8-callback-" + UtsSandbox.RandomId();
            var client = await SandboxRestClient(configure: options =>
            {
                options.Key = null;

                // CreateTokenRequestAsync returns the signed token request already serialised, which
                // is the shape AblyAuth.GetTokenRequest expects from a string callback result.
                options.AuthCallback = async tokenParams =>
                    await tokenRequestClient.Auth.CreateTokenRequestAsync(tokenParams);
            });

            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
        }

        // UTS: rest/integration/RSA8/auth-callback-jwt-3
        [Fact]
        public async Task RSA8_AuthCallbackJwt()
        {
            var sandbox = await Sandbox();
            var apiKey = sandbox.KeyStr;
            var keyName = UtsSandbox.ExtractKeyName(apiKey);
            var keySecret = UtsSandbox.ExtractKeySecret(apiKey);

            var channelName = "test-RSA8-jwt-callback-" + UtsSandbox.RandomId();
            var client = await SandboxRestClient(configure: options =>
            {
                options.Key = null;

                // NOTE: the spec returns the JWT string itself. .NET's AuthCallback returns an object
                // and AblyAuth.GetTokenRequest deserialises a bare string result as a TokenRequest, so
                // a raw JWT would be rejected as malformed JSON before it ever reached the server.
                // TokenDetails(jwt) is this SDK's spelling of the same return value; the absent plain
                // token-string form is reported as an API gap.
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(
                        keyName,
                        keySecret,
                        ttl: tokenParams.Ttl ?? TimeSpan.FromMilliseconds(3600000),
                        clientId: tokenParams.ClientId)));
            });

            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
        }

        // UTS: rest/integration/RSA4/invalid-credentials-rejected-1
        [Fact]
        public async Task RSA4_InvalidCredentialsRejected()
        {
            var sandbox = await Sandbox();

            var channelName = "test-RSA4-invalid-" + UtsSandbox.RandomId();

            // The real app id with a fabricated key name, per the spec: the server answers 401 with
            // Ably error code 40400 (key not found).
            var invalidKey = sandbox.AppId + ".invalidKey:invalidSecret";

            var client = await SandboxRestClient(key: invalidKey);

            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().Be(401);
            result.ErrorCode.Should().Be(40400);
        }

        // UTS: rest/integration/RSC10/token-renewal-expired-jwt-0
        //
        // SPEC: RSC10 — when a REST request fails with a token error (40140-40149) the client renews
        // the token and retries. The spec drives this through client.request(), as this test does.
        // SDK: the renewal branch lives in PubSubHttpClient.ExecuteRequest's `catch (AblyException)`
        // (PubSubHttpClient.cs:208-231) and so only fires when the 401 is *thrown*. Request() goes
        // through HttpPaginatedRequestInternal, which sets NoExceptionOnHttpError
        // (PubSubHttpClient.cs:306), and AblyHttpRequester.Execute then *returns* the error response
        // instead of throwing (AblyHttpRequester.cs:141). So no renewal is attempted, the callback is
        // invoked once, and the 401 reaches the caller. Measured against the live sandbox.
        // Env-gated rather than adapted: the spec-correct assertion is the one worth keeping.
        // See Uts/deviations.md.
        [DeviationFact]
        public async Task RSC10_TokenRenewalExpiredJwt()
        {
            var sandbox = await Sandbox();
            var apiKey = sandbox.KeyStr;
            var keyName = UtsSandbox.ExtractKeyName(apiKey);
            var keySecret = UtsSandbox.ExtractKeySecret(apiKey);

            var callbackCount = 0;

            var channelName = "test-RSC10-renewal-" + UtsSandbox.RandomId();
            var client = await SandboxRestClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    var jwt = callbackCount == 1
                        ? UtsSandbox.GenerateJwt(
                            keyName,
                            keySecret,
                            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-5))
                        : UtsSandbox.GenerateJwt(
                            keyName,
                            keySecret,
                            ttl: TimeSpan.FromMilliseconds(3600000));

                    // See the class summary: TokenDetails is .NET's spelling of a returned token
                    // string, so the test exercises renewal rather than the callback's return type.
                    return Task.FromResult<object>(new TokenDetails(jwt));
                };
            });

            var result = await client.Request(HttpMethod.Get, "/channels/" + channelName);

            ((int)result.StatusCode).Should().BeInRange(200, 299);
            callbackCount.Should().Be(2);
        }

        // UTS: rest/integration/RSA8/capability-restriction-4
        [Fact]
        public async Task RSA8_CapabilityRestriction()
        {
            var sandbox = await Sandbox();
            var apiKey = sandbox.KeyStr;

            var allowedChannel = "test-RSA8-cap-allowed-" + UtsSandbox.RandomId();
            var deniedChannel = "test-RSA8-cap-denied-" + UtsSandbox.RandomId();

            var jwt = UtsSandbox.GenerateJwt(
                UtsSandbox.ExtractKeyName(apiKey),
                UtsSandbox.ExtractKeySecret(apiKey),
                ttl: TimeSpan.FromMilliseconds(3600000),
                capability: "{\"" + allowedChannel + "\":[\"publish\",\"subscribe\"]}");

            var client = await SandboxRestClient(configure: options =>
            {
                options.Key = null;
                options.Token = jwt;
            });

            // Publishing, not the channel status endpoint: the JWT grants publish, not
            // channel-metadata, as the spec's own note spells out.
            await client.Channels.Get(allowedChannel).PublishAsync("test", "hello");

            Func<Task> act = () => client.Channels.Get(deniedChannel).PublishAsync("test", "hello");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40160);
            ((int)error.StatusCode.Value).Should().Be(401);
        }
    }
}
