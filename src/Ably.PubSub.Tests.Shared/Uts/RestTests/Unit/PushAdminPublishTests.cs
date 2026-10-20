// PushAdmin Publish Tests (RSH1, RSH1a)
// Derived from uts/rest/unit/push/push_admin_publish.md
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
    public class PushAdminPublishTests : UtsUnitTestBase
    {
        public PushAdminPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1/push-admin-accessible-0
        [Fact]
        [Trait("spec", "RSH1")]
        public void RSH1_PushAdminAccessible()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject()));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Assertions
            // ASSERT client.push IS Push: guaranteed at compile time (Push is declared as PushHttp)
            client.Push.Should().NotBeNull();
            // ASSERT client.push.admin IS PushAdmin: guaranteed at compile time (Admin is declared as PushAdmin)
            client.Push.Admin.Should().NotBeNull();
            // ASSERT client.push.admin.deviceRegistrations IS PushDeviceRegistrations: guaranteed at compile time (DeviceRegistrations is declared as IDeviceRegistrations)
            client.Push.Admin.DeviceRegistrations.Should().NotBeNull();
            // ASSERT client.push.admin.channelSubscriptions IS PushChannelSubscriptions: guaranteed at compile time (ChannelSubscriptions is declared as IPushChannelSubscriptions)
            client.Push.Admin.ChannelSubscriptions.Should().NotBeNull();
        }

        // UTS: rest/unit/RSH1a/publish-post-push-publish-0
        [UtsFact]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_PublishPostPushPublish()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Push.Admin.PublishAsync(
                new JObject
                {
                    ["transportType"] = "apns",
                    ["deviceToken"] = "foo",
                },
                new JObject
                {
                    ["notification"] = new JObject
                    {
                        ["title"] = "Test",
                        ["body"] = "Hello",
                    },
                });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/publish");

            var body = JObject.Parse(request.BodyText);
            ((string)body["recipient"]["transportType"]).Should().Be("apns");
            ((string)body["recipient"]["deviceToken"]).Should().Be("foo");
            ((string)body["notification"]["title"]).Should().Be("Test");
            ((string)body["notification"]["body"]).Should().Be("Hello");
        }

        // UTS: rest/unit/RSH1a/publish-clientid-recipient-1
        [UtsFact]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_PublishClientidRecipient()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Push.Admin.PublishAsync(
                new JObject
                {
                    ["clientId"] = "user-123",
                },
                new JObject
                {
                    ["data"] = new JObject
                    {
                        ["key"] = "value",
                    },
                });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var body = JObject.Parse(capturedRequests[0].BodyText);
            ((string)body["recipient"]["clientId"]).Should().Be("user-123");
            ((string)body["data"]["key"]).Should().Be("value");
        }

        // UTS: rest/unit/RSH1a/publish-deviceid-recipient-2
        [UtsFact]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_PublishDeviceidRecipient()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Push.Admin.PublishAsync(
                new JObject
                {
                    ["deviceId"] = "device-abc",
                },
                new JObject
                {
                    ["notification"] = new JObject
                    {
                        ["title"] = "Device Push",
                    },
                });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var body = JObject.Parse(capturedRequests[0].BodyText);
            ((string)body["recipient"]["deviceId"]).Should().Be("device-abc");
            ((string)body["notification"]["title"]).Should().Be("Device Push");
        }

        // UTS: rest/unit/RSH1a/rejects-empty-recipient-3
        // DEVIATION: see deviations.md
        [DeviationFact("RSH1a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_RejectsEmptyRecipient()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject(),
                new JObject { ["notification"] = new JObject { ["title"] = "Test" } });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            // No HTTP request should have been made
            capturedRequests.Should().HaveCount(0);
        }

        // UTS: rest/unit/RSH1a/rejects-empty-data-4
        // DEVIATION: see deviations.md
        [DeviationFact("RSH1a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_RejectsEmptyData()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject { ["clientId"] = "user-123" },
                new JObject());
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            // No HTTP request should have been made
            capturedRequests.Should().HaveCount(0);
        }

        // UTS: rest/unit/RSH1a/rejects-null-recipient-5
        [UtsFact]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_RejectsNullRecipient()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Push.Admin.PublishAsync(
                null,
                new JObject { ["notification"] = new JObject { ["title"] = "Test" } });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            capturedRequests.Should().HaveCount(0);
        }

        // UTS: rest/unit/RSH1a/server-error-propagated-6
        [UtsFact]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_ServerErrorPropagated()
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
                            ["message"] = "Invalid recipient",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject { ["transportType"] = "invalid" },
                new JObject { ["notification"] = new JObject { ["title"] = "Test" } });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int?)error.StatusCode).Should().Be(400);
        }
    }
}
