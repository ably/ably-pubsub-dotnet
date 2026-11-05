using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Push;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Push
{
    public class PushAdminFcmTopicsTests : MockHttpRestSpecs
    {
        private readonly List<AblyRequest> _requests = new List<AblyRequest>();

        public PushAdminFcmTopicsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private AblyRequest Request => _requests.Single();

        private PushAdminFcmTopics FcmTopics(string responseText = "{}", HttpStatusCode status = HttpStatusCode.OK)
        {
            var rest = GetRestClient(request =>
            {
                _requests.Add(request);
                return Task.FromResult(new AblyResponse { StatusCode = status, TextResponse = responseText });
            });
            return rest.Push.Admin.FcmTopics;
        }

        [Fact]
        public async Task Subscribe_ShouldPostToDeviceSubscriptionPath()
        {
            await FcmTopics().Get("team-manutd").SubscribeAsync("device-1");

            Request.Method.Should().Be(HttpMethod.Post);
            Request.Url.Should().Be("/push/fcmTopics/team-manutd/subscriptions/device-1");
        }

        [Fact]
        public async Task Unsubscribe_ShouldDeleteDeviceSubscriptionPath()
        {
            await FcmTopics(string.Empty, HttpStatusCode.NoContent).Get("team-manutd").UnsubscribeAsync("device-1");

            Request.Method.Should().Be(HttpMethod.Delete);
            Request.Url.Should().Be("/push/fcmTopics/team-manutd/subscriptions/device-1");
        }

        [Fact]
        public async Task TopicAndDeviceId_ShouldBePercentEncodedInPath()
        {
            // FCM topic names may contain %, which the server requires sent as %25.
            await FcmTopics().Get("50%off").SubscribeAsync("device/1");

            Request.Url.Should().Be("/push/fcmTopics/50%25off/subscriptions/device%2F1");
        }

        [Fact]
        public async Task SubscribeClient_ShouldPostClientIdToTopicSubscriptions()
        {
            await FcmTopics("{\"topic\":\"team-manutd\"}", HttpStatusCode.Accepted).Get("team-manutd").SubscribeClientAsync("client-1");

            Request.Method.Should().Be(HttpMethod.Post);
            Request.Url.Should().Be("/push/fcmTopics/team-manutd/subscriptions");
            ((JObject)Request.PostData).Should().BeEquivalentTo(new JObject { ["clientId"] = "client-1" });
        }

        [Fact]
        public async Task SubscribeChannels_ShouldPostChannelsToTopicSubscriptions()
        {
            await FcmTopics("{\"topic\":\"team-manutd\"}", HttpStatusCode.Accepted).Get("team-manutd").SubscribeChannelsAsync(new[] { "a", "b" });

            Request.Url.Should().Be("/push/fcmTopics/team-manutd/subscriptions");
            ((JObject)Request.PostData).Should().BeEquivalentTo(new JObject { ["channels"] = new JArray("a", "b") });
        }

        [Fact]
        public async Task UnsubscribeClient_ShouldDeleteTopicSubscriptionsWithClientIdParam()
        {
            await FcmTopics("{\"topic\":\"team-manutd\"}", HttpStatusCode.Accepted).Get("team-manutd").UnsubscribeClientAsync("client-1");

            Request.Method.Should().Be(HttpMethod.Delete);
            Request.Url.Should().Be("/push/fcmTopics/team-manutd/subscriptions");
            Request.QueryParameters.Should().ContainKey("clientId").WhoseValue.Should().Be("client-1");
        }

        [Fact]
        public async Task List_ShouldGetDeviceTopics()
        {
            var topics = await FcmTopics("[\"team-manutd\",\"alerts-goals\"]").ListAsync("device-1");

            Request.Method.Should().Be(HttpMethod.Get);
            Request.Url.Should().Be("/push/fcmTopics");
            Request.QueryParameters.Should().ContainKey("deviceId").WhoseValue.Should().Be("device-1");
            topics.Should().Equal("team-manutd", "alerts-goals");
        }

        [Fact]
        public async Task Publish_ShouldPostPayloadToTopicMessagesAndReturnMessageId()
        {
            var payload = JObject.FromObject(new
            {
                data = new { homeScore = "2" },
                android = new { ttl = "120s" },
            });

            var messageId = await FcmTopics("{\"topic\":\"team-manutd\",\"messageId\":\"projects/p/messages/1\"}")
                .PublishAsync("team-manutd", payload);

            Request.Method.Should().Be(HttpMethod.Post);
            Request.Url.Should().Be("/push/fcmTopics/team-manutd/messages");
            ((JObject)Request.PostData).Should().BeEquivalentTo(payload);
            messageId.Should().Be("projects/p/messages/1");
        }

        [Fact]
        public async Task EmptyArguments_ShouldThrowWithoutMakingRequest()
        {
            var fcmTopics = FcmTopics();

            Assert.Throws<AblyException>(() => fcmTopics.Get(string.Empty));
            await Assert.ThrowsAsync<AblyException>(() => fcmTopics.Get("t").SubscribeAsync(string.Empty));
            await Assert.ThrowsAsync<AblyException>(() => fcmTopics.Get("t").SubscribeChannelsAsync(new string[0]));
            await Assert.ThrowsAsync<AblyException>(() => fcmTopics.PublishAsync("t", new JObject()));
            await Assert.ThrowsAsync<AblyException>(() => fcmTopics.ListAsync(null));

            _requests.Should().BeEmpty();
        }

        [Fact]
        public void Get_WithInvalidTopicName_ShouldThrow()
        {
            var fcmTopics = FcmTopics();

            Assert.Throws<AblyException>(() => fcmTopics.Get("/topics/news"));
            Assert.Throws<AblyException>(() => fcmTopics.Get("has space"));
            Assert.Throws<AblyException>(() => fcmTopics.Get(new string('a', 901)));
            fcmTopics.Get(new string('a', 900)).Name.Should().HaveLength(900);
        }

        [Fact]
        public async Task InvalidArguments_ShouldFaultTheTaskRatherThanThrowSynchronously()
        {
            var fcmTopics = FcmTopics();
            var topic = fcmTopics.Get("t");

            var tasks = new List<Task>
            {
                topic.SubscribeClientAsync(string.Empty),
                topic.SubscribeChannelsAsync(new string[0]),
                topic.SubscribeChannelsAsync(new[] { "a", null }),
                topic.UnsubscribeClientAsync(null),
                topic.PublishAsync(null),
                fcmTopics.PublishAsync("/topics/t", new JObject { ["data"] = new JObject() }),
            };

            foreach (var task in tasks)
            {
                await Assert.ThrowsAsync<AblyException>(() => task);
            }

            _requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Publish_WithNonStringResponseFields_ShouldStillReturnMessageId()
        {
            var messageId = await FcmTopics("{\"topic\":\"t\",\"messageId\":\"m1\",\"fcm\":{\"a\":1},\"count\":3}")
                .PublishAsync("t", new JObject { ["data"] = new JObject { ["k"] = "v" } });

            messageId.Should().Be("m1");
        }

        [Fact]
        public async Task WithPushAdminFullWait_ShouldAddFullWaitToMutatingRequests()
        {
            var rest = GetRestClient(
                request =>
                {
                    _requests.Add(request);
                    return Task.FromResult(new AblyResponse { StatusCode = HttpStatusCode.OK, TextResponse = "{}" });
                },
                options => options.PushAdminFullWait = true);
            var topic = rest.Push.Admin.FcmTopics.Get("t");

            await topic.SubscribeAsync("device-1");
            await topic.UnsubscribeAsync("device-1");
            await topic.SubscribeClientAsync("client-1");
            await topic.SubscribeChannelsAsync(new[] { "c" });
            await topic.UnsubscribeClientAsync("client-1");
            await topic.PublishAsync(new JObject { ["data"] = new JObject { ["k"] = "v" } });

            _requests.Should().HaveCount(6);
            _requests.Should().OnlyContain(r => r.QueryParameters.ContainsKey("fullWait") && r.QueryParameters["fullWait"] == "true");
        }
    }
}
