#if MSGPACK
namespace IO.Ably.Tests.MessagePack
{
    public class MessagePackSerializationTests : AblySpecs
    {
        public MessagePackSerializationTests(ITestOutputHelper output) : base(output)
        {

        }

        [Fact]
        public void CanSerialiseListOfMessagesAndDeserialiseThem()
        {
            var message = new Message("example", "The quick brown fox jumped over the lazy dog");
            var serialised = MsgPackHelper.Serialise(new List<Message> {message});

            var result = MsgPackHelper.Deserialise(serialised, typeof (List<Message>)) as List<Message>;
            var resultMessage = result.First();

            resultMessage.Data.Should().Be(message.Data);
            resultMessage.Name.Should().Be(message.Name);

        }

        [Fact]
        public void CanSerializeAndDeserialiseCapabilityObject()
        {
            var allAllowed = Capability.AllowAll;
            var withOneResource = new Capability();
            withOneResource.AddResource("test").AllowPresence().AllowPublish().AllowSubscribe();
            var withTwoResources = new Capability();
            withTwoResources.AddResource("one").AllowAll();
            withTwoResources.AddResource("two").AllowPublish().AllowSubscribe();

            var list = new[] {allAllowed, withOneResource, withTwoResources};
            foreach (var item in list)
            {
                var data = MsgPackHelper.Serialise(item);
                var unpacked = MsgPackHelper.Deserialise(data, typeof(Capability));
                Assert.Equal(item, unpacked);
            }
        }

        [Fact]
        public void CanSerialiseAndDeserialiseTokenDetailsCorrectly()
        {
            var details = new TokenDetails()
            {
                Token = "DaC_fA.DzqwNZFHNIl_GpUR6ENpeqewYnzf5LtI2L2RCP0eDSs597_6OXEW5vumSHiBn2pdxP7ge420UNbrqpKaS_W4aWE5oc15OriGL_8hELLGpgDEoNsnUixWIizGvhsnKVFYT",
                KeyName = "DaC_fA.ChPHsQ",
                Issued = 1462827574141.FromUnixTimeInMilliseconds(),
                Expires = 1462831174141.FromUnixTimeInMilliseconds(),
                Capability = new Capability("{\"*\":[\"*\"]}"),
                ClientId = "123"
            };

            var bytes =
                "hqV0b2tlbtmIRGFDX2ZBLkR6cXdOWkZITklsX0dwVVI2RU5wZXFld1luemY1THRJMkwyUkNQMGVEU3M1OTdfNk9YRVc1dnVtU0hpQm4ycGR4UDdnZTQyMFVOYnJxcEthU19XNGFXRTVvYzE1T3JpR0xfOGhFTExHcGdERW9Oc25VaXhXSWl6R3Zoc25LVkZZVKdrZXlOYW1lrURhQ19mQS5DaFBIc1GmaXNzdWVkzwAAAVSXUWN9p2V4cGlyZXPPAAABVJeIUf2qY2FwYWJpbGl0eat7IioiOlsiKiJdfahjbGllbnRJZKMxMjM="
                    .FromBase64();

            var packed = MsgPackHelper.Serialise(details);
            var unpacked = (TokenDetails)MsgPackHelper.Deserialise(packed, typeof(TokenDetails));
            unpacked.Should().BeEquivalentTo(details);
            var unpackedFromRaw = MsgPackHelper.Deserialise(bytes, typeof(TokenDetails));
            unpackedFromRaw.Should().BeEquivalentTo(details);
        }

        [Fact]
        public void CanSerialiseAndDeserialiseStatsCorrectly()
        {
            var bytes =
                "kYqqaW50ZXJ2YWxJZLAyMDE1LTAyLTAzOjE1OjA1pHVuaXSmbWludXRlo2FsbIKjYWxsgqVjb3VudG6kZGF0Yc0q+KhtZXNzYWdlc4KlY291bnRupGRhdGHNKvinaW5ib3VuZIKjYWxsgqNhbGyCpWNvdW50RqRkYXRhzRtYqG1lc3NhZ2VzgqVjb3VudEakZGF0Yc0bWKhyZWFsdGltZYKjYWxsgqVjb3VudEakZGF0Yc0bWKhtZXNzYWdlc4KlY291bnRGpGRhdGHNG1iob3V0Ym91bmSCo2FsbIKjYWxsgqVjb3VudCikZGF0Yc0PoKhtZXNzYWdlc4KlY291bnQopGRhdGHND6CocmVhbHRpbWWCo2FsbIKlY291bnQopGRhdGHND6CobWVzc2FnZXOCpWNvdW50KKRkYXRhzQ+gqXBlcnNpc3RlZIKjYWxsgqVjb3VudBSkZGF0Yc0H0KhwcmVzZW5jZYKlY291bnQUpGRhdGHNB9CrY29ubmVjdGlvbnOCo2FsbIOkcGVhaxSjbWluAKZvcGVuZWQKo3Rsc4KkcGVhaxSmb3BlbmVkCqhjaGFubmVsc4KkcGVhazKmb3BlbmVkHqthcGlSZXF1ZXN0c4Kpc3VjY2VlZGVkMqZmYWlsZWQKrXRva2VuUmVxdWVzdHOCqXN1Y2NlZWRlZDymZmFpbGVkFA=="
                    .FromBase64();

            var expected = JsonHelper.Deserialize<List<Stats>>(ResourceHelper.GetResource("MsgPackStatsTest.json"));

            var unpacked = (List<Stats>) MsgPackHelper.Deserialise(bytes, typeof(List<Stats>));

            unpacked.Should().BeEquivalentTo(expected);

        }

