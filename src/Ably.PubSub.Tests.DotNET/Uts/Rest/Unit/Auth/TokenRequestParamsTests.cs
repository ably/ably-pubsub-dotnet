using System;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Auth
{
    /// <summary>
    /// Derived from uts/rest/unit/auth/token_request_params.md in ably/specification.
    ///
    /// Spec points: RSA5, RSA5b, RSA5c, RSA5d, RSA6, RSA6b, RSA6c, RSA6d
    ///
    /// <para>
    /// <c>Auth.CreateTokenRequestAsync</c> returns the token request already serialised, rather than
    /// a <c>TokenRequest</c> object, so every assertion reads the JSON the SDK would put on the wire.
    /// That is the stronger place to assert anyway: the spec's whole subject is which fields are
    /// present on the wire, and <c>TokenRequest.Capability</c> carries
    /// <c>NullValueHandling.Ignore</c>, so an unset capability is *absent* rather than null in the
    /// payload. Both readings are accepted below.
    /// </para>
    ///
    /// <para>
    /// No mock HTTP is needed: <c>createTokenRequest</c> signs locally and makes no request. One is
    /// still installed because <c>UtsClients.RestClient</c> takes one, and it records nothing.
    /// </para>
    /// </summary>
    public class TokenRequestParamsTests : UtsTestBase
    {
        public TokenRequestParamsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA5/ttl-null-when-unspecified-0
        //
        // DEVIATION. RSA5 is emphatic: if tokenParams does not specify a TTL the field "MUST be null
        // (or the equivalent absent/unset value) in the tokenRequest ... Implementations MUST NOT
        // default this to 3600000 client-side", so that Ably can apply its own 60-minute default.
        // This SDK does default it: AblyAuth.CreateTokenRequestAsync falls back to
        // TokenParams.WithDefaultsApplied() (TokenParams.cs:96-104), which sets
        // Ttl = Defaults.DefaultTokenTtl — one hour. See Uts/deviations.md.
        [DeviationFact]
        public async Task RSA5_TtlNullWhenUnspecified()
        {
            var client = RestClient(new MockHttpClient());

            var tokenRequest = await CreateTokenRequest(client);

            TtlOf(tokenRequest).Should().BeNull("RSA5 - the server supplies the default, not the client");
        }

        // UTS: rest/unit/RSA5b/explicit-ttl-preserved-0
        [Fact]
        public async Task RSA5b_ExplicitTtlPreserved()
        {
            var client = RestClient(new MockHttpClient());

            var tokenRequest = await CreateTokenRequest(client, new TokenParams
            {
                Ttl = TimeSpan.FromHours(2),
            });

            TtlOf(tokenRequest).Should().Be(7200000);
        }

        // UTS: rest/unit/RSA5c/ttl-from-default-params-0
        [Fact]
        public async Task RSA5c_TtlFromDefaultParams()
        {
            var client = RestClient(new MockHttpClient(), configure: options =>
                options.DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromMinutes(30) });

            var tokenRequest = await CreateTokenRequest(client);

            TtlOf(tokenRequest).Should().Be(1800000);
        }

        // UTS: rest/unit/RSA5d/explicit-ttl-overrides-default-0
        [Fact]
        public async Task RSA5d_ExplicitTtlOverridesDefault()
        {
            var client = RestClient(new MockHttpClient(), configure: options =>
                options.DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromMinutes(30) });

            var tokenRequest = await CreateTokenRequest(client, new TokenParams
            {
                Ttl = TimeSpan.FromMinutes(10),
            });

            TtlOf(tokenRequest).Should().Be(600000);
        }

        // UTS: rest/unit/RSA6/capability-null-when-unspecified-0
        //
        // DEVIATION, same root cause as RSA5. RSA6 forbids defaulting the capability client-side so
        // that the token inherits the key's own capabilities; TokenParams.WithDefaultsApplied() sets
        // Capability = Defaults.DefaultTokenCapability, which is Capability.AllowAll — the
        // '{"*":["*"]}' the spec names explicitly. Recorded as one entry with RSA5.
        [DeviationFact]
        public async Task RSA6_CapabilityNullWhenUnspecified()
        {
            var client = RestClient(new MockHttpClient());

            var tokenRequest = await CreateTokenRequest(client);

            CapabilityOf(tokenRequest).Should().BeNull(
                "RSA6 - the token inherits the key's capabilities unless asked otherwise");
        }

        // UTS: rest/unit/RSA6b/explicit-capability-preserved-0
        [Fact]
        public async Task RSA6b_ExplicitCapabilityPreserved()
        {
            var client = RestClient(new MockHttpClient());

            var tokenRequest = await CreateTokenRequest(client, new TokenParams
            {
                Capability = new Capability("{\"channel-a\":[\"publish\",\"subscribe\"]}"),
            });

            var capability = CapabilityOf(tokenRequest);
            capability.Should().NotBeNull();
            capability.Should().Contain("channel-a");
            capability.Should().Contain("publish");
            capability.Should().Contain("subscribe");
        }

        // UTS: rest/unit/RSA6c/capability-from-default-params-0
        [Fact]
        public async Task RSA6c_CapabilityFromDefaultParams()
        {
            var client = RestClient(new MockHttpClient(), configure: options =>
                options.DefaultTokenParams = new TokenParams
                {
                    Capability = new Capability("{\"*\":[\"subscribe\"]}"),
                });

            var tokenRequest = await CreateTokenRequest(client);

            var capability = CapabilityOf(tokenRequest);
            capability.Should().NotBeNull();
            capability.Should().Contain("subscribe");
            capability.Should().NotContain("publish");
        }

        // UTS: rest/unit/RSA6d/explicit-capability-overrides-default-0
        [Fact]
        public async Task RSA6d_ExplicitCapabilityOverridesDefault()
        {
            var client = RestClient(new MockHttpClient(), configure: options =>
                options.DefaultTokenParams = new TokenParams
                {
                    Capability = new Capability("{\"*\":[\"subscribe\"]}"),
                });

            var tokenRequest = await CreateTokenRequest(client, new TokenParams
            {
                Capability = new Capability("{\"channel-x\":[\"publish\"]}"),
            });

            var capability = CapabilityOf(tokenRequest);
            capability.Should().NotBeNull();
            capability.Should().Contain("channel-x");
            capability.Should().Contain("publish");
        }

        private static async Task<JObject> CreateTokenRequest(
            PubSubHttpClient client,
            TokenParams tokenParams = null)
            => JObject.Parse(await client.Auth.CreateTokenRequestAsync(tokenParams));

        /// <summary>The <c>ttl</c> the request would put on the wire, in milliseconds, or null.</summary>
        private static long? TtlOf(JObject tokenRequest)
        {
            var ttl = tokenRequest["ttl"];
            return ttl == null || ttl.Type == JTokenType.Null ? (long?)null : ttl.Value<long>();
        }

        /// <summary>
        /// The <c>capability</c> the request would put on the wire, or null. Absent and explicitly
        /// null both read as null, which is what RSA6's "null or the equivalent absent/unset value"
        /// asks for.
        /// </summary>
        private static string CapabilityOf(JObject tokenRequest)
        {
            var capability = tokenRequest["capability"];
            return capability == null || capability.Type == JTokenType.Null
                ? null
                : capability.ToString();
        }
    }
}
