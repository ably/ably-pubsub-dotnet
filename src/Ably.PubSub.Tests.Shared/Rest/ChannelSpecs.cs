using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using IO.Ably.Encryption;
using IO.Ably.Rest;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Rest
{
    public class ChannelSpecs : MockHttpRestSpecs
    {
        public class General : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RSN1")]
            public void ChannelsIsACollectionOfChannelObjects()
            {
                var client = GetRestClient();
                client.Channels.Should().BeAssignableTo<IEnumerable<IRestChannel>>();
            }

            [Fact]
            [Trait("spec", "RSN2")]
            public void ShouldBeAbleToIterateThroughExistingChannels()
            {
                var client = GetRestClient();
                var channel1 = client.Channels.Get("test");
                var channel2 = client.Channels.Get("test1");
                _ = client.Channels.Get("test2");
                var channel4 = client.Channels.Get("test3");
                var channel5 = client.Channels.Get("test4");
                var channel6 = client.Channels.Get("test5");
                client.Channels.Release("test2");
                var channel7 = client.Channels.Get("test7");

                client.Channels.Should().BeEquivalentTo(new[] { channel1, channel2, channel4, channel5, channel6, channel7 });
            }

            [Fact]
            public async Task ShouldUpdateOptionsWhenTwoThreadsTryToCreateTheSameChannelWithDifferentOptions()
            {
                var client = GetRestClient();
                var options = new ChannelOptions(encrypted: true);
                var task1 = Task.Run(() => client.Channels.Get("test"));
                var task2 = Task.Run(() => client.Channels.Get("test", options));

                await Task.WhenAll(task1, task2);
                var channel2 = (RestChannel)client.Channels.Get("test");
                channel2.Options.Should().BeSameAs(options);
            }

            [Fact]
            [Trait("spec", "RSN2")]
            public void ShouldBeAbleToCheckIsAChannelExists()
            {
                var client = GetRestClient();
                _ = client.Channels.Get("test");
                _ = client.Channels.Get("test1");

                client.Channels.Any(x => x.Name == "test").Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSN4a")]
            public void ShouldBeAbleToReleaseAChannelSoItIsRemovedFromTheChannelsCollection()
            {
                var client = GetRestClient();
                _ = client.Channels.Get("first");
                client.Channels.Should().Contain(x => x.Name == "first");
                client.Channels.Release("first");
                client.Channels.Should().BeEmpty();
            }

            public General(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RSN3")]
        public class GettingAChannel : ChannelSpecs
        {
            private readonly AblyRest _client;

            public GettingAChannel(ITestOutputHelper output)
                : base(output)
            {
                _client = GetRestClient();
            }

            [Fact]
            [Trait("spec", "RSN3a")]
            public void WhenChannelDoesNotExist_ShouldCreateANewOne()
            {
                var channel = _client.Channels.Get("new");
                channel.Should().NotBeNull();
            }

            [Fact]
            [Trait("spec", "RSN3a")]
            public void WhenChannelAlreadyExists_ShouldReturnExistingChannel()
            {
                var channel = _client.Channels.Get("new");
                var secondTime = _client.Channels.Get("new");
                channel.Should().BeSameAs(secondTime);
            }

            [Fact]
            [Trait("spec", "RSN3a")]
            [Trait("spec", "RSN3b")]
            public void WithProvidedChannelOptions_ShouldSetOptionsOnChannel()
            {
                var options = new ChannelOptions();
                var channel = _client.Channels.Get("test", options);
                ((RestChannel)channel).Options.Should().BeEquivalentTo(options);
            }

            [Fact]
            [Trait("spec", "RSN3c")]
            public void WhenAccessingExistingChannel_WithNewOptions_ShouldUpdateExistingChannelWithNewOptions()
            {
                _ = _client.Channels.Get("test");
                var newOptions = new ChannelOptions(true);
                var secondTime = _client.Channels.Get("test", newOptions);
                ((RestChannel)secondTime).Options.Should().BeEquivalentTo(newOptions);
            }
        }

        public class ChannelPublish : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RSL1a")]
            [Trait("spec", "RSL1b")]
            public void WithNameAndData_CreatesASinglePostRequestWithValidPath()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                channel.PublishAsync("event", "data");

                LastRequest.Method.Should().Be(HttpMethod.Post);
                LastRequest.Url.Should().Be($"/channels/{channel.Name}/messages");
                var messages = LastRequest.PostData as IEnumerable<Message>;
                messages.Should().HaveCount(1);
                messages.First().Data.Should().Be("data");
                messages.First().Name.Should().Be("event");
                Requests.Should().HaveCount(1);
            }

            [Fact]
            [Trait("spec", "RSL1a")]
            [Trait("spec", "RSL1c")]
            public void WithMessagesList_CreatesOnePostRequestToMessagesRoute()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                var message = new Message { Name = "event", Data = "data" };
                var message1 = new Message { Name = "event1", Data = "data" };
                var message2 = new Message { Name = "event2", Data = "data" };
                channel.PublishAsync(new List<Message> { message, message1, message2 });

                Requests.Count.Should().Be(1);

                LastRequest.Method.Should().Be(HttpMethod.Post);
                LastRequest.Url.Should().Be($"/channels/{channel.Name}/messages");
                var postedMessages = LastRequest.PostData as List<Message>;
                postedMessages.Should().HaveCount(3);
                postedMessages.Should().BeEquivalentTo(new[] { message, message1, message2 });
            }

            [Fact]
            [Trait("spec", "RSL1e")]
            public async Task WithNoData_ShouldOnlySendNameProperty()
            {
                var client = GetRestClient(null, options =>
                {
                    // Idempotent publishing will add an id to the message, so disable for this test
                    options.IdempotentRestPublishing = false;
                });

                var messageWithNoData = new Message { Name = "NoData" };
                await client.Channels.Get("nodata").PublishAsync(messageWithNoData);

                LastRequest.RequestBody.GetText().Should().Be("[{\"name\":\"NoData\"}]");
            }

            [Fact]
            [Trait("spec", "RSL1e")]
            public async Task WithNoName_ShouldOnlySendDataProperty()
            {
                var client = GetRestClient(null, options =>
                {
                    // Idempotent publishing will add an id to the message, so disable for this test
                    options.IdempotentRestPublishing = false;
                });

                var messageWithNoName = new Message { Data = "NoName" };
                await client.Channels.Get("noname").PublishAsync(messageWithNoName);

                LastRequest.RequestBody.GetText().Should().Be("[{\"data\":\"NoName\"}]");
            }

            [Fact]
            [Trait("spec", "RSL1e")]
            public async Task WithBlankMessage_ShouldSendBlankMessage()
            {
                var client = GetRestClient(null, options =>
                {
                    // Idempotent publishing will add an id to the message, so disable for this test
                    options.IdempotentRestPublishing = false;
                });

                var messageWithNoName = new Message();
                await client.Channels.Get("blank-message").PublishAsync(messageWithNoName);

                LastRequest.RequestBody.GetText().Should().Be("[{}]");
            }

            [Fact]
            [Trait("spec", "RSL1e")]
            public async Task WithNoNameAndMsgPack_ShouldOnlySendDataProperty()
            {
                var client = GetRestClient(null, opts => opts.UseBinaryProtocol = true);

                var messageWithNoName = new Message { Data = "NoName" };
                await client.Channels.Get("noname").PublishAsync(messageWithNoName);
            }

            [Fact]
            public void Publish_WithNameAndData_AddsPayloadToRequest()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                channel.PublishAsync("event", "data");

                var messages = LastRequest.PostData as IEnumerable<Message>;
                var data = messages.First();
                data.Data.Should().Be("data");
                data.Name.Should().Be("event");
            }

            [Fact]
            public void Publish_WithBinaryArrayData_AddsBase64EncodingToRequest()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                channel.PublishAsync("event", new byte[] { 1, 2 });

                var postData = (LastRequest.PostData as IEnumerable<Message>).First();
                postData.Encoding.Should().Be("base64");
            }

            [Fact]
            public void Publish_WithOneMessage_AddsPayloadToRequest()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");

                var message = new Message { Name = "event", Data = "data" };
                channel.PublishAsync(new List<Message> { message });

                var data = LastRequest.PostData as IEnumerable<Message>;
                data.Should().NotBeNull();

                data.Count().Should().Be(1);
                var payloadMessage = data.First();
                payloadMessage.Data.Should().Be("data");
                payloadMessage.Name.Should().Be("event");
            }

            public ChannelPublish(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class ChannelHistory : ChannelSpecs
        {
            private readonly IRestChannel _channel;
            private AblyRest _client;

            [Fact]
            [Trait("spec", "RSL2a")]
            public async Task WithNoOptions_CreateGetRequestWithValidPath()
            {
                var result = await _channel.HistoryAsync();

                result.Should().BeOfType<PaginatedResult<Message>>();
                LastRequest.Method.Should().Be(HttpMethod.Get);
                LastRequest.Url.Should().Be($"/channels/{_channel.Name}/messages");
            }

            [Fact]
            [Trait("spec", "RSL2b")]
            public async Task WithOptions_AddsParametersToRequest()
            {
                var query = new PaginatedRequestParams();
                var now = DateTimeOffset.Now;
                query.Start = now.AddHours(-1);
                query.End = now;
                query.Direction = QueryDirection.Forwards;
                query.Limit = 1000;
                await _channel.HistoryAsync(query);

                LastRequest.AssertContainsParameter("start", query.Start.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("end", query.End.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("direction", query.Direction.ToString().ToLower());
                LastRequest.AssertContainsParameter("limit", query.Limit.Value.ToString());
            }

            [Fact]
            [Trait("spec", "RSL2b")]
            public async Task WithOptions_AddsParametersToRequest_UsingCompatHistoryParams()
            {
                /*
                 * In a breaking change in the 1.1 release HistoryRequestParams was replaced with PaginatedRequestParams.
                 * To fix this backward compatibility issue a new HistoryRequestParams class the inherits from PaginatedRequestParams
                 * has been created.
                 *
                 * This test demonstrates that HistoryRequestParams can be used to call
                 * HistoryAsync (which accepts a PaginatedRequestParams instance as a parameter.
                 */

#pragma warning disable CS0618 // Type or member is obsolete
                var query = new HistoryRequestParams();
#pragma warning restore CS0618 // Type or member is obsolete
                var now = DateTimeOffset.Now;
                query.Start = now.AddHours(-1);
                query.End = now;
                query.Direction = QueryDirection.Forwards;
                query.Limit = 1000;
                await _channel.HistoryAsync(query);

                LastRequest.AssertContainsParameter("start", query.Start.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("end", query.End.Value.ToUnixTimeInMilliseconds().ToString());
                LastRequest.AssertContainsParameter("direction", query.Direction.ToString().ToLower());
                LastRequest.AssertContainsParameter("limit", query.Limit.Value.ToString());
            }

            [Fact]
            [Trait("spec", "RSL2b")]
            public async Task WithStartBeforeEnd_Throws()
            {
                _ = await Assert.ThrowsAsync<AblyException>(() =>
                        _channel.HistoryAsync(new PaginatedRequestParams { Start = Now, End = Now.AddHours(-1) }));
            }

            [Fact]
            [Trait("spec", "RSL2b2")]
            public async Task WithoutDirection_ShouldDefaultToBackwards()
            {
                await _channel.HistoryAsync();

                LastRequest.AssertContainsParameter("direction", QueryDirection.Backwards.ToString().ToLower());
            }

            [Fact]
            [Trait("spec", "RSL2b3")]
            public async Task WithOutLimit_ShouldUseDefaultOf100()
            {
                await _channel.HistoryAsync();

                LastRequest.AssertContainsParameter("limit", "100");
            }

            [Theory]
            [InlineData(-1)]
            [InlineData(1001)]
            [Trait("spec", "RSL2b3")]
            [Trait("spec", "RSP3a1")]
            public async Task WithLimitLessThan0andMoreThan1000_ShouldThrow(int limit)
            {
                _ = await
                    Assert.ThrowsAsync<AblyException>(() => _channel.HistoryAsync(new PaginatedRequestParams { Limit = limit }));
            }

            [Fact]
            public async Task History_WithInvalidStartOrEnd_Throws()
            {
                var rest = GetRestClient();
                var channel = rest.Channels.Get("Test");
                foreach (object[] dates in InvalidHistoryDates())
                {
                    var query = new PaginatedRequestParams { Start = (DateTimeOffset?)dates.First(), End = (DateTimeOffset)dates.Last() };

                    _ = await Assert.ThrowsAsync<AblyException>(async () => await channel.HistoryAsync(query));
                }
            }

            private static IEnumerable<object[]> InvalidHistoryDates()
            {
                yield return new object[] { new DateTimeOffset(1969, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Now };
                yield return new object[] { new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero) };
                yield return new object[] { null, new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero) };
            }

            [Fact]
            public async Task History_WithPartialResult_ReturnsCorrectFirstCurrentAndNextLinks()
            {
                // Arrange
                var rest = GetRestClient(request => new AblyResponse
                {
                    Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                    TextResponse = "[]"
                }.ToTask());

                var channel = rest.Channels.Get("test");

                // Act
                var result = await channel.HistoryAsync();

                // Assert
                result.NextQueryParams.Should().NotBeNull();
                result.CurrentQueryParams.Should().NotBeNull();
                result.FirstQueryParams.Should().NotBeNull();
            }

            [Fact]
            public async Task History_ForAnEncryptedChannel_DecryptsMessagesBeforeReturningThem()
            {
                // Arrange
                var rest = GetRestClient();
                var message = new Message { Name = "test", Data = "Test" };
                var defaultParams = Crypto.GetDefaultParams();

                rest.ExecuteHttpRequest = request =>
                {
                    var response = new AblyResponse
                    {
                        Headers = DataRequestQueryTests.GetSampleHistoryRequestHeaders(),
                        TextResponse = $"[{JsonHelper.Serialize(message)}]"
                    };
                    return response.ToTask();
                };

                var channel = rest.Channels.Get("test", new ChannelOptions(defaultParams));

                // Act
                var result = await channel.HistoryAsync();

                // Assert
                Assert.NotEmpty(result.Items);
                var firstMessage = result.Items.First();
                firstMessage.Data.Should().Be(message.Data);
            }

            public ChannelHistory(ITestOutputHelper output)
                : base(output)
            {
                _client = GetRestClient();
                _channel = _client.Channels.Get("test");
            }
        }

        public ChannelSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
