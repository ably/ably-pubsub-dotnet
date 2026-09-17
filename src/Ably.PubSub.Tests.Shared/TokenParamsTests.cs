using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class TokenParamsTests : AblySpecs
    {
        [Fact]
        public void ToRequestParams_CreatesADictionaryOfAllProperties()
        {
            var @params = new TokenParams
            {
                Capability = Capability.AllowAll,
                Ttl = TimeSpan.FromHours(1),
                Timestamp = Now,
                ClientId = "123",
                Nonce = "test"
            };

            var result = @params.ToRequestParams();

            result["capability"].Should().Be(@params.Capability.ToJson());
            result["ttl"].Should().Be(@params.Ttl.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            result["timestamp"].Should().Be(@params.Timestamp.Value.ToUnixTimeInMilliseconds().ToString());
            result["clientId"].Should().Be(@params.ClientId);
            result["nonce"].Should().Be(@params.Nonce);
        }

        [Fact]
        public void ToRequestParams_SkipsNullOrEmptyValues()
        {
            var @params = new TokenParams
            {
                Capability = Capability.AllowAll,
                Ttl = TimeSpan.FromHours(1),
            };

            var result = @params.ToRequestParams();

            result["capability"].Should().Be(@params.Capability.ToJson());
            result["ttl"].Should().Be(@params.Ttl.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            result.Keys.Should().HaveCount(2);
        }

        [Fact]
        public void ToRequestParams_WithDictionaryToMerge_MergesValuesWithoutDuplicatesAndFavoursTokenParamsValues()
        {
            var @params = new TokenParams
            {
                Capability = Capability.AllowAll,
                Ttl = TimeSpan.FromHours(1),
            };
            var toMerge = new Dictionary<string, string> { { "ttl", "123400" }, { "authKey1", "authValue1" } };

            var result = @params.ToRequestParams(toMerge);

            result.Keys.Should().HaveCount(3);
            result["ttl"].Should().Be(@params.Ttl.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            result["authKey1"].Should().Be("authValue1");
        }

        public TokenParamsTests(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
