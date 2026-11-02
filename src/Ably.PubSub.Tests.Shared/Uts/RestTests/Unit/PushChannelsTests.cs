// PushChannel Tests (RSH7, RSH7a, RSH7a1, RSH7a2, RSH7a3, RSH7b, RSH7b1, RSH7b2, RSH7c, RSH7c1, RSH7c2, RSH7c3, RSH7d, RSH7d1, RSH7d2, RSH7e)
// Derived from uts/rest/unit/push/push_channels.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: white-box per rest-mapping.md section 10: client.device = LocalDevice(...) uses the internal PubSubHttpClient.Device setter (the public API has no setter)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Push;
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
    public class PushChannelsTests : UtsUnitTestBase
    {
        public PushChannelsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's LocalDevice(id:, deviceIdentityToken:, clientId:).
        private static LocalDevice LocalDeviceWith(string id, string deviceIdentityToken, string clientId)
        {
            var device = LocalDevice.Create(clientId, new FakeMobileDevice());
            device.Id = id;
            device.DeviceIdentityToken = deviceIdentityToken;
            return device;
        }

        // UTS: rest/unit/RSH7a2/subscribe-device-post-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSH6a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSH7a2")]
        public async Task RSH7a2_SubscribeDevicePost()
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
                        ["deviceId"] = "test-device-001",
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device as a registered push target
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            await channel.Push.SubscribeDevice();

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(request.BodyText);
            ((string)body["channel"]).Should().Be("my-channel");
            ((string)body["deviceId"]).Should().Be("test-device-001");

            // RSH7a3 + RSH6a — push device authentication via deviceIdentityToken
            request.Headers["X-Ably-DeviceToken"].Should().Be("test-device-identity-token");
        }

        // UTS: rest/unit/RSH7a1/subscribe-device-no-token-fails-0
        [UtsFact]
        [Trait("spec", "RSH7a1")]
        public async Task RSH7a1_SubscribeDeviceNoTokenFails()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device WITHOUT a deviceIdentityToken (not registered)
            client.Device = LocalDeviceWith("test-device-001", null, "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps and Assertions
            Func<Task> act = () => channel.Push.SubscribeDevice();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            // NOTE: ErrorInfo.Code is a non-nullable int; "IS NOT null" is read as "an error code is set" (non-zero)
            error.Code.Should().NotBe(0);
            error.Message.Should().Contain("deviceIdentityToken");
        }

        // UTS: rest/unit/RSH7b2/subscribe-client-post-0
        [UtsFact]
        [Trait("spec", "RSH7b2")]
        public async Task RSH7b2_SubscribeClientPost()
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
                        ["clientId"] = "test-client",
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device with a clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            await channel.Push.SubscribeClient();

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(request.BodyText);
            ((string)body["channel"]).Should().Be("my-channel");
            ((string)body["clientId"]).Should().Be("test-client");
        }

        // UTS: rest/unit/RSH7b1/subscribe-client-no-clientid-fails-0
        [UtsFact]
        [Trait("spec", "RSH7b1")]
        public async Task RSH7b1_SubscribeClientNoClientidFails()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device WITHOUT a clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", null);

            var channel = client.Channels.Get("my-channel");

            // Test Steps and Assertions
            Func<Task> act = () => channel.Push.SubscribeClient();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            // NOTE: ErrorInfo.Code is a non-nullable int; "IS NOT null" is read as "an error code is set" (non-zero)
            error.Code.Should().NotBe(0);
            error.Message.Should().Contain("clientId");
        }

        // UTS: rest/unit/RSH7c2/unsubscribe-device-delete-0
        // DEVIATION: see deviations.md
        [DeviationFact("RSH6a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSH7c2")]
        public async Task RSH7c2_UnsubscribeDeviceDelete()
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

            // Configure the local device as a registered push target
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            await channel.Push.UnsubscribeDevice();

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be("my-channel");
            request.Url.QueryParams["deviceId"].Should().Be("test-device-001");

            // RSH7c3 + RSH6a — push device authentication via deviceIdentityToken
            request.Headers["X-Ably-DeviceToken"].Should().Be("test-device-identity-token");
        }

        // UTS: rest/unit/RSH7c1/unsubscribe-device-no-token-fails-0
        [UtsFact]
        [Trait("spec", "RSH7c1")]
        public async Task RSH7c1_UnsubscribeDeviceNoTokenFails()
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

            // Configure the local device WITHOUT a deviceIdentityToken (not registered)
            client.Device = LocalDeviceWith("test-device-001", null, "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps and Assertions
            Func<Task> act = () => channel.Push.UnsubscribeDevice();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            // NOTE: ErrorInfo.Code is a non-nullable int; "IS NOT null" is read as "an error code is set" (non-zero)
            error.Code.Should().NotBe(0);
            error.Message.Should().Contain("deviceIdentityToken");
        }

        // UTS: rest/unit/RSH7d2/unsubscribe-client-delete-0
        [UtsFact]
        [Trait("spec", "RSH7d2")]
        public async Task RSH7d2_UnsubscribeClientDelete()
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

            // Configure the local device with a clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            await channel.Push.UnsubscribeClient();

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be("my-channel");
            request.Url.QueryParams["clientId"].Should().Be("test-client");
        }

        // UTS: rest/unit/RSH7d1/unsubscribe-client-no-clientid-fails-0
        [UtsFact]
        [Trait("spec", "RSH7d1")]
        public async Task RSH7d1_UnsubscribeClientNoClientidFails()
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

            // Configure the local device WITHOUT a clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", null);

            var channel = client.Channels.Get("my-channel");

            // Test Steps and Assertions
            Func<Task> act = () => channel.Push.UnsubscribeClient();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            // NOTE: ErrorInfo.Code is a non-nullable int; "IS NOT null" is read as "an error code is set" (non-zero)
            error.Code.Should().NotBe(0);
            error.Message.Should().Contain("clientId");
        }

        // UTS: rest/unit/RSH7e/list-subscriptions-with-filters-0
        [UtsFact]
        [Trait("spec", "RSH7e")]
        public async Task RSH7e_ListSubscriptionsWithFilters()
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
                            ["deviceId"] = "test-device-001",
                        },
                        new JObject
                        {
                            ["channel"] = "my-channel",
                            ["clientId"] = "test-client",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device with both deviceId and clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", "test-client");

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            var result = await channel.Push.ListSubscriptions(ListSubscriptionsRequest.Empty(10));

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            // Channel name, device ID, and client ID are automatically included
            request.Url.QueryParams["channel"].Should().Be("my-channel");
            request.Url.QueryParams["deviceId"].Should().Be("test-device-001");
            request.Url.QueryParams["clientId"].Should().Be("test-client");

            // concatFilters must be set to true
            request.Url.QueryParams["concatFilters"].Should().Be("true");

            // User-provided params are forwarded
            request.Url.QueryParams["limit"].Should().Be("10");

            // ASSERT result IS PaginatedResult: guaranteed at compile time (ListSubscriptions returns PaginatedResult<PushChannelSubscription>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(2);
            // ASSERT result.items[0] IS PushChannelSubscription: guaranteed at compile time (Items is List<PushChannelSubscription>)
            result.Items[0].Channel.Should().Be("my-channel");
            result.Items[0].DeviceId.Should().Be("test-device-001");
            result.Items[1].ClientId.Should().Be("test-client");
        }

        // UTS: rest/unit/RSH7e/list-subscriptions-omits-clientid-1
        [UtsFact]
        [Trait("spec", "RSH7e")]
        public async Task RSH7e_ListSubscriptionsOmitsClientid()
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
                            ["deviceId"] = "test-device-001",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Configure the local device WITHOUT a clientId
            client.Device = LocalDeviceWith("test-device-001", "test-device-identity-token", null);

            var channel = client.Channels.Get("my-channel");

            // Test Steps
            var result = await channel.Push.ListSubscriptions(ListSubscriptionsRequest.Empty());

            // Assertions
            var request = capturedRequests[0];
            request.Url.QueryParams["channel"].Should().Be("my-channel");
            request.Url.QueryParams["deviceId"].Should().Be("test-device-001");
            request.Url.QueryParams["concatFilters"].Should().Be("true");
            request.Url.QueryParams.Should().NotContainKey("clientId");

            result.Items.Should().HaveCount(1);
        }
    }
}
