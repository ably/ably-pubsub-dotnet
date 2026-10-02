using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Push
{
    /// <summary>
    /// Derived from uts/rest/unit/push/push_channel_subscriptions.md in ably/specification.
    ///
    /// Spec points: RSH1c1, RSH1c2, RSH1c3, RSH1c4, RSH1c5
    ///
    /// <para>
    /// The admin channel-subscriptions API, distinct from the per-device <c>PushChannel</c> covered
    /// by <see cref="PushChannelTests"/>. Three idiomatic shape differences: the filter is a
    /// <c>ListSubscriptionsRequest</c> rather than a dictionary, and since the spec's filters mix
    /// fields that its named factories do not pair, they are set on an <c>Empty()</c> request;
    /// <c>listChannels</c> is <c>ListChannelsAsync</c> and takes a <c>PaginatedRequestParams</c>;
    /// and the members live on <c>IPushChannelSubscriptions</c>, implemented explicitly by
    /// <c>PushAdmin</c>.
    /// </para>
    /// </summary>
    public class PushChannelSubscriptionsTests : UtsTestBase
    {
        public PushChannelSubscriptionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1c1/list-filtered-by-channel-0
        [Fact]
        public async Task RSH1c1_ListFilteredByChannel()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { channel = "my-channel", deviceId = "device-001" },
                        new { channel = "my-channel", clientId = "client-abc" },
                    });
                }));

            var filter = ListSubscriptionsRequest.Empty();
            filter.Channel = "my-channel";

            var result = await Subscriptions(client).ListAsync(filter);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["channel"].Should().Be("my-channel");

            result.Should().BeOfType<PaginatedResult<PushChannelSubscription>>();
            result.Items.Should().HaveCount(2);
            result.Items[0].Should().BeOfType<PushChannelSubscription>();
            result.Items[0].Channel.Should().Be("my-channel");
            result.Items[0].DeviceId.Should().Be("device-001");
            result.Items[1].ClientId.Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c1/list-filtered-by-device-client-1
        [Fact]
        public async Task RSH1c1_ListFilteredByDeviceAndClient()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { channel = "my-channel", deviceId = "device-001", clientId = "client-abc" },
                    });
                }));

            var filter = ListSubscriptionsRequest.Empty();
            filter.DeviceId = "device-001";
            filter.ClientId = "client-abc";

            var result = await Subscriptions(client).ListAsync(filter);

            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
            result.Items.Should().HaveCount(1);
        }

        // UTS: rest/unit/RSH1c1/list-with-limit-param-2
        [Fact]
        public async Task RSH1c1_ListWithLimitParam()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { channel = "my-channel", deviceId = "device-001" },
                    });
                }));

            await Subscriptions(client).ListAsync(ListSubscriptionsRequest.Empty(5));

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("5");
        }

        // UTS: rest/unit/RSH1c2/list-channels-paginated-0
        [Fact]
        public async Task RSH1c2_ListChannelsPaginated()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { "channel-1", "channel-2", "channel-3" });
                }));

            var result = await Subscriptions(client).ListChannelsAsync(PaginatedRequestParams.Empty);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/push/channels");

            result.Should().BeOfType<PaginatedResult<string>>();
            result.Items.Should().HaveCount(3);
            result.Items[0].Should().Be("channel-1");
            result.Items[1].Should().Be("channel-2");
            result.Items[2].Should().Be("channel-3");
        }

        // UTS: rest/unit/RSH1c2/list-channels-with-limit-1
        [Fact]
        public async Task RSH1c2_ListChannelsWithLimit()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { "channel-1" });
                }));

            var result = await Subscriptions(client)
                .ListChannelsAsync(new PaginatedRequestParams { Limit = 1 });

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1");
            result.Items.Should().HaveCount(1);
        }

        // UTS: rest/unit/RSH1c3/save-post-subscription-0
        [Fact]
        public async Task RSH1c3_SavePostsSubscription()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new { channel = "my-channel", deviceId = "device-001" });
                }));

            var result = await Subscriptions(client)
                .SaveAsync(PushChannelSubscription.ForDevice("my-channel", "device-001"));

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("POST");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(capturedRequests[0].BodyText);
            body["channel"].Value<string>().Should().Be("my-channel");
            body["deviceId"].Value<string>().Should().Be("device-001");

            result.Should().BeOfType<PushChannelSubscription>();
            result.Channel.Should().Be("my-channel");
            result.DeviceId.Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c3/save-updates-existing-1
        [Fact]
        public async Task RSH1c3_SaveUpdatesExisting()
        {
            var requestCount = 0;
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    req.RespondWith(200, new { channel = "my-channel", deviceId = "device-001" });
                }));

            var subscription = PushChannelSubscription.ForDevice("my-channel", "device-001");

            var result1 = await Subscriptions(client).SaveAsync(subscription);
            var result2 = await Subscriptions(client).SaveAsync(subscription);

            requestCount.Should().Be(2);
            result1.Channel.Should().Be("my-channel");
            result2.Channel.Should().Be("my-channel");
        }

        // UTS: rest/unit/RSH1c3/save-error-propagated-2
        [Fact]
        public async Task RSH1c3_SaveErrorPropagated()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(400, new
                {
                    error = new { code = 40000, statusCode = 400, message = "Invalid subscription" },
                })));

            Func<Task> act = () => Subscriptions(client)
                .SaveAsync(PushChannelSubscription.ForDevice("my-channel", "device-001"));

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int)error.StatusCode.Value).Should().Be(400);
        }

        // UTS: rest/unit/RSH1c4/remove-delete-clientid-0
        [Fact]
        public async Task RSH1c4_RemoveDeletesByClientId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Subscriptions(client)
                .RemoveAsync(PushChannelSubscription.ForClientId("my-channel", "client-abc"));

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["channel"].Should().Be("my-channel");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c4/remove-delete-deviceid-1
        [Fact]
        public async Task RSH1c4_RemoveDeletesByDeviceId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Subscriptions(client)
                .RemoveAsync(PushChannelSubscription.ForDevice("my-channel", "device-001"));

            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["channel"].Should().Be("my-channel");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c4/remove-nonexistent-succeeds-2
        [Fact]
        public async Task RSH1c4_RemoveNonexistentSucceeds()
        {
            var client = RestClient(NoContentMock(new List<PendingHttpRequest>()));

            Func<Task> act = () => Subscriptions(client)
                .RemoveAsync(PushChannelSubscription.ForDevice("my-channel", "nonexistent-device"));

            await act.Should().NotThrowAsync();
        }

        // UTS: rest/unit/RSH1c5/remove-where-clientid-0
        [Fact]
        public async Task RSH1c5_RemoveWhereClientId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Subscriptions(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "clientId", "client-abc" },
            });

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c5/remove-where-deviceid-1
        [Fact]
        public async Task RSH1c5_RemoveWhereDeviceId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Subscriptions(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "deviceId", "device-001" },
            });

            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c5/remove-where-no-match-succeeds-2
        [Fact]
        public async Task RSH1c5_RemoveWhereNoMatchSucceeds()
        {
            var client = RestClient(NoContentMock(new List<PendingHttpRequest>()));

            Func<Task> act = () => Subscriptions(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "clientId", "nonexistent-client" },
            });

            await act.Should().NotThrowAsync();
        }

        private static IPushChannelSubscriptions Subscriptions(PubSubHttpClient client)
            => client.Push.Admin.ChannelSubscriptions;

        private static MockHttpClient NoContentMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });
    }
}
