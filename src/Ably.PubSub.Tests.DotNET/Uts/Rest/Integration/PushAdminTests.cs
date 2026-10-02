using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/push_admin.md in ably/specification.
    ///
    /// Spec points: RSH1a, RSH1b1, RSH1b2, RSH1b3, RSH1b4, RSH1b5, RSH1c1, RSH1c2, RSH1c3, RSH1c4,
    /// RSH1c5
    ///
    /// Integration tier: a real sandbox app, no mock. Every test in the assembly shares one app, so
    /// each device id, client id and channel name takes a <c>UtsSandbox.RandomId()</c> suffix and
    /// everything registered is removed in a <c>finally</c>. Every test uses the full-access key, as
    /// the spec's Setup blocks do — the push-admin key only covers <c>pushenabled:admin:*</c>, not
    /// the channels the spec publishes to.
    ///
    /// Two translation decisions apply to the whole file.
    ///
    /// <b>Every filtered list carries a control.</b> The server silently drops a query parameter it
    /// does not recognise rather than rejecting it, so "the row I just created is in the list" passes
    /// just as happily with the filter doing nothing. Each filtered list therefore also compares
    /// against the unfiltered count, or registers a decoy row the filter has to exclude. The spec's
    /// own assertions are unchanged; the control is additional.
    ///
    /// <b>Every read that follows a write polls.</b> A registration is indexed asynchronously and the
    /// SDK documents subscription removal as asynchronous, so reads go through
    /// <c>UtsSandbox.WallClockPollUntil</c>. <c>PushAdminFullWait</c> is deliberately left unset:
    /// turning it on would change the requests the spec describes.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PushAdminTests : UtsIntegrationTestBase
    {
        public PushAdminTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSH1a/push-publish-clientid-0
        [Fact]
        public async Task RSH1a_PushPublishClientId()
        {
            var client = await SandboxRestClient();

            // The spec's literal "test-client-push". The sandbox app is shared, so the tier's rule is a
            // RandomId suffix on every client id.
            var clientId = "test-client-push-" + UtsSandbox.RandomId();

            var recipient = JObject.FromObject(new { clientId });
            var payload = JObject.FromObject(new
            {
                notification = new
                {
                    title = "Integration Test",
                    body = "Hello from push admin",
                },
            });

            Func<Task> publish = () => client.Push.Admin.PublishAsync(recipient, payload);

            await publish.Should().NotThrowAsync(
                "the spec reads the sandbox as accepting the request even though no real device "
                + "receives it");
        }

        // UTS: rest/integration/RSH1a/push-publish-invalid-recipient-1
        [Fact]
        public async Task RSH1a_PushPublishInvalidRecipient()
        {
            var client = await SandboxRestClient();

            var payload = JObject.FromObject(new { notification = new { title = "Test" } });

            Func<Task> publish = () => client.Push.Admin.PublishAsync(new JObject(), payload);

            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // The spec's "error.code IS NOT null". ErrorInfo.Code is a non-nullable int here, so the
            // assertion that can fail is that it was populated at all.
            error.Code.Should().NotBe(0);
        }

        // UTS: rest/integration/RSH1b3/save-and-get-device-0
        [Fact]
        public async Task RSH1b3_SaveAndGetDevice()
        {
            var client = await SandboxRestClient();
            var deviceId = "test-device-" + UtsSandbox.RandomId();

            try
            {
                var saved = await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("test-token-" + UtsSandbox.RandomId())));

                // "saved IS DeviceDetails" is static: SaveAsync returns DeviceDetails.
                saved.Should().NotBeNull();
                saved.Id.Should().Be(deviceId);
                saved.Platform.Should().Be("ios");
                saved.FormFactor.Should().Be("phone");
                ((string)saved.Push.Recipient["transportType"]).Should().Be("apns");

                Func<Task<DeviceDetails>> fetch = async () =>
                {
                    var result = await client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
                    return result.IsSuccess ? result.Value : null;
                };

                var retrieved = await UtsSandbox.WallClockPollUntil(
                    fetch,
                    $"the device registration '{deviceId}' to be readable");

                retrieved.Should().NotBeNull();
                retrieved.Id.Should().Be(deviceId);
                retrieved.Platform.Should().Be("ios");
            }
            finally
            {
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH1b3/update-device-registration-1
        [Fact]
        public async Task RSH1b3_UpdateDeviceRegistration()
        {
            var client = await SandboxRestClient();
            var deviceId = "test-device-update-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("token-v1")));

                var updated = await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("token-v2")));

                updated.Id.Should().Be(deviceId);
                ((string)updated.Push.Recipient["deviceToken"]).Should().Be("token-v2");

                Func<Task<DeviceDetails>> fetchUpdated = async () =>
                {
                    var result = await client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
                    if (result.IsFailure)
                    {
                        return null;
                    }

                    var token = (string)result.Value.Push?.Recipient?["deviceToken"];
                    return token == "token-v2" ? result.Value : null;
                };

                var retrieved = await UtsSandbox.WallClockPollUntil(
                    fetchUpdated,
                    $"the update to device '{deviceId}' to be readable");

                ((string)retrieved.Push.Recipient["deviceToken"]).Should().Be("token-v2");
            }
            finally
            {
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH1b1/get-unknown-device-error-0
        [Fact]
        public async Task RSH1b1_GetUnknownDeviceError()
        {
            var client = await SandboxRestClient();

            var deviceId = "nonexistent-device-" + UtsSandbox.RandomId();

            // The spec's "FAILS WITH error", which this SDK satisfies by throwing.
            //
            // Worth knowing, because the signature says otherwise: GetAsync returns
            // Task<Result<DeviceDetails>> and PushAdmin.cs:345 has an explicit
            // `if (response.StatusCode == NotFound) return Result.Fail(...)` branch — but that
            // branch is unreachable. The 404 comes back from _restClient.ExecuteRequest, which
            // throws on an error status (NoExceptionOnHttpError is set only on the paginated
            // request path), so the exception escapes before the branch is reached. The spec point
            // is met either way, so this is a note rather than a deviation; see the "Investigated
            // and not defects" section of Uts/deviations.md.
            Func<Task> act = () => client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(404);
        }

        // UTS: rest/integration/RSH1b2/list-devices-filtered-0
        [Fact]
        public async Task RSH1b2_ListDevicesFiltered()
        {
            var client = await SandboxRestClient();
            var deviceId = "test-device-list-" + UtsSandbox.RandomId();

            // The control. Without a second registration the spec's "exactly one item" passes even if
            // the deviceId filter were dropped on the wire, because a freshly provisioned app may hold
            // nothing else.
            var decoyDeviceId = "test-device-list-decoy-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "android",
                    "tablet",
                    GcmRecipient("test-token")));

                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    decoyDeviceId,
                    "android",
                    "tablet",
                    GcmRecipient("test-token")));

                Func<Task<PaginatedResult<DeviceDetails>>> fetchAll = async () =>
                {
                    var page = await client.Push.Admin.DeviceRegistrations.List(
                        ListDeviceDetailsRequest.Empty(1000));
                    var ids = page.Items.Select(d => d.Id).ToList();
                    return ids.Contains(deviceId) && ids.Contains(decoyDeviceId) ? page : null;
                };

                var unfiltered = await UtsSandbox.WallClockPollUntil(
                    fetchAll,
                    "both the target and the decoy device registrations to be listable");

                var result = await client.Push.Admin.DeviceRegistrations.List(
                    ListDeviceDetailsRequest.WithDeviceId(deviceId));

                // "result IS PaginatedResult" is static: List returns PaginatedResult<DeviceDetails>.
                result.Should().NotBeNull();
                result.Items.Should().HaveCount(1);
                result.Items[0].Id.Should().Be(deviceId);
                result.Items[0].Platform.Should().Be("android");

                result.Items.Count.Should().BeLessThan(
                    unfiltered.Items.Count,
                    "the deviceId filter must narrow the list; an unrecognised query parameter would "
                    + "be dropped by the server and the unfiltered page returned instead");
            }
            finally
            {
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(decoyDeviceId);
            }
        }

        // UTS: rest/integration/RSH1b2/list-devices-pagination-1
        [Fact]
        public async Task RSH1b2_ListDevicesPagination()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-list-" + UtsSandbox.RandomId();
            var deviceIds = new List<string>();

            // The control: a fourth device under a different clientId, which the clientId filter must
            // exclude. Without it the spec's "at most two items" and "hasNext" hold whether or not the
            // filter reached the server.
            var decoyClientId = "test-client-list-decoy-" + UtsSandbox.RandomId();
            var decoyDeviceId = "test-device-limit-decoy-" + UtsSandbox.RandomId();

            try
            {
                for (var i = 1; i <= 3; i++)
                {
                    var deviceId = "test-device-limit-" + i + "-" + UtsSandbox.RandomId();
                    deviceIds.Add(deviceId);
                    await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                        deviceId,
                        "ios",
                        "phone",
                        ApnsRecipient("token-" + i),
                        clientId));
                }

                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    decoyDeviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("token-decoy"),
                    decoyClientId));

                Func<Task<PaginatedResult<DeviceDetails>>> fetchAll = async () =>
                {
                    var page = await client.Push.Admin.DeviceRegistrations.List(
                        ListDeviceDetailsRequest.Empty(1000));
                    var ids = page.Items.Select(d => d.Id).ToList();
                    var allPresent = deviceIds.TrueForAll(id => ids.Contains(id));
                    return allPresent && ids.Contains(decoyDeviceId) ? page : null;
                };

                var unfiltered = await UtsSandbox.WallClockPollUntil(
                    fetchAll,
                    "all four device registrations to be listable");

                var filtered = await client.Push.Admin.DeviceRegistrations.List(
                    ListDeviceDetailsRequest.WithClientId(clientId));

                filtered.Items.Should().HaveCount(
                    3,
                    "the clientId filter must narrow the list to the three devices registered under it");
                filtered.Items.Should().OnlyContain(d => d.ClientId == clientId);
                filtered.Items.Count.Should().BeLessThan(unfiltered.Items.Count);

                var result = await client.Push.Admin.DeviceRegistrations.List(
                    ListDeviceDetailsRequest.WithClientId(clientId, 2));

                result.Items.Count.Should().BeLessOrEqualTo(2);
                result.HasNext.Should().BeTrue();
            }
            finally
            {
                foreach (var deviceId in deviceIds)
                {
                    await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
                }

                await client.Push.Admin.DeviceRegistrations.RemoveAsync(decoyDeviceId);
            }
        }

        // UTS: rest/integration/RSH1b4/remove-device-0
        [Fact]
        public async Task RSH1b4_RemoveDevice()
        {
            var client = await SandboxRestClient();
            var deviceId = "test-device-remove-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("test-token")));

                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);

                // The spec's "get FAILS WITH error; error.statusCode == 404". Device deletion is
                // asynchronous, so the 404 is polled for rather than asserted on the first read.
                // The 404 is thrown, not returned (see RSH1b1), and WallClockPollUntil treats an
                // exception as "not yet" — so the success condition has to catch it here, or the
                // poll would spin until its deadline on the very outcome it is waiting for.
                Func<Task<bool>> goneWith404 = async () =>
                {
                    try
                    {
                        await client.Push.Admin.DeviceRegistrations.GetAsync(deviceId);
                        return false;
                    }
                    catch (AblyException ex)
                    {
                        return (int?)ex.ErrorInfo.StatusCode == 404;
                    }
                };

                await UtsSandbox.WallClockPollUntil(
                    goneWith404,
                    $"the removed device '{deviceId}' to report not found");
            }
            finally
            {
                // The removal is the test step; this only covers a failure before it ran.
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH1b4/remove-nonexistent-device-1
        [Fact]
        public async Task RSH1b4_RemoveNonexistentDevice()
        {
            var client = await SandboxRestClient();

            Func<Task> remove = () => client.Push.Admin.DeviceRegistrations.RemoveAsync(
                "nonexistent-device-" + UtsSandbox.RandomId());

            await remove.Should().NotThrowAsync();
        }

        // UTS: rest/integration/RSH1b5/remove-where-clientid-0
        [Fact]
        public async Task RSH1b5_RemoveWhereClientId()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-removeWhere-" + UtsSandbox.RandomId();
            var deviceIds = new List<string>();

            // The control: a device under a different clientId which removeWhere must leave behind. It
            // is also what makes the spec's "the filtered list is empty" meaningful — if the clientId
            // filter were dropped on either call the surviving decoy would show up in the list.
            var decoyClientId = "test-client-removeWhere-decoy-" + UtsSandbox.RandomId();
            var decoyDeviceId = "test-device-rw-decoy-" + UtsSandbox.RandomId();

            try
            {
                for (var i = 1; i <= 2; i++)
                {
                    var deviceId = "test-device-rw-" + i + "-" + UtsSandbox.RandomId();
                    deviceIds.Add(deviceId);
                    await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                        deviceId,
                        "ios",
                        "phone",
                        ApnsRecipient("token-" + i),
                        clientId));
                }

                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    decoyDeviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("token-decoy"),
                    decoyClientId));

                Func<Task<bool>> allVisible = async () =>
                {
                    var page = await client.Push.Admin.DeviceRegistrations.List(
                        ListDeviceDetailsRequest.Empty(1000));
                    var ids = page.Items.Select(d => d.Id).ToList();
                    return deviceIds.TrueForAll(id => ids.Contains(id)) && ids.Contains(decoyDeviceId);
                };

                await UtsSandbox.WallClockPollUntil(
                    allVisible,
                    "all three device registrations to be listable before the bulk removal");

                await client.Push.Admin.DeviceRegistrations.RemoveWhereAsync(
                    new Dictionary<string, string> { { "clientId", clientId } });

                Func<Task<bool>> noneLeft = async () =>
                {
                    var page = await client.Push.Admin.DeviceRegistrations.List(
                        ListDeviceDetailsRequest.WithClientId(clientId));
                    return page.Items.Count == 0;
                };

                await UtsSandbox.WallClockPollUntil(
                    noneLeft,
                    $"the devices registered under '{clientId}' to be removed");

                var survivors = await client.Push.Admin.DeviceRegistrations.List(
                    ListDeviceDetailsRequest.WithClientId(decoyClientId));

                survivors.Items.Select(d => d.Id).Should().Contain(
                    decoyDeviceId,
                    "removeWhere must only remove the devices matching its filter");
            }
            finally
            {
                foreach (var deviceId in deviceIds)
                {
                    await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
                }

                await client.Push.Admin.DeviceRegistrations.RemoveAsync(decoyDeviceId);
            }
        }

        // UTS: rest/integration/RSH1c3/save-and-list-subscriptions-0
        [Fact]
        public async Task RSH1c3_SaveAndListSubscriptions()
        {
            var client = await SandboxRestClient();
            var deviceId = "test-device-sub-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-sub-" + UtsSandbox.RandomId();

            // The control: a second subscription for the same device on a different channel, which the
            // channel filter must exclude.
            var decoyChannelName = "pushenabled:test-sub-decoy-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceRegistration(
                    deviceId,
                    "ios",
                    "phone",
                    ApnsRecipient("test-token")));

                var saved = await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForDevice(channelName, deviceId));

                // "saved IS PushChannelSubscription" is static: SaveAsync returns one.
                saved.Should().NotBeNull();
                saved.Channel.Should().Be(channelName);
                saved.DeviceId.Should().Be(deviceId);

                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForDevice(decoyChannelName, deviceId));

                Func<Task<PaginatedResult<PushChannelSubscription>>> fetchAll = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithDeviceId(deviceId: deviceId, limit: 1000));
                    var channels = page.Items.Select(s => s.Channel).ToList();
                    return channels.Contains(channelName) && channels.Contains(decoyChannelName)
                        ? page
                        : null;
                };

                var unfiltered = await UtsSandbox.WallClockPollUntil(
                    fetchAll,
                    "both the target and the decoy channel subscriptions to be listable");

                var filter = ListSubscriptionsRequest.WithDeviceId(deviceId: deviceId);
                filter.Channel = channelName;
                var result = await client.Push.Admin.ChannelSubscriptions.ListAsync(filter);

                // "result IS PaginatedResult" is static here too.
                result.Should().NotBeNull();
                result.Items.Should().HaveCountGreaterOrEqualTo(1);

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

                result.Items.Should().OnlyContain(
                    s => s.Channel == channelName,
                    "the channel filter must narrow the list");
                result.Items.Count.Should().BeLessThan(
                    unfiltered.Items.Count,
                    "an unrecognised query parameter would be dropped by the server and the "
                    + "unfiltered page returned instead");
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForDevice(channelName, deviceId));
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForDevice(decoyChannelName, deviceId));
                await client.Push.Admin.DeviceRegistrations.RemoveAsync(deviceId);
            }
        }

        // UTS: rest/integration/RSH1c3/save-subscription-clientid-1
        [Fact]
        public async Task RSH1c3_SaveSubscriptionClientId()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-sub-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-clientsub-" + UtsSandbox.RandomId();

            try
            {
                var saved = await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));

                saved.Should().NotBeNull();
                saved.Channel.Should().Be(channelName);
                saved.ClientId.Should().Be(clientId);
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));
            }
        }

        // UTS: rest/integration/RSH1c2/list-channels-with-subscriptions-0
        [Fact]
        public async Task RSH1c2_ListChannelsWithSubscriptions()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-lc-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-listchannels-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));

                // NOTE: the spec's listChannels({}) is an unparameterised request, which this SDK sends
                // as direction=backwards&limit=100 (PaginatedRequestParams.GetParameters). The
                // membership assertion is therefore against the first page only, exactly as the spec
                // writes it.
                Func<Task<PaginatedResult<string>>> fetchChannels = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListChannelsAsync(
                        new PaginatedRequestParams());
                    return page.Items.Contains(channelName) ? page : null;
                };

                var result = await UtsSandbox.WallClockPollUntil(
                    fetchChannels,
                    $"the channel '{channelName}' to appear in listChannels");

                result.Should().NotBeNull();
                result.Items.Should().Contain(channelName);
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));
            }
        }

        // UTS: rest/integration/RSH1c4/remove-channel-subscription-0
        [Fact]
        public async Task RSH1c4_RemoveChannelSubscription()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-rm-" + UtsSandbox.RandomId();
            var channelName = "pushenabled:test-remove-" + UtsSandbox.RandomId();

            // The control: a second subscription on the same channel under a different clientId, which
            // remove must leave behind. It is also what makes the spec's "the list is empty" an
            // assertion about the clientId filter rather than about there being nothing to find.
            var decoyClientId = "test-client-rm-decoy-" + UtsSandbox.RandomId();

            try
            {
                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));
                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));

                // Two queries, not one. The server rejects an unfiltered subscription list —
                // "expected parameter 'deviceId', 'clientId', 'deviceClientId', and/or 'channel'" —
                // so each clientId has to be asked for separately, and a single page filtered to
                // one of them could never contain the other.
                Func<string, Task<bool>> visible = async id =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(clientId: id, limit: 1000));
                    return page.Items.Any(s => s.Channel == channelName && s.ClientId == id);
                };

                Func<Task<bool>> bothVisible = async () =>
                    await visible(clientId) && await visible(decoyClientId);

                await UtsSandbox.WallClockPollUntil(
                    bothVisible,
                    "both channel subscriptions to be listable before the removal");

                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, clientId));

                // Subscription removal is documented as asynchronous, so the empty result is polled
                // for rather than read once.
                Func<Task<bool>> removed = async () =>
                {
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(channelName, clientId));
                    return page.Items.Count == 0;
                };

                await UtsSandbox.WallClockPollUntil(
                    removed,
                    $"the subscription for '{clientId}' on '{channelName}' to be removed");

                var survivors = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                    ListSubscriptionsRequest.WithClientId(channelName, decoyClientId));

                survivors.Items.Select(s => s.ClientId).Should().Contain(
                    decoyClientId,
                    "remove must only delete the subscription its attributes identify");
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(channelName, decoyClientId));
            }
        }

        // UTS: rest/integration/RSH1c4/remove-nonexistent-subscription-1
        [Fact]
        public async Task RSH1c4_RemoveNonexistentSubscription()
        {
            var client = await SandboxRestClient();

            Func<Task> remove = () => client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                PushChannelSubscription.ForClientId(
                    "pushenabled:nonexistent-" + UtsSandbox.RandomId(),
                    "nonexistent-client"));

            await remove.Should().NotThrowAsync();
        }

        // UTS: rest/integration/RSH1c5/remove-where-subscriptions-0
        [Fact]
        public async Task RSH1c5_RemoveWhereSubscriptions()
        {
            var client = await SandboxRestClient();
            var clientId = "test-client-rwsub-" + UtsSandbox.RandomId();
            var channelNames = new List<string>();

            // The control: a subscription under a different clientId which removeWhere must leave
            // behind, so that the spec's empty filtered list cannot pass with the clientId filter
            // dropped.
            var decoyClientId = "test-client-rwsub-decoy-" + UtsSandbox.RandomId();
            var decoyChannelName = "pushenabled:test-rwsub-decoy-" + UtsSandbox.RandomId();

            try
            {
                for (var i = 1; i <= 2; i++)
                {
                    var channelName = "pushenabled:test-rwsub-" + i + "-" + UtsSandbox.RandomId();
                    channelNames.Add(channelName);
                    await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                        PushChannelSubscription.ForClientId(channelName, clientId));
                }

                await client.Push.Admin.ChannelSubscriptions.SaveAsync(
                    PushChannelSubscription.ForClientId(decoyChannelName, decoyClientId));

                // Two queries, not one — see RSH1c4: an unfiltered list is rejected by the server,
                // and a page filtered to one clientId can never show the decoy's.
                Func<Task<bool>> allVisible = async () =>
                {
                    var minePage = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(clientId: clientId, limit: 1000));
                    var mine = minePage.Items
                        .Where(s => s.ClientId == clientId)
                        .Select(s => s.Channel)
                        .ToList();

                    var decoyPage = await client.Push.Admin.ChannelSubscriptions.ListAsync(
                        ListSubscriptionsRequest.WithClientId(clientId: decoyClientId, limit: 1000));
                    var decoyPresent = decoyPage.Items.Any(
                        s => s.ClientId == decoyClientId && s.Channel == decoyChannelName);

                    return channelNames.TrueForAll(name => mine.Contains(name)) && decoyPresent;
                };

                await UtsSandbox.WallClockPollUntil(
                    allVisible,
                    "all three channel subscriptions to be listable before the bulk removal");

                await client.Push.Admin.ChannelSubscriptions.RemoveWhereAsync(
                    new Dictionary<string, string> { { "clientId", clientId } });

                Func<Task<bool>> noneLeft = async () =>
                {
                    var filter = ListSubscriptionsRequest.WithClientId(clientId: clientId);
                    filter.ClientId = clientId;
                    var page = await client.Push.Admin.ChannelSubscriptions.ListAsync(filter);
                    return page.Items.Count == 0;
                };

                await UtsSandbox.WallClockPollUntil(
                    noneLeft,
                    $"the subscriptions for '{clientId}' to be removed");

                var survivorFilter = ListSubscriptionsRequest.WithClientId(clientId: clientId);
                survivorFilter.ClientId = decoyClientId;
                var survivors = await client.Push.Admin.ChannelSubscriptions.ListAsync(survivorFilter);

                survivors.Items.Select(s => s.Channel).Should().Contain(
                    decoyChannelName,
                    "removeWhere must only remove the subscriptions matching its filter");
            }
            finally
            {
                await client.Push.Admin.ChannelSubscriptions.RemoveAsync(
                    PushChannelSubscription.ForClientId(decoyChannelName, decoyClientId));
            }
        }

        /// <summary>
        /// The spec's <c>DeviceDetails(...)</c> constructor. Each call site keeps the spec's own
        /// platform, form factor and recipient visible.
        /// </summary>
        private static DeviceDetails DeviceRegistration(
            string deviceId,
            string platform,
            string formFactor,
            JObject recipient,
            string clientId = null)
        {
            return new DeviceDetails
            {
                Id = deviceId,
                Platform = platform,
                FormFactor = formFactor,
                ClientId = clientId,
                Push = new DeviceDetails.PushData { Recipient = recipient },
            };
        }

        private static JObject ApnsRecipient(string deviceToken)
            => JObject.FromObject(new { transportType = "apns", deviceToken });

        private static JObject GcmRecipient(string registrationToken)
            => JObject.FromObject(new { transportType = "gcm", registrationToken });
    }
}
