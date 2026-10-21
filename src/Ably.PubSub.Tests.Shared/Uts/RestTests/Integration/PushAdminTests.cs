// Push Admin Integration Tests (RSH1, RSH1a, RSH1b1, RSH1b2, RSH1b3, RSH1b4, RSH1b5, RSH1c1, RSH1c2, RSH1c3, RSH1c4, RSH1c5)
// Derived from uts/rest/integration/push_admin.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class PushAdminTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public PushAdminTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The specs' full_access_key (app_config.keys[0].key_str).
        private string FullAccessKey => App.Keys[0].KeyStr;

        // The spec's DeviceDetails(id:, clientId:, platform:, formFactor:, push: DevicePushDetails(recipient:)).
        private static DeviceDetails Device(string id, string platform, string formFactor, JObject recipient, string clientId = null)
        {
            return new DeviceDetails
            {
                Id = id,
                ClientId = clientId,
                Platform = platform,
                FormFactor = formFactor,
                Push = new DeviceDetails.PushData { Recipient = recipient },
            };
        }

        // UTS: rest/integration/RSH1a/push-publish-clientid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_PushPublishClientid()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            // Test Steps and Assertions
            // Publish with clientId recipient — should not throw
            await client.Push.Admin.PublishAsync(
                new JObject { ["clientId"] = "test-client-push" },
                new JObject
                {
                    ["notification"] = new JObject
                    {
                        ["title"] = "Integration Test",
                        ["body"] = "Hello from push admin",
                    },
                });
        }

        // UTS: rest/integration/RSH1a/push-publish-invalid-recipient-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1a")]
        public async Task RSH1a_PushPublishInvalidRecipient()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            // Test Steps and Assertions
            Func<Task> publish = () => client.Push.Admin.PublishAsync(
                new JObject(),
                new JObject { ["notification"] = new JObject { ["title"] = "Test" } });
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // NOTE: ErrorInfo.Code is a non-nullable int; "IS NOT null" is read as "an error code is set" (non-zero)
            error.Code.Should().NotBe(0);
        }

        // UTS: rest/integration/RSH1b3/save-and-get-device-0
        // DEVIATION: see deviations.md (RSH1b1: get() sends the local device's X-Ably-DeviceSecret for any deviceId; the sandbox answers 401 40100)
        [DeviationFact("RSH1b1", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b3")]
        public async Task RSH1b3_SaveAndGetDevice()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var deviceId = "test-device-" + RandomId();

            // Test Steps
            // Save a device registration
            var saved = await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "ios",
                "phone",
                new JObject { ["transportType"] = "apns", ["deviceToken"] = "test-token-" + RandomId() }));

            // Assertions
            // ASSERT saved IS DeviceDetails: guaranteed at compile time (SaveAsync returns DeviceDetails)
            saved.Should().NotBeNull();
            saved.Id.Should().Be(deviceId);
            saved.Platform.Should().Be("ios");
            saved.FormFactor.Should().Be("phone");
            ((string)saved.Push.Recipient["transportType"]).Should().Be("apns");

            // Retrieve the same device
            var retrievedResult = await client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
            AssertSucceeded(retrievedResult, "deviceRegistrations.get()");
            var retrieved = retrievedResult.Value;

            // ASSERT retrieved IS DeviceDetails: guaranteed at compile time (GetAsync returns Result<DeviceDetails>)
            retrieved.Should().NotBeNull();
            retrieved.Id.Should().Be(deviceId);
            retrieved.Platform.Should().Be("ios");

            // Cleanup
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
        }

        // UTS: rest/integration/RSH1b3/update-device-registration-1
        // DEVIATION: see deviations.md (RSH1b1: get() sends the local device's X-Ably-DeviceSecret for any deviceId; the sandbox answers 401 40100)
        [DeviationFact("RSH1b1", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b3")]
        public async Task RSH1b3_UpdateDeviceRegistration()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var deviceId = "test-device-update-" + RandomId();

            // Test Steps
            // Initial save
            await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "ios",
                "phone",
                new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-v1" }));

            // Update with new token
            var updated = await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "ios",
                "phone",
                new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-v2" }));

            // Assertions
            updated.Id.Should().Be(deviceId);
            ((string)updated.Push.Recipient["deviceToken"]).Should().Be("token-v2");

            // Verify via get
            var retrievedResult = await client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
            AssertSucceeded(retrievedResult, "deviceRegistrations.get()");
            ((string)retrievedResult.Value.Push.Recipient["deviceToken"]).Should().Be("token-v2");

            // Cleanup
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
        }

        // UTS: rest/integration/RSH1b1/get-unknown-device-error-0
        // DEVIATION: see deviations.md (RSH1b1: get() sends the local device's X-Ably-DeviceSecret for any deviceId; the sandbox answers 401 40100)
        [DeviationFact("RSH1b1", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b1")]
        public async Task RSH1b1_GetUnknownDeviceError()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            // Test Steps and Assertions
            // NOTE: a 404 throws AblyException before GetAsync's own NotFound Result branch (rest-mapping.md section 6)
            Func<Task> get = () => client.Push.Admin.DeviceRegistrations.GetAsync("nonexistent-device-" + RandomId());
            var error = (await get.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(404);
        }

        // UTS: rest/integration/RSH1b2/list-devices-filtered-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b2")]
        public async Task RSH1b2_ListDevicesFiltered()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var deviceId = "test-device-list-" + RandomId();

            // Register a device first
            await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "android",
                "tablet",
                new JObject { ["transportType"] = "gcm", ["registrationToken"] = "test-token" }));

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.WithDeviceId(deviceId));

            // Assertions
            // ASSERT result IS PaginatedResult: guaranteed at compile time (List returns PaginatedResult<DeviceDetails>)
            result.Items.Should().HaveCount(1);
            result.Items[0].Id.Should().Be(deviceId);
            result.Items[0].Platform.Should().Be("android");

            // Cleanup
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
        }

        // UTS: rest/integration/RSH1b2/list-devices-pagination-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b2")]
        public async Task RSH1b2_ListDevicesPagination()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-list-" + RandomId();
            var deviceIds = new List<string>();

            // Register multiple devices with the same clientId
            foreach (var i in new[] { 1, 2, 3 })
            {
                var deviceId = "test-device-limit-" + i + "-" + RandomId();
                deviceIds.Add(deviceId);
                await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                    deviceId,
                    "ios",
                    "phone",
                    new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-" + i },
                    clientId));
            }

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.WithClientId(clientId, 2));

            // Assertions
            result.Items.Count.Should().BeLessThanOrEqualTo(2);
            result.HasNext.Should().BeTrue();

            // Cleanup
            foreach (var deviceId in deviceIds)
            {
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH1b4/remove-device-0
        // DEVIATION: see deviations.md (RSH1b1: get() sends the local device's X-Ably-DeviceSecret for any deviceId; the sandbox answers 401 40100)
        [DeviationFact("RSH1b1", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b4")]
        public async Task RSH1b4_RemoveDevice()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var deviceId = "test-device-remove-" + RandomId();

            // Register a device
            await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "ios",
                "phone",
                new JObject { ["transportType"] = "apns", ["deviceToken"] = "test-token" }));

            // Test Steps
            // Remove the device
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);

            // Verify it's gone
            // NOTE: a 404 throws AblyException before GetAsync's own NotFound Result branch (rest-mapping.md section 6)
            Func<Task> get = () => client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
            var error = (await get.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(404);
        }

        // UTS: rest/integration/RSH1b4/remove-nonexistent-device-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b4")]
        public async Task RSH1b4_RemoveNonexistentDevice()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            // Test Steps and Assertions
            // Should not throw
            await client.Push.Admin.DeviceRegistrations.RemoveAsync("nonexistent-device-" + RandomId());
        }

        // UTS: rest/integration/RSH1b5/remove-where-clientid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1b5")]
        public async Task RSH1b5_RemoveWhereClientid()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-removeWhere-" + RandomId();
            var deviceIds = new List<string>();

            // Register two devices with the same clientId
            foreach (var i in new[] { 1, 2 })
            {
                var deviceId = "test-device-rw-" + i + "-" + RandomId();
                deviceIds.Add(deviceId);
                await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                    deviceId,
                    "ios",
                    "phone",
                    new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-" + i },
                    clientId));
            }

            // Test Steps
            // Remove all devices for this clientId
            await client.Push.Admin.DeviceRegistrations.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = clientId });

            // Verify both are gone
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.WithClientId(clientId));
            result.Items.Should().HaveCount(0);
        }

        // UTS: rest/integration/RSH1c3/save-and-list-subscriptions-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c3")]
        public async Task RSH1c3_SaveAndListSubscriptions()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var deviceId = "test-device-sub-" + RandomId();
            var channelName = "pushenabled:test-sub-" + RandomId();

            // Register a device first (required for deviceId subscriptions)
            await client.Push.Admin.DeviceRegistrations.SaveAsync(Device(
                deviceId,
                "ios",
                "phone",
                new JObject { ["transportType"] = "apns", ["deviceToken"] = "test-token" }));

            // Test Steps
            // Save a channel subscription
            var saved = await client.Push.Admin.ChannelSubscriptions.SaveAsync(PushChannelSubscription.ForDevice(channelName, deviceId));

            // Assertions
            // ASSERT saved IS PushChannelSubscription: guaranteed at compile time (SaveAsync returns PushChannelSubscription)
            saved.Should().NotBeNull();
            saved.Channel.Should().Be(channelName);
            saved.DeviceId.Should().Be(deviceId);

            // List subscriptions for this channel
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithClientId(channel: channelName));

            // ASSERT result IS PaginatedResult: guaranteed at compile time (ListAsync returns PaginatedResult<PushChannelSubscription>)
            result.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            var found = false;
            foreach (var sub in result.Items)
            {
                if (sub.DeviceId == deviceId)
                {
                    found = true;
                    sub.Channel.Should().Be(channelName);
                }
            }

            found.Should().BeTrue();

            // Cleanup
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(PushChannelSubscription.ForDevice(channelName, deviceId));
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
        }

        // UTS: rest/integration/RSH1c3/save-subscription-clientid-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c3")]
        public async Task RSH1c3_SaveSubscriptionClientid()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-sub-" + RandomId();
            var channelName = "pushenabled:test-clientsub-" + RandomId();

            // Test Steps
            var saved = await client.Push.Admin.ChannelSubscriptions.SaveAsync(PushChannelSubscription.ForClientId(channelName, clientId));

            // Assertions
            saved.Channel.Should().Be(channelName);
            saved.ClientId.Should().Be(clientId);

            // Cleanup
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(PushChannelSubscription.ForClientId(channelName, clientId));
        }

        // UTS: rest/integration/RSH1c2/list-channels-with-subscriptions-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c2")]
        public async Task RSH1c2_ListChannelsWithSubscriptions()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-lc-" + RandomId();
            var channelName = "pushenabled:test-listchannels-" + RandomId();

            // Create a subscription to ensure the channel appears
            await client.Push.Admin.ChannelSubscriptions.SaveAsync(PushChannelSubscription.ForClientId(channelName, clientId));

            // Test Steps
            var result = await client.Push.Admin.ChannelSubscriptions.ListChannelsAsync(new PaginatedRequestParams());

            // Assertions
            // ASSERT result IS PaginatedResult: guaranteed at compile time (ListChannelsAsync returns PaginatedResult<string>)
            // The channel we subscribed to should appear in the list
            result.Items.Should().Contain(channelName);

            // Cleanup
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(PushChannelSubscription.ForClientId(channelName, clientId));
        }

        // UTS: rest/integration/RSH1c4/remove-channel-subscription-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c4")]
        public async Task RSH1c4_RemoveChannelSubscription()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-rm-" + RandomId();
            var channelName = "pushenabled:test-remove-" + RandomId();

            // Create a subscription
            await client.Push.Admin.ChannelSubscriptions.SaveAsync(PushChannelSubscription.ForClientId(channelName, clientId));

            // Test Steps
            // Remove the subscription
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(PushChannelSubscription.ForClientId(channelName, clientId));

            // Verify it's gone
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithClientId(channel: channelName, clientId: clientId));
            result.Items.Should().HaveCount(0);
        }

        // UTS: rest/integration/RSH1c4/remove-nonexistent-subscription-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c4")]
        public async Task RSH1c4_RemoveNonexistentSubscription()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            // Test Steps and Assertions
            // Should not throw
            await client.Push.Admin.ChannelSubscriptions.RemoveAsync(PushChannelSubscription.ForClientId(
                "pushenabled:nonexistent-" + RandomId(),
                "nonexistent-client"));
        }

        // UTS: rest/integration/RSH1c5/remove-where-subscriptions-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH1c5")]
        public async Task RSH1c5_RemoveWhereSubscriptions()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });
            var clientId = "test-client-rwsub-" + RandomId();
            var channelNames = new List<string>();

            // Create subscriptions on two channels for the same clientId
            foreach (var i in new[] { 1, 2 })
            {
                var ch = "pushenabled:test-rwsub-" + i + "-" + RandomId();
                channelNames.Add(ch);
                await client.Push.Admin.ChannelSubscriptions.SaveAsync(PushChannelSubscription.ForClientId(ch, clientId));
            }

            // Test Steps
            // Remove all subscriptions for this clientId
            await client.Push.Admin.ChannelSubscriptions.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = clientId });

            // Verify they're all gone
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithClientId(clientId: clientId));
            result.Items.Should().HaveCount(0);
        }
    }
}
