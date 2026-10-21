// PushChannelSubscriptions Tests (RSH1c, RSH1c1, RSH1c2, RSH1c3, RSH1c4, RSH1c5)
// Derived from uts/rest/unit/push/push_channel_subscriptions.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class PushChannelSubscriptionsTests : UtsUnitTestBase
    {
        public PushChannelSubscriptionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1c1/list-filtered-by-channel-0
        [UtsFact]
        [Trait("spec", "RSH1c1")]
        public async Task RSH1c1_ListFilteredByChannel()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["channel"] = "my-channel",
                            ["deviceId"] = "device-001",
                        },
                        new JObject
                        {
                            ["channel"] = "my-channel",
                            ["clientId"] = "client-abc",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var listRequest = ListSubscriptionsRequest.Empty();
            listRequest.Channel = "my-channel";
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(listRequest);

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be("my-channel");

            // ASSERT result IS PaginatedResult: guaranteed at compile time (ListAsync returns PaginatedResult<PushChannelSubscription>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(2);
            // ASSERT result.items[0] IS PushChannelSubscription: guaranteed at compile time (Items is List<PushChannelSubscription>)
            result.Items[0].Channel.Should().Be("my-channel");
            result.Items[0].DeviceId.Should().Be("device-001");
            result.Items[1].ClientId.Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c1/list-filtered-by-device-client-1
        [UtsFact]
        [Trait("spec", "RSH1c1")]
        public async Task RSH1c1_ListFilteredByDeviceClient()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["channel"] = "notifications",
                            ["deviceId"] = "device-001",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var listRequest = ListSubscriptionsRequest.Empty();
            listRequest.DeviceId = "device-001";
            listRequest.ClientId = "client-abc";
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(listRequest);

            // Assertions
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
            result.Items.Should().HaveCount(1);
        }

        // UTS: rest/unit/RSH1c1/list-with-limit-param-2
        [UtsFact]
        [Trait("spec", "RSH1c1")]
        public async Task RSH1c1_ListWithLimitParam()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["channel"] = "ch-1",
                            ["deviceId"] = "device-001",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.Empty(5));

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("5");
        }

        // UTS: rest/unit/RSH1c2/list-channels-paginated-0
        [UtsFact]
        [Trait("spec", "RSH1c2")]
        public async Task RSH1c2_ListChannelsPaginated()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { "channel-1", "channel-2", "channel-3" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.ChannelSubscriptions.ListChannelsAsync(new PaginatedRequestParams());

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/channels");

            // ASSERT result IS PaginatedResult: guaranteed at compile time (ListChannelsAsync returns PaginatedResult<string>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(3);
            result.Items[0].Should().Be("channel-1");
            result.Items[1].Should().Be("channel-2");
            result.Items[2].Should().Be("channel-3");
        }

        // UTS: rest/unit/RSH1c2/list-channels-with-limit-1
        [UtsFact]
        [Trait("spec", "RSH1c2")]
        public async Task RSH1c2_ListChannelsWithLimit()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { "channel-1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.ChannelSubscriptions.ListChannelsAsync(new PaginatedRequestParams { Limit = 1 });

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1");
            result.Items.Should().HaveCount(1);
        }

        // UTS: rest/unit/RSH1c3/save-post-subscription-0
        [UtsFact]
        [Trait("spec", "RSH1c3")]
        public async Task RSH1c3_SavePostSubscription()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject
                    {
                        ["channel"] = "my-channel",
                        ["deviceId"] = "device-001",
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var subscription = PushChannelSubscription.ForDevice("my-channel", "device-001");

            var result = await client.Push.Admin.ChannelSubscriptions.SaveAsync(subscription);

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(request.BodyText);
            ((string)body["channel"]).Should().Be("my-channel");
            ((string)body["deviceId"]).Should().Be("device-001");

            // ASSERT result IS PushChannelSubscription: guaranteed at compile time (SaveAsync returns PushChannelSubscription)
            result.Should().NotBeNull();
            result.Channel.Should().Be("my-channel");
            result.DeviceId.Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c3/save-updates-existing-1
        [UtsFact]
        [Trait("spec", "RSH1c3")]
        public async Task RSH1c3_SaveUpdatesExisting()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["channel"] = "my-channel",
                            ["clientId"] = "client-abc",
                        });
                    }
                    else
                    {
                        req.RespondWith(200, new JObject
                        {
                            ["channel"] = "my-channel",
                            ["clientId"] = "client-abc",
                        });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var subscription = PushChannelSubscription.ForClientId("my-channel", "client-abc");

            var result1 = await client.Push.Admin.ChannelSubscriptions.SaveAsync(subscription);
            var result2 = await client.Push.Admin.ChannelSubscriptions.SaveAsync(subscription);

            // Assertions
            Volatile.Read(ref requestCount).Should().Be(2);
            result1.Channel.Should().Be("my-channel");
            result2.Channel.Should().Be("my-channel");
        }

        // UTS: rest/unit/RSH1c3/save-error-propagated-2
        [UtsFact]
        [Trait("spec", "RSH1c3")]
        public async Task RSH1c3_SaveErrorPropagated()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(400, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40000,
                            ["statusCode"] = 400,
                            ["message"] = "Invalid subscription",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            var subscription = PushChannelSubscription.ForDevice("my-channel", "device-001");

            Func<Task> act = () => client.Push.Admin.ChannelSubscriptions.SaveAsync(subscription);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int?)error.StatusCode).Should().Be(400);
        }

        // UTS: rest/unit/RSH1c4/remove-delete-clientid-0
        [UtsFact]
        [Trait("spec", "RSH1c4")]
        public async Task RSH1c4_RemoveDeleteClientid()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var subscription = PushChannelSubscription.ForClientId("my-channel", "client-abc");

            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(subscription);

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be("my-channel");
            request.Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c4/remove-delete-deviceid-1
        [UtsFact]
        [Trait("spec", "RSH1c4")]
        public async Task RSH1c4_RemoveDeleteDeviceid()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var subscription = PushChannelSubscription.ForDevice("my-channel", "device-001");

            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(subscription);

            // Assertions
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["channel"].Should().Be("my-channel");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c4/remove-nonexistent-succeeds-2
        [UtsFact]
        [Trait("spec", "RSH1c4")]
        public async Task RSH1c4_RemoveNonexistentSucceeds()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            var subscription = PushChannelSubscription.ForClientId("nonexistent-channel", "nonexistent-client");

            // Should not throw — server returns success even for nonexistent subscriptions
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(subscription);
        }

        // UTS: rest/unit/RSH1c5/remove-where-clientid-0
        [UtsFact]
        [Trait("spec", "RSH1c5")]
        public async Task RSH1c5_RemoveWhereClientid()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Push.Admin.ChannelSubscriptions.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = "client-abc" });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1c5/remove-where-deviceid-1
        [UtsFact]
        [Trait("spec", "RSH1c5")]
        public async Task RSH1c5_RemoveWhereDeviceid()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Push.Admin.ChannelSubscriptions.RemoveWhereAsync(new Dictionary<string, string> { ["deviceId"] = "device-001" });

            // Assertions
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/channelSubscriptions");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1c5/remove-where-no-match-succeeds-2
        [UtsFact]
        [Trait("spec", "RSH1c5")]
        public async Task RSH1c5_RemoveWhereNoMatchSucceeds()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(204, null);
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            // Should not throw — server returns success even with no matching subscriptions
            await client.Push.Admin.ChannelSubscriptions.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = "nonexistent-client" });
        }
    }
}
