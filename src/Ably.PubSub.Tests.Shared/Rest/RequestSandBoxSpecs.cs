using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RichardSzalay.MockHttp;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    [Trait("type", "integration")]
    [Trait("spec", "RSC19")]
    [Trait("spec", "RSC19a")]
    [Trait("spec", "RSC19b")]
    [Trait("spec", "RSC19c")]
    [Trait("spec", "RSC19d")]
    [Trait("spec", "RSC19e")]
    public class RequestSandBoxSpecs : SandboxSpecs, IAsyncLifetime
    {
        private readonly string _channelName;
        private readonly string _channelAltName;
        private readonly string _channelNamePrefix;
        private readonly string _channelPath;
        private readonly string _channelsPath;
        private readonly string _channelMessagesPath;

        private AblyRequest _lastRequest;

        public RequestSandBoxSpecs(ITestOutputHelper output)
            : base(new AblySandboxFixture(), output)
        {
            _channelNamePrefix = "rest_request".AddRandomSuffix();
            _channelName = $"{_channelNamePrefix}_channel";
            _channelAltName = $"{_channelNamePrefix}_alt_channel";
            _channelsPath = "/channels";
            _channelPath = $"{_channelsPath}/{_channelName}";
            _channelMessagesPath = $"{_channelPath}/messages";
        }

        public async Task InitializeAsync()
        {
            var client = await GetRestClient(Protocol.Json);
            var channel = client.Channels.Get(_channelName);
            for (int i = 0; i < 4; i++)
            {
                channel.Publish("Test event", "Test data " + i);
            }

            var altChannel = client.Channels.Get(_channelAltName);
            for (int i = 0; i < 4; i++)
            {
                altChannel.Publish("Test event", "Test alt data " + i);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Trait("spec", "RSC19a")]
        [Theory]
        [ProtocolData]
        public async Task Request_ShouldAcceptCorrectHttpVerbs(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));
            client.HttpClient.SetPreferredHost("localhost");
            var mockHttp = new MockHttpMessageHandler();

            var verbs = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };

            foreach (var verb in verbs)
            {
                mockHttp.When(new HttpMethod(verb), "https://localhost/*").Respond(HttpStatusCode.OK, "application/json", "{ \"verb\": \"" + verb + "\" }");
                client.HttpClient.Client = mockHttp.ToHttpClient();

                var response = await client.RequestV2(verb, "/");
                response.Success.Should().BeTrue($"'{verb}' verb should be supported ");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Items.First()["verb"].ToString().Should().Be(verb);

                var failedResponse = await client.RequestV2("INVALID_HTTP_VERB", "/");
                failedResponse.Success.Should().BeFalse($"'INVALID_HTTP_VERB' should fail because '{verb}' verb is expected by mock HTTP server.");
            }
        }

        [Trait("spec", "RSC19")]
        [Trait("spec", "RSC19a")]
        [Trait("spec", "RSC19b")]
        [Trait("spec", "RSC19c")]
        [Trait("spec", "RSC19d")]
        [Theory]
        [ProtocolData]
        public async Task Request_SimpleGet(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));

            var testParams = new Dictionary<string, string> { { "testParams", "testParamValue" } };
            var testHeaders = new Dictionary<string, string> { { "X-Test-Header", "testHeaderValue" } };

            var paginatedResponse = await client.RequestV2(HttpMethod.Get.Method, _channelPath, testParams, null, testHeaders);

            _lastRequest.Headers.Should().ContainKey("Authorization");
            _lastRequest.Headers.Should().ContainKey("X-Test-Header");
            _lastRequest.Headers["X-Test-Header"].Should().Be("testHeaderValue");
            paginatedResponse.Should().NotBeNull();
            paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK); // 200
            paginatedResponse.Success.Should().BeTrue();
            paginatedResponse.ErrorCode.Should().Be(0);
            paginatedResponse.ErrorMessage.Should().BeNull();
            paginatedResponse.Items.Should().HaveCount(1);
            paginatedResponse.Items.First().Should().BeOfType<JObject>();
            paginatedResponse.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));
            var channelDetails = paginatedResponse.Items.First() as JObject; // cast from JToken
            channelDetails["channelId"].ToString().Should().BeEquivalentTo(_channelName);
        }

        [Trait("spec", "RSC19")]
        [Trait("docUrl", "https://ably.com/docs/api/rest-api#batch-publish")]
        [Theory]
        [ProtocolData]
        public async Task BatchMessagePublishOnMultipleChannelsUsingHttpPostRequest(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));

            void ValidateResponse(HttpPaginatedResponse paginatedResponse, int channelCounter = 1)
            {
                _lastRequest.Headers.Should().ContainKey("Authorization");
                paginatedResponse.Should().NotBeNull();
                paginatedResponse.StatusCode.Should().Be(HttpStatusCode.Created); // 201
                paginatedResponse.Success.Should().BeTrue();
                paginatedResponse.ErrorCode.Should().Be(0);
                paginatedResponse.ErrorMessage.Should().BeNull();
                paginatedResponse.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));

                paginatedResponse.Items.Should().HaveCount(4);
                foreach (var item in paginatedResponse.Items)
                {
                    item["channel"].ToString().Should().Be($"channel{channelCounter++}");
                }
            }

            async Task ValidateMessages(string channelName, Action<Message, int> validate)
            {
                var paginatedResult = await client.Channels.Get(channelName).HistoryAsync();
                var counter = 0;
                // No need to iterate pages since result wont exceed 100.
                foreach (var message in paginatedResult.Items)
                {
                    validate(message, paginatedResult.Items.Count - counter++);
                }
            }

            // Publish single message
            var objectPayload = new
            {
                channels = new[] { "channel1", "channel2", "channel3", "channel4" },
                messages = new
                {
                    id = "1",
                    data = "foo",
                }
            };
            var paginatedResponse = await client.Request(HttpMethod.Post, "/messages", null, JToken.FromObject(objectPayload), null);

            ValidateResponse(paginatedResponse);

            foreach (var channel in new[] { "channel1", "channel2", "channel3", "channel4" })
            {
                await ValidateMessages(channel, (message, messageIndex) =>
                {
                    message.Id.Should().Be("1");
                    message.Data.Should().Be("foo");
                });
            }

            // Publish multiple messages
            var jsonPayload =
                @"{
                    ""channels"" : [ ""channel5"", ""channel6"",""channel7"", ""channel8"" ],
                    ""messages"" : [
                        {
                            ""data"" : ""message1"",
                        },
                        {
                            ""name"": ""eventName"",
                            ""data"" : ""message2"",
                        }
                    ]
                  }";
            paginatedResponse = await client.RequestV2(HttpMethod.Post.Method, "/messages", null, jsonPayload, null);

            ValidateResponse(paginatedResponse, 5);

            foreach (var channel in new[] { "channel5", "channel6", "channel7", "channel8" })
            {
                await ValidateMessages(channel, (message, messageIndex) =>
                {
                    message.Data.Should().Be($"message{messageIndex}");
                    if (messageIndex == 2)
                    {
                        message.Name.Should().Be("eventName");
                    }
                });
            }
        }

        [Trait("spec", "RSC19")]
        [Trait("spec", "RSC19a")]
        [Trait("spec", "RSC19b")]
        [Trait("spec", "RSC19c")]
        [Trait("spec", "RSC19d")]
        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        public async Task Request_Paginated(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));

            var testParams = new Dictionary<string, string> { { "prefix", _channelNamePrefix } };

            var paginatedResponse = await client.Request(HttpMethod.Get, _channelsPath, testParams);

            _lastRequest.Headers.Should().ContainKey("Authorization");
            paginatedResponse.Should().NotBeNull();
            paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK); // 200
            paginatedResponse.Success.Should().BeTrue();
            paginatedResponse.ErrorCode.Should().Be(0);
            paginatedResponse.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));
            var items = paginatedResponse.Items;
            items.Should().HaveCount(2);
            foreach (var item in items)
            {
                (item as JObject)["channelId"].ToString().Should().StartWith(_channelNamePrefix);
            }
        }

        [Trait("spec", "RSC19")]
        [Trait("spec", "RSC19a")]
        [Trait("spec", "RSC19b")]
        [Trait("spec", "RSC19c")]
        [Trait("spec", "RSC19d")]
        [Theory]
        [ProtocolData]
        public async Task Request_PaginatedWithLimit(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));

            var testParams = new Dictionary<string, string> { { "prefix", _channelNamePrefix }, { "limit", "1" } };

            var paginatedResponse = await client.Request(HttpMethod.Get, _channelsPath, testParams);

            _lastRequest.Headers.Should().ContainKey("Authorization");
            paginatedResponse.Should().NotBeNull();
            paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK); // 200
            paginatedResponse.Success.Should().BeTrue();
            paginatedResponse.ErrorCode.Should().Be(0);
            paginatedResponse.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));
            var items = paginatedResponse.Items;
            items.Should().HaveCount(1);
            foreach (var item in items)
            {
                (item as JObject)["channelId"].ToString().Should().StartWith(_channelNamePrefix);
            }

            var page2 = await paginatedResponse.NextAsync();
            page2.Items.Should().HaveCount(1);
            page2.StatusCode.Should().Be(HttpStatusCode.OK); // 200
            page2.Success.Should().BeTrue();
            page2.ErrorCode.Should().Be(0);
            page2.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));

            // show that the 2 pages are different
            var item1 = items[0] as JObject;
            var item2 = page2.Items[0] as JObject;
            item1["channelId"].ToString().Should().NotBe(item2["channelId"].ToString());
        }

        [Trait("spec", "RSC19")]
        [Trait("spec", "RSC19a")]
        [Trait("spec", "RSC19b")]
        [Trait("spec", "RSC19c")]
        [Trait("spec", "RSC19d")]
        [Theory]
        [ProtocolData]
        public async Task Request_Post(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));

            var body1 = JToken.Parse("{ \"name\": \"rsc19test\", \"data\": \"from-json-string\" }");
            var body2 = JToken.FromObject(new Message("rsc19test", "from-message"));

            var paginatedResponse = await client.Request(HttpMethod.Post, _channelMessagesPath, null, body1);

            _lastRequest.Headers.Should().ContainKey("Authorization");
            paginatedResponse.Should().NotBeNull();
            paginatedResponse.StatusCode.Should().Be(HttpStatusCode.Created); // 201
            paginatedResponse.Success.Should().BeTrue();
            paginatedResponse.ErrorCode.Should().Be(0);
            paginatedResponse.ErrorMessage.Should().BeNull();
            paginatedResponse.Response.ContentType.Should().Be(AblyHttpClient.GetHeaderValue(protocol));

            await client.Request(HttpMethod.Post, _channelMessagesPath, null, body2);

            var ch = client.Channels.Get(_channelName);
            var body3 = new Message("rsc19test", "from-publish");
            ch.Publish(body3);

            await Task.Delay(1000);

            var paginatedResult = client.Channels.Get(_channelName).History(new PaginatedRequestParams { Limit = 3 });
            paginatedResult.Should().NotBeNull();
            paginatedResult.Items.Should().HaveCount(3);
            paginatedResult.Items[2].Data.Should().BeEquivalentTo("from-json-string");
            paginatedResult.Items[1].Data.Should().BeEquivalentTo("from-message");
            paginatedResult.Items[0].Data.Should().BeEquivalentTo("from-publish");
        }

        [Trait("spec", "RSC19e")]
        [Theory]
        [ProtocolData]
        public async Task RequestFails_NotFound(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol));
            var response = await client.Request(HttpMethod.Post, "/does-not-exist");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Success.Should().BeFalse();
            response.ErrorCode.Should().Be(ErrorCodes.NotFound);
            response.ErrorMessage.Should().NotBeNullOrEmpty();
            response.Response.ContentType.Should().Be("application/json");
        }

        [Trait("spec", "RSC19e")]
        [Theory]
        [ProtocolData]
        public async Task RequestFails_ErrorConnecting(Protocol protocol)
        {
            var client = TrackLastRequest(await GetRestClient(protocol, options =>
            {
                options.HttpMaxRetryCount = 1;
                options.HttpOpenTimeout = TimeSpan.FromSeconds(1);
                options.HttpRequestTimeout = TimeSpan.FromSeconds(1);
            }));

            // custom host with a port that is not in use speeds up the test
            client.HttpClient.SetPreferredHost("fake.host:54321");
            try
            {
                await client.Request(HttpMethod.Post, "/");
            }
            catch (AblyException e)
            {
                e.ErrorInfo.Code.Should().Be(ErrorCodes.InternalError);
                e.ErrorInfo.Message.Should().NotBeNullOrEmpty();
                e.ErrorInfo.Message.Should().Contain("Invalid URI: Invalid port specified.");
            }
        }

        [Trait("spec", "RSC19e")]
        [Theory]
        [ProtocolData]
        public async Task RequestFails_Non200StatusResponseShouldNotRaiseException(Protocol protocol)
        {
            /*
             * This test is to show that non success HTTP status codes do not
             * cause the underlying HTTP client to throw an exception
             * (Which is the default behaviour for System.Net.HttpClient)
             */
            var client = TrackLastRequest(await GetRestClient(protocol, options =>
            {
                options.HttpMaxRetryCount = 1;
                options.HttpOpenTimeout = TimeSpan.FromSeconds(1);
            }));

            client.HttpClient.SetPreferredHost("echo.ably.io/respondwith?status=400");
            var response = await client.RequestV2(HttpMethod.Post.Method, "/");
            response.Success.Should().BeFalse();
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // because we are using the echo server there is no error code or message
            response.ErrorCode.Should().Be(0);
            response.ErrorMessage.Should().BeNull();
        }

        private AblyRest TrackLastRequest(AblyRest client)
        {
            var exec = client.ExecuteHttpRequest;
            client.ExecuteHttpRequest = request =>
            {
                _lastRequest = request;
                return exec(request);
            };
            return client;
        }
    }
}
