// PushChannel Integration Tests (RSH7a, RSH7b, RSH7c, RSH7d)
// Derived from uts/rest/integration/push_channels.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); white-box per rest-mapping.md section 10: client.device = LocalDevice(...) uses the internal PubSubHttpClient.Device setter (the public API has no setter)

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
    public class PushChannelsTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public PushChannelsTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The specs' full_access_key (app_config.keys[0].key_str).
        private string FullAccessKey => App.Keys[0].KeyStr;

        // The spec's LocalDevice(id:, deviceIdentityToken:, clientId:).
        private static LocalDevice LocalDeviceWith(string id, string deviceIdentityToken, string clientId)
        {
            var device = LocalDevice.Create(clientId, new FakeMobileDevice());
            device.Id = id;
            device.DeviceIdentityToken = deviceIdentityToken;
            return device;
        }

        // UTS: rest/integration/RSH7a/subscribe-unsubscribe-device-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH7a")]
        public async Task RSH7a_SubscribeUnsubscribeDevice()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            var deviceId = "test-device-pushchan-" + RandomId();
            var channelName = "pushenabled:test-rsh7a-" + RandomId();
            var deviceToken = "test-apns-token-" + RandomId();

            // Register a device via admin API (required before device subscriptions work)
            await client.Push.Admin.DeviceRegistrations.SaveAsync(new DeviceDetails
            {
                Id = deviceId,
                Platform = "ios",
                FormFactor = "phone",
                Push = new DeviceDetails.PushData
                {
                    Recipient = new JObject { ["transportType"] = "apns", ["deviceToken"] = deviceToken },
                },
            });

            // Configure the local device to match the registered device
            // The deviceIdentityToken is obtained from the registration response
            // For integration testing, we use the admin API to register and then
            // configure the LocalDevice with values that allow push device auth
            client.Device = LocalDeviceWith(deviceId, "test-device-identity-token", null);

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Subscribe the device to push on this channel
            await channel.Push.SubscribeDevice();

            // Verify subscription exists via admin API
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithDeviceId(channelName, deviceId));
            result.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            var found = false;
            foreach (var sub in result.Items)
            {
                if (sub.DeviceId == deviceId && sub.Channel == channelName)
                {
                    found = true;
                }
            }

            found.Should().BeTrue();

            // Unsubscribe the device
            await channel.Push.UnsubscribeDevice();

            // Verify subscription is removed
            var resultAfter = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithDeviceId(channelName, deviceId));
            resultAfter.Items.Should().HaveCount(0);

            // Cleanup
            await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
        }

        // UTS: rest/integration/RSH7b/subscribe-unsubscribe-client-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSH7b")]
        public async Task RSH7b_SubscribeUnsubscribeClient()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey, UseBinaryProtocol = false });

            var clientId = "test-client-pushchan-" + RandomId();
            var channelName = "pushenabled:test-rsh7b-" + RandomId();

            // Configure the local device with a clientId
            // subscribeClient does not require device registration — it subscribes
            // by clientId, not by deviceId
            client.Device = LocalDeviceWith("test-device-" + RandomId(), "test-device-identity-token", clientId);

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Subscribe the client to push on this channel
            await channel.Push.SubscribeClient();

            // Verify subscription exists via admin API
            var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithClientId(channelName, clientId));
            result.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            var found = false;
            foreach (var sub in result.Items)
            {
                if (sub.ClientId == clientId && sub.Channel == channelName)
                {
                    found = true;
                }
            }

            found.Should().BeTrue();

            // Unsubscribe the client
            await channel.Push.UnsubscribeClient();

            // Verify subscription is removed
            var resultAfter = await client.Push.Admin.ChannelSubscriptions.ListAsync(ListSubscriptionsRequest.WithClientId(channelName, clientId));
            resultAfter.Items.Should().HaveCount(0);
        }
    }
}
