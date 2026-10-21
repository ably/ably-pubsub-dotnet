// Token Request Parameter Defaults (RSA5, RSA6, RSA9)
// Derived from uts/rest/unit/auth/token_request_params.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: createTokenRequest() returns the signed request as a JSON string in this SDK, so it is parsed into TokenRequest to read ttl and capability (idiomatic); the spec sets no mock, so none is installed (the strict default fails the test on any HTTP request)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    public class TokenRequestParamsTests : UtsUnitTestBase
    {
        public TokenRequestParamsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA5/ttl-null-when-unspecified-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA5", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA5")]
        public async Task RSA5_TtlNullWhenUnspecified()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync());

            // Assertions
            // TTL should be null (not zero, not a default like 3600000)
            tokenRequest.Ttl.Should().BeNull();
        }

        // UTS: rest/unit/RSA5b/explicit-ttl-preserved-0
        [UtsFact]
        [Trait("spec", "RSA5b")]
        public async Task RSA5b_ExplicitTtlPreserved()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync(new TokenParams { Ttl = TimeSpan.FromMilliseconds(7200000) }));

            // Assertions
            tokenRequest.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
        }

        // UTS: rest/unit/RSA5c/ttl-from-default-params-0
        [UtsFact]
        [Trait("spec", "RSA5c")]
        public async Task RSA5c_TtlFromDefaultParams()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromMilliseconds(1800000) },
            });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync());

            // Assertions
            tokenRequest.Ttl.Should().Be(TimeSpan.FromMilliseconds(1800000));
        }

        // UTS: rest/unit/RSA5d/explicit-ttl-overrides-default-0
        [UtsFact]
        [Trait("spec", "RSA5d")]
        public async Task RSA5d_ExplicitTtlOverridesDefault()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromMilliseconds(1800000) },
            });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync(new TokenParams { Ttl = TimeSpan.FromMilliseconds(600000) }));

            // Assertions
            tokenRequest.Ttl.Should().Be(TimeSpan.FromMilliseconds(600000));
        }

        // UTS: rest/unit/RSA6/capability-null-when-unspecified-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSA6", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSA6")]
        public async Task RSA6_CapabilityNullWhenUnspecified()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync());

            // Assertions
            // Capability should be null (not a default like '{"*":["*"]}')
            tokenRequest.Capability.Should().BeNull();
        }

        // UTS: rest/unit/RSA6b/explicit-capability-preserved-0
        [UtsFact]
        [Trait("spec", "RSA6b")]
        public async Task RSA6b_ExplicitCapabilityPreserved()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync(new TokenParams { Capability = new Capability("{\"channel-a\":[\"publish\",\"subscribe\"]}") }));

            // Assertions
            tokenRequest.Capability.ToJson().Should().Be("{\"channel-a\":[\"publish\",\"subscribe\"]}");
        }

        // UTS: rest/unit/RSA6c/capability-from-default-params-0
        [UtsFact]
        [Trait("spec", "RSA6c")]
        public async Task RSA6c_CapabilityFromDefaultParams()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DefaultTokenParams = new TokenParams { Capability = new Capability("{\"*\":[\"subscribe\"]}") },
            });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync());

            // Assertions
            tokenRequest.Capability.ToJson().Should().Be("{\"*\":[\"subscribe\"]}");
        }

        // UTS: rest/unit/RSA6d/explicit-capability-overrides-default-0
        [UtsFact]
        [Trait("spec", "RSA6d")]
        public async Task RSA6d_ExplicitCapabilityOverridesDefault()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DefaultTokenParams = new TokenParams { Capability = new Capability("{\"*\":[\"subscribe\"]}") },
            });

            // Test Steps
            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync(new TokenParams { Capability = new Capability("{\"channel-x\":[\"publish\"]}") }));

            // Assertions
            tokenRequest.Capability.ToJson().Should().Be("{\"channel-x\":[\"publish\"]}");
        }
    }
}
