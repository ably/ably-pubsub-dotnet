using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Tests.Push;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/push_channels.md in ably/specification.
    ///
    /// Spec points: RSH7a, RSH7b, RSH7c, RSH7d
    ///
    /// Integration tier: a real sandbox app, no mock. That matters here, because the spec's
    /// RSH7a2/RSH7b2/RSH7c2/RSH7d2 points are about the verb and path each call issues and nothing
    /// in this tier can observe a request. The only check available is the spec's own: read the
    /// subscription back through <c>push.admin.channelSubscriptions.list</c>.
    ///
    /// Four things are worth knowing before changing a test here.
    ///
    /// <b>The local device is installed through internals.</b> <c>IHttpChannel.Push</c> is
    /// <c>null</c> unless the client was built with an <c>IMobileDevice</c>, and on the
    /// <c>net6.0</c>/<c>net7.0</c> heads this tree compiles for there is no platform one. So the
    /// spec's <c>client.device = LocalDevice(...)</c> is two steps: construct through the internal
    /// <c>PubSubHttpClient(ClientOptions, IMobileDevice)</c> with the repo's existing
    /// <c>FakeMobileDevice</c>, then assign <c>client.Device</c>. Both are reachable because the
    /// test assembly has <c>InternalsVisibleTo</c>; no new harness type is needed.
    ///
    /// <b>Each verification list carries a control.</b> The server silently drops a query parameter
    /// it does not recognise, so "the subscription I just made is in the list" would pass with the
    /// filter doing nothing. Each test therefore also creates a decoy subscription on the same
    /// channel under a different id, which the filter has to exclude and the unsubscribe has to
    /// leave behind. The spec's assertions are unchanged; the controls are additional.
    ///
    /// <b>Creation and removal are not immediately consistent</b> — the SDK documents removal as
    /// asynchronous — so both verification reads go through <c>UtsSandbox.WallClockPollUntil</c>.
    ///
    /// The literal <c>deviceIdentityToken: "test-device-identity-token"</c> is the spec's own value
    /// and is kept. For a device subscription the SDK sends it as <c>X-Ably-DeviceIdentityToken</c>,
    /// so a made-up value does reach the sandbox; for a clientId subscription it never leaves the
    /// process, and only satisfies the SDK's local "is this device activated" check.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PushChannelsTests : UtsIntegrationTestBase
    {
        public PushChannelsTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSH7a/subscribe-unsubscribe-device-0
        [Fact]
        public async Task RSH7a_SubscribeUnsubscribeDevice()
        {
            var options = await SandboxOptions();
            var client = new PubSubHttpClient(options, new FakeMobileDevice());

            var deviceId = "test-device-pushchan-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-rsh7a-" + UtsSandbox.RandomId();
            var deviceToken = "test-apns-token-" + UtsSandbox.RandomId();

            // The control: a clientId subscription on the same channel, which the deviceId filter must
            // exclude and which unsubscribeDevice must leave behind.
            var decoyClientId = "test-client-rsh7a-decoy-" + UtsSandbox.RandomId();

            try
            {
                // Register a device via the admin API, before client.Device is set, so this request
                // carries only the key's authentication.
                await client.Push.Admin.DeviceRegistrations.SaveAsync(new DeviceDetails
                {
                    Id = deviceId,
                    Platform = "ios",
                    FormFactor = "phone",
                    Push = new DeviceDetails.PushData
                    {
                        Recipient = JObject.FromObject(new
                        {
                            transportType = "apns",
                            deviceToken,
                        }),
                    },
                });

                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));

                // The spec's client.device = LocalDevice(id:, deviceIdentityToken:, clientId: null).
                client.Device = new LocalDevice
                {
                    Id = deviceId,
                    DeviceIdentityToken = "test-device-identity-token",
                    ClientId = null,
                };

                var channel = client.Channels.Get(channelName);

                channel.Push.Should().NotBeNull(
                    "PushChannel is only built when the client carries an IMobileDevice, which is the "
                    + "premise of every assertion below");

                await channel.Push.SubscribeDevice();

                Func<Task<PaginatedResult<PushChannelSubscription>>> fetchSubscription = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithDeviceId(channelName, deviceId));
                    return page.Items.Count >= 1 ? page : null;
                };

                var result = await UtsSandbox.WallClockPollUntil(
                    fetchSubscription,
                    $"the device subscription for '{deviceId}' on '{channelName}' to be listable");

                result.Items.Should().HaveCountGreaterOrEqualTo(1);

                var found = false;
                foreach (var sub in result.Items)
                {
                    if (sub.DeviceId == deviceId && sub.Channel == channelName)
                    {
                        found = true;
                    }
                }

                found.Should().BeTrue();

                result.Items.Should().OnlyContain(
                    s => s.DeviceId == deviceId,
                    "the deviceId filter must narrow the list; an unrecognised query parameter would "
                    + "be dropped by the server and the decoy clientId subscription returned too");

                await channel.Push.UnsubscribeDevice();

                Func<Task<bool>> removed = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithDeviceId(channelName, deviceId));
                    return page.Items.Count == 0;
                };

                await UtsSandbox.WallClockPollUntil(
                    removed,
                    $"the device subscription for '{deviceId}' on '{channelName}' to be removed");

                var channelOnly = ListSubscriptionsRequest.Empty();
                channelOnly.Channel = channelName;
                var remaining = await client.Push.Admin.ChannelSubscriptions.ListAsync(channelOnly);

                remaining.Items.Select(s => s.ClientId).Should().Contain(
                    decoyClientId,
                    "unsubscribeDevice must only remove the local device's own subscription");
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH7b/subscribe-unsubscribe-client-0
        [Fact]
        public async Task RSH7b_SubscribeUnsubscribeClient()
        {
            var options = await SandboxOptions();
            var client = new PubSubHttpClient(options, new FakeMobileDevice());

            var clientId = "test-client-pushchan-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-rsh7b-" + UtsSandbox.RandomId();

            // The control: a subscription on the same channel under a different clientId, which the
            // clientId filter must exclude and which unsubscribeClient must leave behind.
            var decoyClientId = "test-client-rsh7b-decoy-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));

                // The spec's client.device = LocalDevice(id:, deviceIdentityToken:, clientId:).
                // subscribeClient does not require device registration — it subscribes by clientId.
                client.Device = new LocalDevice
                {
                    Id = "test-device-" + UtsSandbox.RandomId(),
                    DeviceIdentityToken = "test-device-identity-token",
                    ClientId = clientId,
                };

                var channel = client.Channels.Get(channelName);

                channel.Push.Should().NotBeNull(
                    "PushChannel is only built when the client carries an IMobileDevice, which is the "
                    + "premise of every assertion below");

                await channel.Push.SubscribeClient();

                Func<Task<PaginatedResult<PushChannelSubscription>>> fetchSubscription = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(channelName, clientId));
                    return page.Items.Count >= 1 ? page : null;
                };

                var result = await UtsSandbox.WallClockPollUntil(
                    fetchSubscription,
                    $"the client subscription for '{clientId}' on '{channelName}' to be listable");

                result.Items.Should().HaveCountGreaterOrEqualTo(1);

                var found = false;
                foreach (var sub in result.Items)
                {
                    if (sub.ClientId == clientId && sub.Channel == channelName)
                    {
                        found = true;
                    }
                }

                found.Should().BeTrue();

                result.Items.Should().OnlyContain(
                    s => s.ClientId == clientId,
                    "the clientId filter must narrow the list; an unrecognised query parameter would "
                    + "be dropped by the server and the decoy subscription returned too");

                await channel.Push.UnsubscribeClient();

                Func<Task<bool>> removed = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(channelName, clientId));
                    return page.Items.Count == 0;
                };

                await UtsSandbox.WallClockPollUntil(
                    removed,
                    $"the client subscription for '{clientId}' on '{channelName}' to be removed");

                var channelOnly = ListSubscriptionsRequest.Empty();
                channelOnly.Channel = channelName;
                var remaining = await client.Push.Admin.ChannelSubscriptions.ListAsync(channelOnly);

                remaining.Items.Select(s => s.ClientId).Should().Contain(
                    decoyClientId,
                    "unsubscribeClient must only remove the local device's own clientId subscription");
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));
            }
        }
    }
}
