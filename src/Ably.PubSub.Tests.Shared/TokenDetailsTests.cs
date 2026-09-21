using System;

using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IO.Ably.Tests
{
    public class TokenDetailsTests
    {
        [Fact]
        public void FromJson_ParsesTokenCorrectly()
        {
            const string json = @"{
	                            ""access_token"": {
		                            ""token"": ""QF_CjTvDs2kFQMKLwpccEhIkNcKpw5ovPsOnLsOgJMKow5ACXHvCgGzCtcK7"",
		                            ""key"": ""3lJG9Q"",
		                            ""issued"": 1430784000000,
		                            ""expires"": 1430784000000,
		                            ""capability"": {
			                            ""*"": [
				                            ""*""
			                            ]
		                            }
	                            }
                            }";

            var token = JsonHelper.DeserializeObject<TokenDetails>((JObject)JObject.Parse(json)["access_token"]);

            token.Token.Should().Be("QF_CjTvDs2kFQMKLwpccEhIkNcKpw5ovPsOnLsOgJMKow5ACXHvCgGzCtcK7");

            token.Issued.ToUnixTimeInMilliseconds().Should().Be(1430784000000);
            token.Expires.ToUnixTimeInMilliseconds().Should().Be(1430784000000);
            var expectedCapability = new Capability();
            expectedCapability.AddResource("*").AllowAll();
            token.Capability.ToJson().Should().Be(expectedCapability.ToJson());
        }

        [Fact]
        public void ShouldSerializeDatesInMilliseconds()
        {
            var details = new TokenDetails
            {
                Expires = DateTimeOffset.UtcNow,
                Issued = DateTimeOffset.UtcNow.AddSeconds(1),
            };

            var json = JsonHelper.Serialize(details);

            var jObject = JObject.Parse(json);
            ((string)jObject["expires"]).Should().Be(details.Expires.ToUnixTimeInMilliseconds().ToString());
            ((string)jObject["issued"]).Should().Be(details.Issued.ToUnixTimeInMilliseconds().ToString());
        }

        [Fact]
        public void ShouldExcludeClientIdWhenNull()
        {
            var details = new TokenDetails("123");
            var json = JsonHelper.Serialize(details);
            json.Should().NotContain("clientId");
        }
    }
}
