// Token Types Tests (TD1, TD2, TD3, TD4, TD5, TK1, TK2, TK3, TK4, TK5, TK6, TE1, TE2, TE3, TE4, TE5, TE6)
// Derived from uts/rest/unit/types/token_types.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
    public class TokenTypesTests : UtsUnitTestBase
    {
        public TokenTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TD1/token-details-attributes-0
        [Fact]
        [Trait("spec", "TD1")]
        public void TD1_TokenDetailsAttributes()
        {
            // Test Steps
            // TD1 - token attribute
            var tokenDetails = new TokenDetails("test-token")
            {
                Expires = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
            };
            tokenDetails.Token.Should().Be("test-token");

            // TD2 - expires attribute (milliseconds since epoch)
            tokenDetails.Expires.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // TD3 - issued attribute
            var tokenWithIssued = new TokenDetails("test-token")
            {
                Expires = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Issued = DateTimeOffset.FromUnixTimeMilliseconds(1234567800000),
            };
            tokenWithIssued.Issued.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567800000));

            // TD4 - capability attribute (JSON string)
            var tokenWithCapability = new TokenDetails("test-token")
            {
                Expires = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
            };
            tokenWithCapability.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");

            // TD5 - clientId attribute
            var tokenWithClient = new TokenDetails("test-token")
            {
                Expires = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                ClientId = "my-client",
            };
            tokenWithClient.ClientId.Should().Be("my-client");
        }

        // UTS: rest/unit/TD/token-details-from-json-0
        [Fact]
        [Trait("spec", "TD")]
        public void TD_TokenDetailsFromJson()
        {
            // Test Steps
            var jsonData = new JObject
            {
                ["token"] = "deserialized-token",
                ["expires"] = 1234567890000,
                ["issued"] = 1234567800000,
                ["capability"] = "{\"channel-1\":[\"publish\"]}",
                ["clientId"] = "json-client",
                ["keyName"] = "appId.keyId",
            };

            var tokenDetails = JsonHelper.DeserializeObject<TokenDetails>(jsonData);

            // Assertions
            tokenDetails.Token.Should().Be("deserialized-token");
            tokenDetails.Expires.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));
            tokenDetails.Issued.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567800000));
            tokenDetails.Capability.ToJson().Should().Be("{\"channel-1\":[\"publish\"]}");
            tokenDetails.ClientId.Should().Be("json-client");
        }

        // UTS: rest/unit/TK1/token-params-attributes-0
        [Fact]
        [Trait("spec", "TK1")]
        public void TK1_TokenParamsAttributes()
        {
            // Test Steps
            // TK1 - ttl attribute (milliseconds, nullable)
            var tokenParams = new TokenParams { Ttl = TimeSpan.FromMilliseconds(3600000) };
            tokenParams.Ttl.Should().Be(TimeSpan.FromMilliseconds(3600000));

            // TK1 - ttl defaults to null when not specified (RSA5 depends on this)
            tokenParams = new TokenParams();
            tokenParams.Ttl.Should().NotHaveValue();

            // TK2 - capability attribute (nullable)
            tokenParams = new TokenParams { Capability = new Capability("{\"*\":[\"subscribe\"]}") };
            tokenParams.Capability.ToJson().Should().Be("{\"*\":[\"subscribe\"]}");

            // TK2 - capability defaults to null when not specified (RSA6 depends on this)
            tokenParams = new TokenParams();
            tokenParams.Capability.Should().BeNull();

            // TK3 - clientId attribute
            tokenParams = new TokenParams { ClientId = "param-client" };
            tokenParams.ClientId.Should().Be("param-client");

            // TK4 - timestamp attribute (milliseconds since epoch)
            tokenParams = new TokenParams { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000) };
            tokenParams.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // TK5 - nonce attribute
            tokenParams = new TokenParams { Nonce = "unique-nonce-value" };
            tokenParams.Nonce.Should().Be("unique-nonce-value");

            // TK6 - All attributes together
            tokenParams = new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(7200000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
                ClientId = "full-client",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "full-nonce",
            };
            tokenParams.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
            tokenParams.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");
            tokenParams.ClientId.Should().Be("full-client");
            tokenParams.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));
            tokenParams.Nonce.Should().Be("full-nonce");
        }

        // UTS: rest/unit/TK/token-params-to-query-string-0
        [Fact]
        [Trait("spec", "TK")]
        public void TK_TokenParamsToQueryString()
        {
            // Test Steps
            var tokenParams = new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(3600000),
                ClientId = "query-client",
                Capability = new Capability("{\"ch\":[\"pub\"]}"),
            };

            var queryMap = tokenParams.ToRequestParams();

            // Assertions
            queryMap["ttl"].Should().Be("3600000");
            queryMap["clientId"].Should().Be("query-client");
            queryMap["capability"].Should().Be("{\"ch\":[\"pub\"]}");
        }

        // UTS: rest/unit/TE1/token-request-attributes-0
        [Fact]
        [Trait("spec", "TE1")]
        public void TE1_TokenRequestAttributes()
        {
            // Test Steps
            // TE1 - keyName attribute
            var request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-1",
            };
            request.KeyName.Should().Be("appId.keyId");

            // TE2 - ttl attribute (nullable)
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Ttl = TimeSpan.FromMilliseconds(3600000),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-2",
            };
            request.Ttl.Should().Be(TimeSpan.FromMilliseconds(3600000));

            // TE2 - ttl defaults to null when not specified (RSA5 depends on this)
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-2b",
            };
            request.Ttl.Should().NotHaveValue();

            // TE3 - capability attribute (nullable)
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Capability = new Capability("{\"*\":[\"*\"]}"),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-3",
            };
            request.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");

            // TE3 - capability defaults to null when not specified (RSA6 depends on this)
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-3b",
            };
            request.Capability.Should().BeNull();

            // TE4 - clientId attribute
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                ClientId = "request-client",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-4",
            };
            request.ClientId.Should().Be("request-client");

            // TE5 - timestamp attribute
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-5",
            };
            request.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // TE6 - nonce attribute
            request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "unique-nonce",
            };
            request.Nonce.Should().Be("unique-nonce");
        }

        // UTS: rest/unit/TE/token-request-mac-signature-0
        [Fact]
        [Trait("spec", "TE")]
        public void TE_TokenRequestMacSignature()
        {
            // Test Steps
            var request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "nonce-value",
                Mac = "signature-base64",
            };

            // Assertions
            request.Mac.Should().Be("signature-base64");
        }

        // UTS: rest/unit/TE/token-request-to-json-1
        [Fact]
        [Trait("spec", "TE")]
        public void TE_TokenRequestToJson()
        {
            // Test Steps
            var request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Ttl = TimeSpan.FromMilliseconds(3600000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
                ClientId = "json-client",
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000),
                Nonce = "json-nonce",
                Mac = "json-mac",
            };

            var jsonData = JObject.Parse(JsonHelper.Serialize(request));

            // Assertions
            ((string)jsonData["keyName"]).Should().Be("appId.keyId");
            ((long)jsonData["ttl"]).Should().Be(3600000L);
            ((string)jsonData["capability"]).Should().Be("{\"*\":[\"*\"]}");
            ((string)jsonData["clientId"]).Should().Be("json-client");
            ((long)jsonData["timestamp"]).Should().Be(1234567890000L);
            ((string)jsonData["nonce"]).Should().Be("json-nonce");
            ((string)jsonData["mac"]).Should().Be("json-mac");
        }

        // UTS: rest/unit/TE/token-request-from-json-2
        [Fact]
        [Trait("spec", "TE")]
        public void TE_TokenRequestFromJson()
        {
            // Test Steps
            var jsonData = new JObject
            {
                ["keyName"] = "appId.keyId",
                ["ttl"] = 7200000,
                ["capability"] = "{\"ch\":[\"sub\"]}",
                ["clientId"] = "from-json-client",
                ["timestamp"] = 1234567899999,
                ["nonce"] = "from-json-nonce",
                ["mac"] = "from-json-mac",
            };

            var request = JsonHelper.DeserializeObject<TokenRequest>(jsonData);

            // Assertions
            request.KeyName.Should().Be("appId.keyId");
            request.Ttl.Should().Be(TimeSpan.FromMilliseconds(7200000));
            request.Capability.ToJson().Should().Be("{\"ch\":[\"sub\"]}");
            request.ClientId.Should().Be("from-json-client");
            request.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567899999));
            request.Nonce.Should().Be("from-json-nonce");
            request.Mac.Should().Be("from-json-mac");
        }
    }
}
