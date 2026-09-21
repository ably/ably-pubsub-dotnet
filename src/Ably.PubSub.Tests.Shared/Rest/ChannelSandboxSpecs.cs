using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using IO.Ably.Encryption;
using IO.Ably.Rest;
using IO.Ably.Tests.Infrastructure;

using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Rest
{
    [Collection("AblyRest SandBox Collection")]
    [Trait("type", "integration")]
    public class ChannelSandboxSpecs : SandboxSpecs
    {
        private readonly JObject _examples;
        private readonly JObject _examples256;

        public ChannelSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
            _examples = JObject.Parse(ResourceHelper.GetResource("crypto-data-128.json"));
            _examples256 = JObject.Parse(ResourceHelper.GetResource("crypto-data-256.json"));
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1c")]
        [Trait("spec", "RSL1d")]
        public async Task SendingAVeryLargeMessage_ShouldThrowErrorToIndicateSendingFailed(Protocol protocol)
        {
            var message = new Message
            {
                Name = "large",
                Data = new string('a', 50 * 1024 * 8), // 100KB
            };
            var client = await GetRestClient(protocol);
            var channel = client.Channels.Get("large");
            _ = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync(message));
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1f1")]
        public async Task WithBasicAuthWhenMessageHasClientId_ShouldRetrieveMessageWithSameClientId(Protocol protocol)
        {
            var message = new Message("test", "test") { ClientId = "123" };
            var client = await GetRestClient(protocol);
            var channel = client.Channels.Get("persisted:test");
            await channel.PublishAsync(message);

            var result = await channel.HistoryAsync();
            result.Items.First().ClientId.Should().Be("123");
        }

        // RSL1g
        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1g1b")]
        [Trait("spec", "RSA7e2")]
        public async Task WithImplicitClientIdComingFromOptions_ReturnsMessageWithCorrectClientId(Protocol protocol)
        {
            var message = new Message("test", "test") { ClientId = null };
            var client = await GetRestClient(protocol, opts => opts.ClientId = "999");
            var channel = client.Channels.Get("persisted:test".AddRandomSuffix());
            await channel.PublishAsync(message);

            await Task.Delay(1000);

            var result = await channel.HistoryAsync();
            result.Items.First().ClientId.Should().Be("999");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1g2")]
        public async Task WithExplicitClientIdMatchingClientIdInOptions_ReturnsMessageWithCorrectClientId(Protocol protocol)
        {
            var message = new Message("test", "test") { ClientId = "999" };
            var client = await GetRestClient(protocol, opts => opts.ClientId = "999");
            var channel = client.Channels.Get("persisted:test".AddRandomSuffix());
            await channel.PublishAsync(message);

            var result = await channel.HistoryAsync();
            result.Items.First().ClientId.Should().Be("999");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1g3")]
        [Trait("spec", "RSL1g4")]
        public async Task WithExplicitClientIdNotMatchingClientIdInOptions_ReturnsMessageWithCorrectClientId(Protocol protocol)
        {
            var message = new Message("test", "test") { ClientId = "999" };
            var client = await GetRestClient(protocol, opts => opts.ClientId = "111");
            var channel = client.Channels.Get("test");
            _ = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync(message));

            // Can publish further messages in the same channel
            await channel.PublishAsync("test", "test");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k1")]
        public async Task IdempotentPublishing_LibraryGeneratesIds(Protocol protocol)
        {
            static void AssertMessage(Message message, int serial)
            {
                message.Id.Should().NotBeNull();
                var idParts = message.Id.Split(':');
                idParts.Should().HaveCount(2);
                idParts[1].Should().Be(serial.ToString());
                byte[] b = Convert.FromBase64String(idParts[0]);
                b.Should().HaveCount(9);
            }

            var msg = new Message("test", "test");
            var client = await GetRestClient(protocol, opts => opts.IdempotentRestPublishing = true, "idempotent-dev");
            var channel = client.Channels.Get("test".AddRandomSuffix());

            await channel.PublishAsync(msg);

            AssertMessage(msg, 0);

            var messages = new[]
            {
                new Message("test1", "test1"),
                new Message("test2", "test2"),
                new Message("test3", "test3"),
            };

            await channel.PublishAsync(messages);

            for (var i = 0; i < messages.Length; i++)
            {
                var m = messages[i];
                AssertMessage(m, i);
            }

            var history = await channel.HistoryAsync();
            history.Items.Should().HaveCount(4);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k2")]
        [Trait("spec", "RSL1k3")]
        public async Task IdempotentPublishing_ClientProvidedMessageIdsArePreserved(Protocol protocol)
        {
            var client = await GetRestClient(protocol, opts => opts.IdempotentRestPublishing = true);
            var channel = client.Channels.Get("test".AddRandomSuffix());

            var msg = new Message("test", "test") { Id = "RSL1k2" };
            await channel.PublishAsync(msg);
            msg.Id.Should().Be("RSL1k2");

            var messages = new[]
            {
                new Message("test1", "test1"),
                new Message("test2", "test2"),
                new Message("test3", "test3"),
            };

            messages[0].Id = "RSL1k3:0";
            messages[1].Id = "RSL1k3:1";
            messages[2].Id = "RSL1k3:2";

            // Can publish further messages in the same channel
            await channel.PublishAsync(messages);

            messages[0].Id.Should().Be("RSL1k3:0");
            messages[1].Id.Should().Be("RSL1k3:1");
            messages[2].Id.Should().Be("RSL1k3:2");

            var history = await channel.HistoryAsync();
            history.Items.Should().HaveCount(4);
            history.Items[3].Id.Should().Be("RSL1k2");
            history.Items[2].Id.Should().Be("RSL1k3:0");
            history.Items[1].Id.Should().Be("RSL1k3:1");
            history.Items[0].Id.Should().Be("RSL1k3:2");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k3")]
        public async Task IdempotentPublishing_MultipleMessagesWithSameId(Protocol protocol)
        {
            var client = await GetRestClient(protocol, opts => opts.IdempotentRestPublishing = true);
            var channel = client.Channels.Get("test".AddRandomSuffix());

            var messages = new[]
            {
                new Message("test1", "test1"),
                new Message("test2", "test2"),
                new Message("test3", "test3"),
            };

            messages[0].Id = "RSL1k3:0";
            messages[1].Id = "RSL1k3:0";
            messages[2].Id = "RSL1k3:0";

            var ex = await Record.ExceptionAsync(async () => await channel.PublishAsync(messages));
            ex.Should().NotBeNull();
            ex.Should().BeOfType<AblyException>();
            ((AblyException)ex).ErrorInfo.Code.Should().Be(ErrorCodes.InvalidPublishRequestInvalidClientSpecifiedId);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k4")]
        public async Task IdempotentPublishing_SimulateErrorAndRetry(Protocol protocol)
        {
            const int numberOfRetries = 2;
            var client = await GetRestClient(protocol, opts =>
            {
                opts.FallbackHosts = new[] { "sandbox-rest.ably.io" };
                opts.IdempotentRestPublishing = true;
            });

            var suffix = string.Empty.AddRandomSuffix();
            var channelName = $"test{suffix}";
            var channel = client.Channels.Get(channelName);
            var messageId = Guid.NewGuid().ToString();
            var messages = new[]
            {
                new Message($"test1{suffix}", "test1") { Id = messageId },
            };

            // intercept the HTTP request overriding the RequestUri
            // to make it appear that a retry against another host has happened
            int tryCount = 0;
            client.HttpClient.Options.HttpMaxRetryCount = numberOfRetries;
            client.HttpClient.SendAsync = async message =>
            {
                var result = await client.HttpClient.InternalSendAsync(message);
                tryCount++;
                if (tryCount < numberOfRetries)
                {
                    // setting IsDefaultHost and raising a TaskCanceledException
                    // will cause the request to retry
                    throw new TaskCanceledException("faked exception to cause retry");
                }

                return result;
            };

            await channel.PublishAsync(messages);

            // publish http request should be made twice
            tryCount.Should().Be(numberOfRetries);

            // restore the SendAsync method
            client.HttpClient.SendAsync = client.HttpClient.InternalSendAsync;

            await Task.Delay(1000);

            var history = await channel.HistoryAsync();
            foreach (var item in history.Items)
            {
                Output.WriteLine("Message: " + item.ToJson());
            }

            history.Items.Should().HaveCount(1);
            history.Items[0].Name.Should().Be($"test1{suffix}");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k5")]
        public async Task IdempotentPublishing_WithClientSpecificMessage_ShouldRetry(Protocol protocol)
        {
            const int numberOfRetries = 2;
            var client = await GetRestClient(protocol, opts =>
            {
                opts.FallbackHosts = new[] { "sandbox-rest.ably.io" };
                opts.IdempotentRestPublishing = true;
            });

            var suffix = string.Empty.AddRandomSuffix();
            var channelName = $"persisted:clientspecificmessage_test{suffix}";
            var channel = client.Channels.Get(channelName);

            var payload = new Message($"test1{suffix}", "test1") { Id = "client_id" };

            // intercept the HTTP request overriding the RequestUri
            // to make it appear that a retry against another host has happened
            int tryCount = 0;
            client.HttpClient.Options.HttpMaxRetryCount = numberOfRetries;
            client.HttpClient.SendAsync = async message =>
            {
                var result = await client.HttpClient.InternalSendAsync(message);
                tryCount++;
                if (tryCount < numberOfRetries)
                {
                    // setting IsDefaultHost and raising a TaskCanceledException
                    // will cause the request to retry
                    throw new TaskCanceledException("faked exception to cause retry");
                }

                return result;
            };

            await channel.PublishAsync(payload);

            // publish http request should be made twice
            tryCount.Should().Be(numberOfRetries);

            // restore the SendAsync method
            client.HttpClient.SendAsync = client.HttpClient.InternalSendAsync;

            await Task.Delay(1000);

            var history = await channel.HistoryAsync();
            history.Items.Should().HaveCount(1);
            history.Items[0].Name.Should().Be($"test1{suffix}");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL1k5")]
        public async Task IdempotentPublishing_SendingAMessageMultipleTimesShouldOnlyPublishOnce(Protocol protocol)
        {
            var client = await GetRestClient(protocol, opts => opts.IdempotentRestPublishing = true);
            var channel = client.Channels.Get("test".AddRandomSuffix());

            var msg = new Message("test", "test") { Id = "RSL1k5" };
            await channel.PublishAsync(msg);
            await channel.PublishAsync(msg);
            await channel.PublishAsync(msg);

            var history = await channel.HistoryAsync();
            history.Items.Should().HaveCount(1);
            history.Items[0].Id.Should().Be("RSL1k5");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL4c4")]
        [Trait("spec", "RSL4d4")]
        [Trait("spec", "RSL5a")]
        [Trait("spec", "RSL5c")]
        [Trait("spec", "RSL6a")]

        // Uses the to publish the examples inside crypto-data-128.json to publish and then retrieve the messages
        public async Task CanPublishAMessageAndRetrieveIt128(Protocol protocol)
        {
            var items = (JArray)_examples["items"];

            AblyRest ably = await GetRestClient(protocol);
            IRestChannel channel = ably.Channels.Get("persisted:test".AddRandomSuffix(), GetOptions(_examples));
            var count = 0;
            foreach (var item in items)
            {
                var encoded = item["encoded"];
                var encoding = (string)encoded["encoding"];
                var decodedData = DecodeData((string)encoded["data"], encoding);
                await channel.PublishAsync((string)encoded["name"], decodedData);
                var message = (await channel.HistoryAsync()).Items.First();
                if (message.Data is byte[])
                {
                    (message.Data as byte[]).Should().BeEquivalentTo(decodedData as byte[], "Item number {0} data does not match decoded data", count);
                }
                else if (encoding == "json")
                {
                    JToken.DeepEquals((JToken)message.Data, (JToken)decodedData).Should().BeTrue("Item number {0} data does not match decoded data", count);
                }
                else
                {
                    message.Data.Should().Be(decodedData, "Item number {0} data does not match decoded data", count);
                }

                count++;
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL5b")]
        [Trait("spec", "RSL5c")]

        // Uses the to publish the examples inside crypto-data-256.json to publish and then retrieve the messages
        public async Task CanPublishAMessageAndRetrieveIt256(Protocol protocol)
        {
            var items = (JArray)_examples256["items"];

            AblyRest ably = await GetRestClient(protocol);
            IRestChannel channel = ably.Channels.Get("persisted:test".AddRandomSuffix(), GetOptions(_examples256));
            var count = 0;
            foreach (var item in items)
            {
                var encoded = item["encoded"];
                var encoding = (string)encoded["encoding"];
                var decodedData = DecodeData((string)encoded["data"], encoding);
                await channel.PublishAsync((string)encoded["name"], decodedData);

                await Task.Delay(1000);

                var message = (await channel.HistoryAsync()).Items.First();
                if (message.Data is byte[])
                {
                    (message.Data as byte[]).Should().BeEquivalentTo(decodedData as byte[], "Item number {0} data does not match decoded data", count);
                }
                else if (encoding == "json")
                {
                    JToken.DeepEquals((JToken)message.Data, (JToken)decodedData).Should().BeTrue("Item number {0} data does not match decoded data", count);
                }
                else
                {
                    message.Data.Should().Be(decodedData, "Item number {0} data does not match decoded data", count);
                }

                count++;
            }
        }

        [Theory]
        [ProtocolData]
        public async Task Send20MessagesAndThenPaginateHistory(Protocol protocol)
        {
            // Arrange
            var client = await GetRestClient(protocol);
            IRestChannel channel = client.Channels.Get("persisted:historyTest:" + protocol);

            // Act
            for (int i = 0; i < 20; i++)
            {
                await channel.PublishAsync("name" + i, "data" + i);
            }

            await Task.Delay(1000);

            // Assert
            var history = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 10 });
            history.Items.Should().HaveCount(10);
            history.HasNext.Should().BeTrue();
            history.Items.First().Name.Should().Be("name19");

            var secondPage = await history.NextAsync();
            secondPage.Items.Should().HaveCount(10);
            secondPage.Items.First().Name.Should().Be("name9");
        }

        [Theory]
        [ProtocolData]
        public async Task Send20MessagesAndThenPaginateHistorySync(Protocol protocol)
        {
            // Arrange
            var client = await GetRestClient(protocol);
            IRestChannel channel = client.Channels.Get("persisted:historyTest:" + protocol);

            // Act
            for (int i = 0; i < 20; i++)
            {
                channel.Publish("name" + i, "data" + i);
            }

            // Assert
            var history = channel.History(new PaginatedRequestParams { Limit = 10 });
            history.Items.Should().HaveCount(10);
            history.HasNext.Should().BeTrue();
            history.Items.First().Name.Should().Be("name19");

            var secondPage = history.Next();
            secondPage.Items.Should().HaveCount(10);
            secondPage.Items.First().Name.Should().Be("name9");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL4a")]
        public async Task WithUnsupportedPayloadTypes_ShouldRaiseException(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var channel = client.Channels.Get("persisted:test_" + protocol);

            _ = await Assert.ThrowsAsync<AblyException>(() => channel.PublishAsync("int", 1));
        }

        private class TestLoggerSink : ILoggerSink
        {
            public LogLevel LastLoggedLevel { get; private set; }

            public string LastMessage { get; private set; }

            public void LogEvent(LogLevel level, string message)
            {
                LastLoggedLevel = level;
                LastMessage = message;
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL6b")]
        public async Task WithEncryptionCipherMismatch_ShouldLeaveMessageEncryptedAndLogError(Protocol protocol)
        {
            var loggerSink = new TestLoggerSink();
            ILogger logger = InternalLogger.Create(LogLevel.Error, loggerSink);

            logger.LogLevel.Should().Be(LogLevel.Error);
            logger.IsDebug.Should().Be(false);

            var client = await GetRestClient(protocol, options =>
            {
                options.Logger = logger; // pass the logger into the client
            });

            var opts = GetOptions(_examples);
            opts.Logger = logger;
            var channel1 = client.Channels.Get("persisted:encryption", opts);

            const string payload = "test payload";
            await channel1.PublishAsync("test", payload);

            await Task.Delay(1000);

            var channel2 = client.Channels.Get("persisted:encryption", new ChannelOptions(logger, true));

            var message = (await channel2.HistoryAsync()).Items.First();

            loggerSink.LastLoggedLevel.Should().Be(LogLevel.Error);
            message.Encoding.Should().Be("utf-8/cipher+aes-128-cbc");
        }

        [Theory]
        [InteroperabilityMessagePayloadData]
        [Trait("spec", "RSL6a1")]
        public async Task WithTestMessagePayloadsWhenDecoding_ShouldDecodeMessagesAsPerSpec(
            Protocol protocol,
            JObject messageData)
        {
            var channelName = "channel-name-" + new Random().Next(int.MaxValue);

            var httpClient = (await AblySandboxFixture.GetSettings()).GetHttpClient();

            var rawMessage = new JObject
            {
                ["data"] = messageData["data"],
                ["encoding"] = messageData["encoding"],
            };

            var request = new AblyRequest($"/channels/{channelName}/messages", HttpMethod.Post)
            {
                RequestBody = rawMessage.ToJson().GetBytes(),
            };

            var client1 = await GetRestClient(protocol);
            await client1.AblyAuth.AddAuthHeader(request);
            await httpClient.Execute(request);

            await Task.Delay(1000);

            var channel = client1.Channels.Get(channelName);
            var result = await channel.HistoryAsync();

            var returnedMessage = result.Items.First();
            var expectedType = (string)messageData["expectedType"];
            if (expectedType == "binary")
            {
                ((byte[])returnedMessage.Data).ToHexString().Should().Be((string)messageData["expectedHexValue"]);
            }
            else
            {
                var returnedData = expectedType == "string" ? returnedMessage.Data.ToString() : returnedMessage.Data.ToJson();
                var expectedValue = expectedType == "string" ? (string)messageData["expectedValue"] : messageData["expectedValue"].ToJson();
                returnedData.Should().Be(expectedValue);
            }
        }

        [Theory]
        [InteroperabilityMessagePayloadData]
        [Trait("spec", "RSL6a1")]
        public async Task WithTestMessagePayloadsWhenDecoding_ShouldEncodeMessagesAsPerSpec(
            Protocol protocol,
            JObject messageData)
        {
            var channelName = "channel-name-" + new Random().Next(int.MaxValue);
            var httpClient = (await AblySandboxFixture.GetSettings()).GetHttpClient();
            var expectedType = (string)messageData["expectedType"];

            var client1 = await GetRestClient(protocol);
            var channel = client1.Channels.Get(channelName);

            // Act
            if (expectedType == "binary")
            {
                await channel.PublishAsync("event", ((string)messageData["expectedHexValue"]).ToByteArray());
            }
            else if (expectedType == "string")
            {
                await channel.PublishAsync("event", (string)messageData["expectedValue"]);
            }
            else
            {
                await channel.PublishAsync("event", messageData["expectedValue"]);
            }

            await Task.Delay(1000);

            var request = new AblyRequest($"/channels/{channelName}/messages", HttpMethod.Get);
            await client1.AblyAuth.AddAuthHeader(request);
            var response = await httpClient.Execute(request);

            // Assert
            var historyData = JArray.Parse(response.TextResponse);
            var responseData = (JObject)historyData.First;
            responseData.Should().NotBeNull();

            if (expectedType == "binary")
            {
                ((string)responseData["data"]).Should().Be((string)messageData["data"]);
            }
            else if (expectedType == "json")
            {
                responseData["data"].ToJson().Should().Be(messageData["data"].ToJson());
            }
            else
            {
                var r = (string)responseData["data"];
                r.Should().Be((string)messageData["data"]);
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL6b")]
        public async Task WithEncryptionCipherAlgorithmMismatch_ShouldLeaveMessageEncryptedAndLogError(Protocol protocol)
        {
            var loggerSink = new TestLoggerSink();
            var logger = InternalLogger.Create(Defaults.DefaultLogLevel, loggerSink);

            var client = await GetRestClient(protocol);
            var channel1 = client.Channels.Get("persisted:encryption", GetOptions(_examples));

            const string payload = "test payload";
            await channel1.PublishAsync("test", payload);

            var channel2 = client.Channels.Get("persisted:encryption", new ChannelOptions(logger, true, new CipherParams(Crypto.GenerateRandomKey(128, CipherMode.CBC))));

            await Task.Delay(1000);

            var message = (await channel2.HistoryAsync()).Items.First();

            loggerSink.LastLoggedLevel.Should().Be(LogLevel.Error);
            loggerSink.LastMessage.Should().Contain("Error decrypting payload");
            message.Encoding.Should().Be("utf-8/cipher+aes-128-cbc");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSL8")]
        [Trait("spec", "CHD1")]
        public async Task ChannelDetails_AreAvailable(Protocol protocol)
        {
            const string Name = "Test";

            AblyRest client = await GetRestClient(protocol);
            IRestChannel c = client.Channels.Get(Name);

            ChannelDetails cd = await c.StatusAsync();
            cd.ChannelId.Should().Be(Name);
            cd.Status.IsActive.Should().BeTrue();

            ChannelMetrics cm = cd.Status.Occupancy.Metrics;
            cm.Connections.Should().Be(0);
            cm.PresenceConnections.Should().Be(0);
            cm.PresenceMembers.Should().Be(0);
            cm.PresenceSubscribers.Should().Be(0);
            cm.Publishers.Should().Be(0);
            cm.Subscribers.Should().Be(0);
        }

        private static object DecodeData(string data, string encoding)
        {
            switch (encoding)
            {
                case "json":
                    return JsonHelper.Deserialize(data);
                case "base64":
                    return data.FromBase64();
                default:
                    return data;
            }
        }

        private static ChannelOptions GetOptions(JObject data)
        {
            var key = (string)data["key"];
            var iv = (string)data["iv"];
            var cipherParams = new CipherParams("aes", key, CipherMode.CBC, iv);
            return new ChannelOptions(cipherParams);
        }
    }
}
