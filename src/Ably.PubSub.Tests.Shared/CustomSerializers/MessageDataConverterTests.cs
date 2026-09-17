using System;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Shared.CustomSerializers
{
    public class MessageDataConverterTests
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly JsonSerializerSettings _jsonSettings = JsonHelper.Settings;

        public MessageDataConverterTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        private class TestLetter
        {
            [JsonProperty("sender")]
            public string Sender { get; set; }

            [JsonProperty("receiver")]
            public string Receiver { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }

        [Fact]
        public void ShouldParse_Message_WithData_Transparently()
        {
            var message = new Message
            {
                Id = "my-id",
                Data = new TestLetter
                {
                    Sender = "naruto-kun",
                    Receiver = "sakura-chan",
                    Message = null
                },
                ClientId = "my-client-id",
                ConnectionId = "my-connection-id",
                Encoding = null,
                Extras = null,
                Name = "my-sweet-name",
                Timestamp = DateTimeOffset.Now
            };

            var serialized = JsonConvert.SerializeObject(message, _jsonSettings);
            var serializedJToken = JToken.Parse(serialized);
            serializedJToken["encoding"].Should().BeNull();
            serializedJToken["extras"].Should().BeNull();
            serializedJToken["data"].Should().NotBeNull();
            serializedJToken["data"].Type.Should().Be(JTokenType.Object);
            serializedJToken["data"]["sender"].Value<string>().Should().BeEquivalentTo("naruto-kun");
            serializedJToken["data"]["receiver"].Value<string>().Should().BeEquivalentTo("sakura-chan");
            serializedJToken["data"]["message"].Should().NotBeNull();
            serializedJToken["data"]["message"].Type.Should().Be(JTokenType.Null);
        }
    }
}