        [Fact]
        public void CanSerialiseAndDeserialiseTokenDetailsWithEmptyCapability()
        {
            var details = new TokenDetails()
            {
                Token = "DaC_fA.DzqwNZFHNIl_GpUR6ENpeqewYnzf5LtI2L2RCP0eDSs597_6OXEW5vumSHiBn2pdxP7ge420UNbrqpKaS_W4aWE5oc15OriGL_8hELLGpgDEoNsnUixWIizGvhsnKVFYT",
                KeyName = "DaC_fA.ChPHsQ",
                Issued = 1462827574141.FromUnixTimeInMilliseconds(),
                Capability = new Capability(),
            };

            var packed = MsgPackHelper.Serialise(details);
            var unpacked = (TokenDetails)MsgPackHelper.Deserialise(packed, typeof(TokenDetails));
            unpacked.Should().BeEquivalentTo(details);
        }

        [Fact]
        public void CanSerialiseAndDeserialiseBase64ByteArray()
        {
            var message = new Message() {Name = "example", Data = "AAECAwQFBgcICQoLDA0ODw==".FromBase64()};
            var serialised = MsgPackHelper.Serialise(new List<Message> { message });
            var resultMessage = MsgPackHelper.Deserialise(serialised, typeof(List<Message>)) as List<Message>;
            var data = resultMessage.First().Data as byte[];
            data.Should().BeEquivalentTo(message.Data as byte[]);
            resultMessage.First().Name.Should().Be(message.Name);
        }

        [Fact]
        public void CanDeserialiseTokenResponse()
        {
            var value =
                "gaxhY2Nlc3NfdG9rZW6GpXRva2Vu2YhnNFg2UVEuRHlCYzlMZUdvdy1saWVEcG4zTXRsd09uUEhoN2VtN3MyQ3JTZ1pLM2NUNkRvZUo1dlQxWXRwNDFvaTVWUUtNUkxuSVdDckFadHVOb3F5Q0lvVFphQjFfb1FFX0Utb3c2Y3hKX1EwcFUyZ3lpb2xRNGp1VDM1TjI0Qzgzd0p6aUI5p2tleU5hbWWtZzRYNlFRLnV0ekdsZ6Zpc3N1ZWTOVMEP1qdleHBpcmVzzlTBHeaqY2FwYWJpbGl0eYGhKpGhKqhjbGllbnRJZKMxMjM=";

            var decodedMessagePack = MsgPackHelper.Deserialise(value.FromBase64(), typeof (MessagePackObject)).ToString();

            var response = JsonHelper.Deserialize<TokenResponse>(decodedMessagePack);

            response.AccessToken.Should().NotBeNull();
            response.AccessToken.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");
            response.AccessToken.ClientId.Should().Be("123");
            response.AccessToken.Token.Should().Be("g4X6QQ.DyBc9LeGow-lieDpn3MtlwOnPHh7em7s2CrSgZK3cT6DoeJ5vT1Ytp41oi5VQKMRLnIWCrAZtuNoqyCIoTZaB1_oQE_E-ow6cxJ_Q0pU2gyiolQ4juT35N24C83wJziB9");
            response.AccessToken.Issued.Should().Be(((long)1421938646).FromUnixTimeInMilliseconds());
            response.AccessToken.Expires.Should().Be(((long)1421942246).FromUnixTimeInMilliseconds());
        }

        [Fact]
        public void CanDeserialiseConnectionDetailsMessages()
        {
            var connectionDetails = new ConnectionDetails() { ClientId = "123", ConnectionStateTtl = TimeSpan.FromSeconds(60)};
            var serialized = MsgPackHelper.Serialise(connectionDetails);
            var deserialized = MsgPackHelper.Deserialise(serialized, typeof(ConnectionDetails));
            deserialized.Should().BeEquivalentTo(connectionDetails);
        }
    }

    public class JsonSerializationTests
    {
        [Fact]
        public void CanDeserialiseTokenResponse()
        {
            var value = @"{
	            ""access_token"": {
		            ""token"": ""_SYo4Q.D3WmHhU"",
		            ""keyName"": ""_SYo4Q.j8mhAQ"",
		            ""issued"": 1449163326485,
		            ""expires"": 1449163326485,
		            ""capability"": {
			            ""*"": [
				            ""*""
			            ]
		            },
		            ""clientId"": ""123""
	            }
            }";

            var response = JsonHelper.Deserialize<TokenResponse>(value);

            response.AccessToken.Should().NotBeNull();
            response.AccessToken.Capability.ToJson().Should().Be("{\"*\":[\"*\"]}");
            response.AccessToken.ClientId.Should().Be("123");
            response.AccessToken.Token.Should().Be("_SYo4Q.D3WmHhU");
            response.AccessToken.Issued.Should().Be(((long)1449163326485).FromUnixTimeInMilliseconds());
            response.AccessToken.Expires.Should().Be(((long)1449163326485).FromUnixTimeInMilliseconds());
        }
    }
}
#endif
