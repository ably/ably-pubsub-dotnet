// Options Types Tests (TO1, TO2, TO3, AO1, AO2)
// Derived from uts/rest/unit/types/options_types.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class OptionsTypesTests : UtsUnitTestBase
    {
        public OptionsTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TO3/client-options-attributes-0
        [Fact]
        [Trait("spec", "TO3")]
        public void TO3_ClientOptionsAttributes()
        {
            // Test Steps - Defaults
            var options = new ClientOptions();

            options.AuthMethod.Should().Be(HttpMethod.Get);
            options.Tls.Should().BeTrue();
            options.HttpRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
            options.HttpMaxRetryCount.Should().Be(3);

            // NOTE: no assertion: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op and reads false)
            // ASSERT options.useBinaryProtocol == true
            options.IdempotentRestPublishing.Should().BeTrue();
            options.AddRequestIds.Should().BeFalse();
            // DEVIATION: the SDK leaves QueryTime unset (null, behaves as false), the spec says it defaults to false - see deviations.md
            options.QueryTime.Should().BeNull();

            // NOTE: no assertion: the SDK has no maxMessageSize option (TO3l8; rest-mapping.md section 8, Missing APIs)
            // ASSERT options.maxMessageSize == 65536

            // Test Steps - Setting Values
            options = new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                ClientId = "my-client",
                Endpoint = "test",
                Tls = false,
                HttpRequestTimeout = TimeSpan.FromMilliseconds(30000),
                UseBinaryProtocol = false,
                IdempotentRestPublishing = false,
                AddRequestIds = true,
            };

            options.Key.Should().Be("appId.keyId:keySecret");
            options.ClientId.Should().Be("my-client");
            options.Endpoint.Should().Be("test");
            options.Tls.Should().BeFalse();
            options.HttpRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(30000));
            options.UseBinaryProtocol.Should().BeFalse();
            options.IdempotentRestPublishing.Should().BeFalse();
            options.AddRequestIds.Should().BeTrue();
        }

        // UTS: rest/unit/TO3/client-options-custom-hosts-1
        [Fact]
        [Trait("spec", "TO3")]
        public void TO3_ClientOptionsCustomHosts()
        {
            // Test Steps
            // NOTE: restHost is a removed option (Endpoint replaces it); Endpoint = "<host>" gives the same primary domain (REC1b2)
            // restHost: "custom.ably.example.com"
            var options = new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "custom.ably.example.com",
                FallbackHosts = new[] { "fallback1.example.com", "fallback2.example.com" },
            };

            // ASSERT options.restHost == "custom.ably.example.com"
            options.Endpoint.Should().Be("custom.ably.example.com");

            // NOTE: no assertion: ClientOptions.FallbackHosts is set-only in this SDK (no public getter; TO3k6; rest-mapping.md section 8, Missing APIs)
            // ASSERT options.fallbackHosts == ["fallback1.example.com", "fallback2.example.com"]
        }

        // UTS: rest/unit/TO3/client-options-auth-url-2
        [Fact]
        [Trait("spec", "TO3")]
        public void TO3_ClientOptionsAuthUrl()
        {
            // Test Steps
            var options = new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthMethod = HttpMethod.Post,
                AuthHeaders = new Dictionary<string, string> { ["X-API-Key"] = "secret" },
                AuthParams = new Dictionary<string, string> { ["scope"] = "full" },
            };

            // Assertions
            options.AuthUrl.Should().Be(new Uri("https://auth.example.com/token"));
            options.AuthMethod.Should().Be(HttpMethod.Post);
            options.AuthHeaders["X-API-Key"].Should().Be("secret");
            options.AuthParams["scope"].Should().Be("full");
        }

        // UTS: rest/unit/TO3/client-options-default-token-params-3
        [Fact]
        [Trait("spec", "TO3")]
        public void TO3_ClientOptionsDefaultTokenParams()
        {
            // Test Steps
            var options = new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DefaultTokenParams = new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(7200000),
                    ClientId = "default-client",
                    Capability = new Capability("{\"*\":[\"subscribe\"]}"),
                },
            };

            // Assertions
            options.DefaultTokenParams.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
            options.DefaultTokenParams.ClientId.Should().Be("default-client");
            options.DefaultTokenParams.Capability.ToJson().Should().Be("{\"*\":[\"subscribe\"]}");
        }

        // UTS: rest/unit/AO2/auth-options-attributes-0
        [Fact]
        [Trait("spec", "AO2")]
        public void AO2_AuthOptionsAttributes()
        {
            // Test Steps
            var authOptions = new AuthOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthMethod = HttpMethod.Post,
                AuthHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer api-key" },
                AuthParams = new Dictionary<string, string> { ["user"] = "test" },
                QueryTime = true,
            };

            // Assertions
            authOptions.AuthUrl.Should().Be(new Uri("https://auth.example.com/token"));
            authOptions.AuthMethod.Should().Be(HttpMethod.Post);
            authOptions.AuthHeaders["Authorization"].Should().Be("Bearer api-key");
            authOptions.AuthParams["user"].Should().Be("test");
            authOptions.QueryTime.Should().BeTrue();
        }

        // UTS: rest/unit/AO/auth-options-with-callback-0
        [UtsFact]
        [Trait("spec", "AO")]
        public async Task AO_AuthOptionsWithCallback()
        {
            // Test Steps
            var callbackCalled = false;

            // NOTE: now() is read from the wall clock; the expiry is never compared
            Func<TokenParams, Task<object>> testCallback = tokenParams =>
            {
                callbackCalled = true;
                return Task.FromResult<object>(new TokenDetails("callback-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var authOptions = new AuthOptions { AuthCallback = testCallback };

            // Verify callback is stored and callable
            var result = (TokenDetails)await authOptions.AuthCallback(new TokenParams());

            // Assertions
            callbackCalled.Should().BeTrue();
            result.Token.Should().Be("callback-token");
        }

        // UTS: rest/unit/TO/endpoint-affects-host-0
        [Fact]
        [Trait("spec", "TO")]
        public void TO_EndpointAffectsHost()
        {
            // Test Cases
            var endpoints = new[] { null, "test", "custom-env" };

            // Test Steps
            foreach (var endpoint in endpoints)
            {
                ClientOptions options;
                if (endpoint == null)
                {
                    options = new ClientOptions { Key = "appId.keyId:keySecret" };
                }
                else
                {
                    options = new ClientOptions
                    {
                        Key = "appId.keyId:keySecret",
                        Endpoint = endpoint,
                    };
                }

                options.Endpoint.Should().Be(endpoint);
            }
        }

        // UTS: rest/unit/TO/conflicting-options-validation-1
        [Fact]
        [Trait("spec", "TO")]
        public void TO_ConflictingOptionsValidation()
        {
            // Test Steps (Case 1 - key + authCallback)
            // NOTE: the spec gives no steps for case 1; derived from the Test Cases table: key + authCallback is valid (authCallback takes precedence), so construction succeeds
            var clientWithBoth = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("callback-token")),
            });
            clientWithBoth.Should().NotBeNull();

            // Test Steps (Case 2 - Conflicting hosts)
            // NOTE: case 2 (restHost + endpoint) is not applicable: restHost is a removed option (Endpoint replaces it)
            // ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   restHost: "custom.host.com",
            //   endpoint: "test"
            // ) FAILS WITH error
            // ASSERT error.message CONTAINS "restHost" OR error.message CONTAINS "endpoint"

            // Test Steps (Case 3 - No auth)
            Action create = () => Rest(new ClientOptions());
            var error = create.Should().Throw<AblyException>().Which.ErrorInfo;
            (error.Message.Contains("auth") || error.Message.Contains("key") || error.Message.Contains("token")).Should().BeTrue();
        }
    }
}
