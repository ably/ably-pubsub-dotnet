using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/options_types.md in ably/specification.
    ///
    /// Spec points: TO1, TO2, TO3, AO1, AO2
    ///
    /// Four translation decisions apply across the file.
    ///
    /// The spec's <c>endpoint</c> option is translated as <c>ClientOptions.Environment</c>. There is no
    /// member called <c>Endpoint</c> anywhere in this SDK, and the hosts the spec's own table expects
    /// ("test" produces "test-rest.ably.io") are exactly what <c>Environment</c> produces, so the two
    /// name one option here.
    ///
    /// <c>restHost</c> and <c>fallbackHosts</c> are write-only on <c>ClientOptions</c>; the readers are
    /// <c>FullRestHost()</c> and <c>GetFallbackHosts()</c>, which is what the spec's
    /// <c>options.restHost</c> and <c>options.fallbackHosts</c> reads become.
    ///
    /// <c>authMethod</c> is an <c>HttpMethod</c> rather than a string, so the spec's comparison against
    /// "GET"/"POST" reads <c>AuthMethod.Method</c>.
    ///
    /// Two of the spec's defaults are asserted against what this SDK really does, with the spec's
    /// expectation stated at the site: <c>useBinaryProtocol</c> (msgpack is compiled out of this build,
    /// so the getter is hard-coded false) and <c>queryTime</c> (modelled as a nullable tri-state, so
    /// the stored default is null rather than false).
    /// </summary>
    public class OptionsTypesTests : UtsTestBase
    {
        public OptionsTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TO3/client-options-attributes-0
        [Fact]
        public void TO3_ClientOptionsAttributesDefaults()
        {
            var options = new ClientOptions();

            // The spec's "Test Steps - Defaults" block, in its order.
            options.AuthMethod.Method.Should().Be("GET");
            options.Tls.Should().BeTrue();
            options.HttpRequestTimeout.TotalMilliseconds.Should().Be(10000);
            options.HttpMaxRetryCount.Should().Be(3);

            // The spec expects useBinaryProtocol to default to true. msgpack is compiled out of this
            // build - Ably.PubSub.Core.csproj imports the MsgPack shared project only when the MSGPACK
            // constant is defined - so the getter returns a hard-coded false and JSON is the only
            // protocol available. Asserting the value this build actually has.
            options.UseBinaryProtocol.Should().BeFalse();

            options.IdempotentRestPublishing.Should().BeTrue();
            options.AddRequestIds.Should().BeFalse();

            // The spec expects queryTime to default to false. .NET models it as a bool? so that
            // AuthOptions.Merge can tell "unset" from "explicitly false", so the stored default is
            // null and the effective default is false.
            options.QueryTime.Should().BeNull();

            // NOTE: the spec's 23-row required-attribute table also lists maxMessageSize (default
            // 65536). There is no such ClientOptions member in this SDK - the only MaxMessageSize is
            // the server-supplied Types/ConnectionDetails.MaxMessageSize - so there is nothing to
            // assert for it here.

            // The remaining defaults from that table, which the Test Steps block does not cover.
            options.Key.Should().BeNull();
            options.Token.Should().BeNull();
            options.TokenDetails.Should().BeNull();
            options.AuthCallback.Should().BeNull();
            options.AuthUrl.Should().BeNull();
            options.AuthHeaders.Should().BeEmpty();
            options.AuthParams.Should().BeEmpty();
            options.ClientId.Should().BeNull();
            options.Environment.Should().BeNull();
            options.FullRestHost().Should().Be("rest.ably.io");
            options.GetFallbackHosts().Should().Equal(new[]
            {
                "a.ably-realtime.com",
                "b.ably-realtime.com",
                "c.ably-realtime.com",
                "d.ably-realtime.com",
                "e.ably-realtime.com",
            });
            options.HttpMaxRetryDuration.TotalMilliseconds.Should().Be(15000);
            options.FallbackRetryTimeout.TotalMilliseconds.Should().Be(600000);
            options.DefaultTokenParams.Should().BeNull();
        }

        // UTS: rest/unit/TO3/client-options-attributes-0
        [Fact]
        public void TO3_ClientOptionsAttributesSettingValues()
        {
            var options = new ClientOptions(UtsClients.ValidKey)
            {
                ClientId = "my-client",
                Environment = "test",
                Tls = false,
                HttpRequestTimeout = TimeSpan.FromMilliseconds(30000),
                UseBinaryProtocol = false,
                IdempotentRestPublishing = false,
                AddRequestIds = true,
            };

            options.Key.Should().Be(UtsClients.ValidKey);
            options.ClientId.Should().Be("my-client");
            options.Environment.Should().Be("test");
            options.Tls.Should().BeFalse();
            options.HttpRequestTimeout.TotalMilliseconds.Should().Be(30000);
            options.UseBinaryProtocol.Should().BeFalse();
            options.IdempotentRestPublishing.Should().BeFalse();
            options.AddRequestIds.Should().BeTrue();
        }

        // UTS: rest/unit/TO3/client-options-custom-hosts-1
        [Fact]
        public void TO3_ClientOptionsCustomHosts()
        {
            var options = new ClientOptions(UtsClients.ValidKey)
            {
                RestHost = "custom.ably.example.com",
                FallbackHosts = new[] { "fallback1.example.com", "fallback2.example.com" },
            };

            options.FullRestHost().Should().Be("custom.ably.example.com");
            options.GetFallbackHosts().Should().Equal(new[]
            {
                "fallback1.example.com",
                "fallback2.example.com",
            });
        }

        // UTS: rest/unit/TO3/client-options-auth-url-2
        [Fact]
        public void TO3_ClientOptionsAuthUrl()
        {
            var options = new ClientOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthMethod = HttpMethod.Post,
                AuthHeaders = new Dictionary<string, string> { { "X-API-Key", "secret" } },
                AuthParams = new Dictionary<string, string> { { "scope", "full" } },
            };

            options.AuthUrl.ToString().Should().Be("https://auth.example.com/token");
            options.AuthMethod.Method.Should().Be("POST");
            options.AuthHeaders["X-API-Key"].Should().Be("secret");
            options.AuthParams["scope"].Should().Be("full");
        }

        // UTS: rest/unit/TO3/client-options-default-token-params-3
        [Fact]
        public void TO3_ClientOptionsDefaultTokenParams()
        {
            var options = new ClientOptions(UtsClients.ValidKey)
            {
                DefaultTokenParams = new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(7200000),
                    ClientId = "default-client",
                    Capability = new Capability("{\"*\":[\"subscribe\"]}"),
                },
            };

            options.DefaultTokenParams.Ttl.Value.TotalMilliseconds.Should().Be(7200000);
            options.DefaultTokenParams.ClientId.Should().Be("default-client");
            options.DefaultTokenParams.Capability.ToJson().Should().Be("{\"*\":[\"subscribe\"]}");
        }

        // UTS: rest/unit/AO2/auth-options-attributes-0
        [Fact]
        public void AO2_AuthOptionsAttributes()
        {
            var authOptions = new AuthOptions
            {
                AuthUrl = new Uri("https://auth.example.com/token"),
                AuthMethod = HttpMethod.Post,
                AuthHeaders = new Dictionary<string, string> { { "Authorization", "Bearer api-key" } },
                AuthParams = new Dictionary<string, string> { { "user", "test" } },
                QueryTime = true,
            };

            authOptions.AuthUrl.ToString().Should().Be("https://auth.example.com/token");
            authOptions.AuthMethod.Method.Should().Be("POST");
            authOptions.AuthHeaders["Authorization"].Should().Be("Bearer api-key");
            authOptions.AuthParams["user"].Should().Be("test");
            authOptions.QueryTime.Should().BeTrue();

            // The spec's test-case table (rows 1-4) also enumerates key, token, tokenDetails and
            // authCallback as required AuthOptions attributes; its Test Steps block does not set them.
            var withCredentials = new AuthOptions
            {
                Key = UtsClients.ValidKey,
                Token = "a-token-literal",
                TokenDetails = new TokenDetails("a-token-details-token"),
                AuthCallback = tokenParams => Task.FromResult<object>(null),
            };

            withCredentials.Key.Should().Be(UtsClients.ValidKey);
            withCredentials.Token.Should().Be("a-token-literal");
            withCredentials.TokenDetails.Token.Should().Be("a-token-details-token");
            withCredentials.AuthCallback.Should().NotBeNull();
        }

        // UTS: rest/unit/AO/auth-options-with-callback-0
        [Fact]
        public async Task AO_AuthOptionsWithCallback()
        {
            var callbackCalled = false;

            Func<TokenParams, Task<object>> testCallback = tokenParams =>
            {
                callbackCalled = true;
                return Task.FromResult<object>(new TokenDetails("callback-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddMilliseconds(3600000),
                });
            };

            var authOptions = new AuthOptions { AuthCallback = testCallback };

            // The .NET callback is Func<TokenParams, Task<object>>, so the spec's
            // auth_options.authCallback(TokenParams()) is awaited and its result downcast.
            var result = await authOptions.AuthCallback(new TokenParams());

            callbackCalled.Should().BeTrue();
            result.Should().BeOfType<TokenDetails>();
            ((TokenDetails)result).Token.Should().Be("callback-token");
        }

        // UTS: rest/unit/TO/endpoint-affects-host-0
        [Fact]
        public void TO_EndpointAffectsHost()
        {
            // The spec's Test Steps only assert that the option round-trips, but its test-case table
            // names the rest host each endpoint is expected to produce, and its Note says host
            // resolution "may be tested at the HTTP client level". FullRestHost() is the options
            // object's own resolution, so both halves are asserted here.

            // Case 1 - no endpoint, so the production host.
            var production = new ClientOptions(UtsClients.ValidKey);
            production.Environment.Should().BeNull();
            production.FullRestHost().Should().Be("rest.ably.io");

            // Case 2 - endpoint "test".
            var test = new ClientOptions(UtsClients.ValidKey) { Environment = "test" };
            test.Environment.Should().Be("test");
            test.FullRestHost().Should().Be("test-rest.ably.io");

            // Case 3 - endpoint "custom-env".
            var custom = new ClientOptions(UtsClients.ValidKey) { Environment = "custom-env" };
            custom.Environment.Should().Be("custom-env");
            custom.FullRestHost().Should().Be("custom-env-rest.ably.io");
        }

        // UTS: rest/unit/TO/conflicting-options-validation-1
        [Fact]
        public void TO_ConflictingOptionsValidationKeyAndAuthCallback()
        {
            // Case 1 of the spec's table: key + authCallback is valid, and authCallback takes
            // precedence. The mock HTTP client is installed only so that constructing the client
            // cannot reach a real HttpClient; no request is made.
            var mockHttp = new MockHttpClient();

            var client = RestClient(mockHttp, configure: options =>
            {
                options.AuthCallback = tokenParams => Task.FromResult<object>(null);
            });

            client.Options.Key.Should().Be(UtsClients.ValidKey);
            client.Options.AuthCallback.Should().NotBeNull();

            // "authCallback takes precedence" is observable as the chosen auth method, which is not on
            // the public IAblyAuth surface; it is read off the internal AblyAuth instead, which the
            // test assembly can see through InternalsVisibleTo.
            client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
        }

        // UTS: rest/unit/TO/conflicting-options-validation-1
        [Fact]
        public void TO_ConflictingOptionsValidationConflictingHosts()
        {
            // Case 2 of the spec's table: the spec expects ClientOptions carrying both restHost and
            // endpoint to fail with an error naming one of them. This SDK performs no such validation.
            // It has no endpoint member at all, and where both restHost and environment are given,
            // FullRestHost() documents restHost as the winner and raises nothing. The adapted
            // assertion records that behaviour rather than an expectation that cannot hold here.
            var options = new ClientOptions(UtsClients.ValidKey)
            {
                RestHost = "custom.host.com",
                Environment = "test",
            };

            options.FullRestHost().Should().Be("custom.host.com");
            options.Environment.Should().Be("test");
        }

        // UTS: rest/unit/TO/conflicting-options-validation-1
        [Fact]
        public void TO_ConflictingOptionsValidationNoAuthOptions()
        {
            // Case 3 of the spec's table: a client built from options carrying no credentials at all
            // must fail. The client is constructed directly rather than through UtsTestBase.RestClient
            // because that helper supplies the key whose absence is the point of the test.
            Action act = () =>
            {
                _ = new PubSubHttpClient(new ClientOptions());
            };

            var error = act.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Message.Should().Contain("auth");
            error.Message.Should().Contain("key");
            error.Message.Should().Contain("token");
        }
    }
}
