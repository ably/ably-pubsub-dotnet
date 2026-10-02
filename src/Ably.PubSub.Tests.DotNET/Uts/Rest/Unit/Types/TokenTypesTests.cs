using System;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/token_types.md in ably/specification.
    ///
    /// Spec points: TD1, TD2, TD3, TD4, TD5, TK1, TK2, TK3, TK4, TK5, TK6, TE1, TE2, TE3, TE4, TE5,
    /// TE6
    ///
    /// The spec writes four things in a shape .NET does not use, so the same translation is applied
    /// throughout rather than restated at each site.
    ///
    /// Expiry, issue and request timestamps are milliseconds since the epoch in the spec and
    /// <c>DateTimeOffset</c> here, so a spec literal is built with the <c>FromMs</c> reader at the foot
    /// of this class and read back with <c>ToUnixTimeInMilliseconds()</c>.
    ///
    /// <c>ttl</c> is milliseconds in the spec and <c>TimeSpan?</c> here, so the assertions read
    /// <c>Ttl.Value.TotalMilliseconds</c>.
    ///
    /// <c>capability</c> is a JSON string in the spec and a parsed <c>Capability</c> here, so the
    /// assertions compare <c>Capability.ToJson()</c> against the spec's string.
    ///
    /// <c>fromJson</c> / <c>toJson</c> are <c>JsonHelper.DeserializeObject&lt;T&gt;</c> /
    /// <c>JsonHelper.Serialize</c>, which is the pair that installs the SDK's own converters for all
    /// three of those mappings, and <c>TokenParams.toQueryParams()</c> is
    /// <c>TokenParams.ToRequestParams()</c>.
    /// </summary>
    public class TokenTypesTests : UtsTestBase
    {
        public TokenTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TD1/token-details-attributes-0
        [Fact]
        public void TD1_TokenDetailsAttributes()
        {
            // TD1 - token attribute.
            var tokenDetails = new TokenDetails("test-token")
            {
                Expires = FromMs(1234567890000),
            };

            tokenDetails.Token.Should().Be("test-token");

            // TD2 - expires attribute (milliseconds since epoch).
            tokenDetails.Expires.ToUnixTimeInMilliseconds().Should().Be(1234567890000);

            // TD3 - issued attribute.
            var tokenWithIssued = new TokenDetails("test-token")
            {
                Expires = FromMs(1234567890000),
                Issued = FromMs(1234567800000),
            };

            tokenWithIssued.Issued.ToUnixTimeInMilliseconds().Should().Be(1234567800000);

            // TD4 - capability attribute (JSON string).
            var tokenWithCapability = new TokenDetails("test-token")
            {
                Expires = FromMs(1234567890000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
            };

            tokenWithCapability.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");

            // TD5 - clientId attribute.
            var tokenWithClient = new TokenDetails("test-token")
            {
                Expires = FromMs(1234567890000),
                ClientId = "my-client",
            };

            tokenWithClient.ClientId.Should().Be("my-client");
        }

        // UTS: rest/unit/TD/token-details-from-json-0
        [Fact]
        public void TD_TokenDetailsFromJson()
        {
            var jsonData = new JObject
            {
                { "token", "deserialized-token" },
                { "expires", 1234567890000L },
                { "issued", 1234567800000L },
                { "capability", "{\"channel-1\":[\"publish\"]}" },
                { "clientId", "json-client" },
                { "keyName", "appId.keyId" },
            };

            var tokenDetails = JsonHelper.DeserializeObject<TokenDetails>(jsonData);

            tokenDetails.Token.Should().Be("deserialized-token");
            tokenDetails.Expires.ToUnixTimeInMilliseconds().Should().Be(1234567890000);
            tokenDetails.Issued.ToUnixTimeInMilliseconds().Should().Be(1234567800000);
            tokenDetails.Capability.ToJson().Should().Be("{\"channel-1\":[\"publish\"]}");
            tokenDetails.ClientId.Should().Be("json-client");
        }

        // UTS: rest/unit/TK1/token-params-attributes-0
        [Fact]
        public void TK1_TokenParamsAttributes()
        {
            // TK1 - ttl attribute (milliseconds, nullable).
            var withTtl = new TokenParams { Ttl = TimeSpan.FromMilliseconds(3600000) };
            withTtl.Ttl.Value.TotalMilliseconds.Should().Be(3600000);

            // TK1 - ttl defaults to null when not specified (RSA5 depends on this).
            var emptyForTtl = new TokenParams();
            emptyForTtl.Ttl.Should().BeNull();

            // TK2 - capability attribute (nullable).
            var withCapability = new TokenParams { Capability = new Capability("{\"*\":[\"subscribe\"]}") };
            withCapability.Capability.ToJson().Should().Be("{\"*\":[\"subscribe\"]}");

            // TK2 - capability defaults to null when not specified (RSA6 depends on this).
            var emptyForCapability = new TokenParams();
            emptyForCapability.Capability.Should().BeNull();

            // TK3 - clientId attribute.
            var withClientId = new TokenParams { ClientId = "param-client" };
            withClientId.ClientId.Should().Be("param-client");

            // TK4 - timestamp attribute (milliseconds since epoch).
            var withTimestamp = new TokenParams { Timestamp = FromMs(1234567890000) };
            withTimestamp.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567890000);

            // TK5 - nonce attribute.
            var withNonce = new TokenParams { Nonce = "unique-nonce-value" };
            withNonce.Nonce.Should().Be("unique-nonce-value");

            // TK6 - all attributes together.
            var all = new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(7200000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
                ClientId = "full-client",
                Timestamp = FromMs(1234567890000),
                Nonce = "full-nonce",
            };

            all.Ttl.Value.TotalMilliseconds.Should().Be(7200000);
            all.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");
            all.ClientId.Should().Be("full-client");
            all.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567890000);
            all.Nonce.Should().Be("full-nonce");
        }

        // UTS: rest/unit/TK/token-params-to-query-string-0
        [Fact]
        public void TK_TokenParamsToQueryString()
        {
            var tokenParams = new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(3600000),
                ClientId = "query-client",
                Capability = new Capability("{\"ch\":[\"pub\"]}"),
            };

            var queryMap = tokenParams.ToRequestParams();

            queryMap["ttl"].Should().Be("3600000");
            queryMap["clientId"].Should().Be("query-client");
            queryMap["capability"].Should().Be("{\"ch\":[\"pub\"]}");
        }

        // UTS: rest/unit/TE1/token-request-attributes-0
        [Fact]
        public void TE1_TokenRequestAttributes()
        {
            // TE1 - keyName attribute.
            var withKeyName = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-1",
            };

            withKeyName.KeyName.Should().Be("appId.keyId");

            // TE2 - ttl attribute (nullable).
            var withTtl = new TokenRequest
            {
                KeyName = "appId.keyId",
                Ttl = TimeSpan.FromMilliseconds(3600000),
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-2",
            };

            withTtl.Ttl.Value.TotalMilliseconds.Should().Be(3600000);

            // TE2 - ttl defaults to null when not specified (RSA5 depends on this).
            var withoutTtl = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-2b",
            };

            withoutTtl.Ttl.Should().BeNull();

            // TE3 - capability attribute (nullable).
            var withCapability = new TokenRequest
            {
                KeyName = "appId.keyId",
                Capability = new Capability("{\"*\":[\"*\"]}"),
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-3",
            };

            withCapability.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");

            // TE3 - capability defaults to null when not specified (RSA6 depends on this).
            var withoutCapability = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-3b",
            };

            withoutCapability.Capability.Should().BeNull();

            // TE4 - clientId attribute.
            var withClientId = new TokenRequest
            {
                KeyName = "appId.keyId",
                ClientId = "request-client",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-4",
            };

            withClientId.ClientId.Should().Be("request-client");

            // TE5 - timestamp attribute.
            var withTimestamp = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-5",
            };

            withTimestamp.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567890000);

            // TE6 - nonce attribute.
            var withNonce = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "unique-nonce",
            };

            withNonce.Nonce.Should().Be("unique-nonce");
        }

        // UTS: rest/unit/TE/token-request-mac-signature-0
        [Fact]
        public void TE_TokenRequestMacSignature()
        {
            var request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Timestamp = FromMs(1234567890000),
                Nonce = "nonce-value",
                Mac = "signature-base64",
            };

            request.Mac.Should().Be("signature-base64");
        }

        // UTS: rest/unit/TE/token-request-to-json-1
        [Fact]
        public void TE_TokenRequestToJson()
        {
            var request = new TokenRequest
            {
                KeyName = "appId.keyId",
                Ttl = TimeSpan.FromMilliseconds(3600000),
                Capability = new Capability("{\"*\":[\"*\"]}"),
                ClientId = "json-client",
                Timestamp = FromMs(1234567890000),
                Nonce = "json-nonce",
                Mac = "json-mac",
            };

            var jsonData = JObject.Parse(JsonHelper.Serialize(request));

            ((string)jsonData["keyName"]).Should().Be("appId.keyId");
            ((long)jsonData["ttl"]).Should().Be(3600000);
            ((string)jsonData["capability"]).Should().Be("{\"*\":[\"*\"]}");
            ((string)jsonData["clientId"]).Should().Be("json-client");
            ((long)jsonData["timestamp"]).Should().Be(1234567890000);
            ((string)jsonData["nonce"]).Should().Be("json-nonce");
            ((string)jsonData["mac"]).Should().Be("json-mac");
        }

        // UTS: rest/unit/TE/token-request-from-json-2
        [Fact]
        public void TE_TokenRequestFromJson()
        {
            var jsonData = new JObject
            {
                { "keyName", "appId.keyId" },
                { "ttl", 7200000L },
                { "capability", "{\"ch\":[\"sub\"]}" },
                { "clientId", "from-json-client" },
                { "timestamp", 1234567899999L },
                { "nonce", "from-json-nonce" },
                { "mac", "from-json-mac" },
            };

            var request = JsonHelper.DeserializeObject<TokenRequest>(jsonData);

            request.KeyName.Should().Be("appId.keyId");
            request.Ttl.Value.TotalMilliseconds.Should().Be(7200000);
            request.Capability.ToJson().Should().Be("{\"ch\":[\"sub\"]}");
            request.ClientId.Should().Be("from-json-client");
            request.Timestamp.Value.ToUnixTimeInMilliseconds().Should().Be(1234567899999);
            request.Nonce.Should().Be("from-json-nonce");
            request.Mac.Should().Be("from-json-mac");
        }

        /// <summary>
        /// The spec's millisecond-since-epoch literals, as the <c>DateTimeOffset</c> every token type
        /// here stores. Defined once so the conversion is in one place.
        /// </summary>
        private static DateTimeOffset FromMs(long milliseconds) =>
            milliseconds.FromUnixTimeInMilliseconds();
    }
}
