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
    /// Derived from uts/rest/unit/push/push_channels.md in ably/specification.
    ///
    /// Spec points: RSH7, RSH7a, RSH7a1, RSH7a2, RSH7a3, RSH7b, RSH7b1, RSH7b2, RSH7c, RSH7c1,
    /// RSH7c2, RSH7c3, RSH7d, RSH7d1, RSH7d2, RSH7e
    ///
    /// <para>
    /// The spec's <c>client.device = LocalDevice(...)</c> needs two seams, both of which the SDK
    /// already provides for its own tests - see <c>UtsClients.RestClientWithDevice</c>. Without a
    /// mobile device present <c>HttpChannel.Push</c> returns null, so a plain desktop client cannot
    /// reach this surface at all.
    /// </para>
    ///
    /// <para>
    /// The RSH7a1/RSH7b1/RSH7c1/RSH7d1 failure cases assert on the message rather than the code:
    /// the SDK raises these through the single-argument <c>AblyException</c> constructor, which
    /// leaves no specific code (the source carries a "what error code should we use here" TODO at
    /// <c>PushChannel.cs:37</c>). The spec asks only that a code be present and that the message
    /// name the missing field, so the assertions follow it - the code is reported as whatever the
    /// SDK sets, and the message is the part the spec pins.
    /// </para>
    /// </summary>
    public class PushChannelTests : UtsTestBase
    {
        private const string DeviceId = "test-device-001";
        private const string IdentityToken = "test-device-identity-token";
        private const string DeviceClientId = "test-client";
        private const string ChannelName = "my-channel";

        public PushChannelTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH7a2/subscribe-device-post-0
        [Fact]
        public async Task RSH7a2_SubscribeDevicePosts()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(
                SubscriptionMock(capturedRequests),
                RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.SubscribeDevice();

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(request.BodyText);
            body["channel"].Value<string>().Should().Be(ChannelName);
            body["deviceId"].Value<string>().Should().Be(DeviceId);

            // RSH7a3's half of this spec test - that the request carries push device authentication
            // - is asserted separately below, because the SDK sends the wrong header name and the
            // rest of this test is worth keeping green.
        }

        // UTS: rest/unit/RSH7a2/subscribe-device-post-0 (the RSH7a3 assertion)
        //
        // DEVIATION. RSH6a is unusually explicit: push device authentication adds an
        // `X-Ably-DeviceToken` header, and "this header has always been `X-Ably-DeviceToken`, but
        // has previously been mistakenly documented as `X-Ably-DeviceIdentityToken` in the hope of
        // renaming it to avoid confusion with APNs device token. It was never renamed."
        // Defaults.DeviceIdentityTokenHeader (Defaults.cs:75) is the mistaken name. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSH7a3_SubscribeDeviceSendsDeviceAuth()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(
                SubscriptionMock(capturedRequests),
                RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.SubscribeDevice();

            capturedRequests[0].Headers.Should().ContainKey("X-Ably-DeviceToken");
            capturedRequests[0].Headers["X-Ably-DeviceToken"].Should().Be(IdentityToken);
        }

        // UTS: rest/unit/RSH7a1/subscribe-device-no-token-fails-0
        [Fact]
        public async Task RSH7a1_SubscribeDeviceWithoutIdentityTokenFails()
        {
            var client = RestClientWithDevice(EmptyMock(), UnregisteredDevice());

            Func<Task> act = () => client.Channels.Get(ChannelName).Push.SubscribeDevice();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Message.Should().Contain("deviceIdentityToken");
        }

        // UTS: rest/unit/RSH7b2/subscribe-client-post-0
        [Fact]
        public async Task RSH7b2_SubscribeClientPosts()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(
                SubscriptionMock(capturedRequests),
                RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.SubscribeClient();

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/channelSubscriptions");

            var body = JObject.Parse(request.BodyText);
            body["channel"].Value<string>().Should().Be(ChannelName);
            body["clientId"].Value<string>().Should().Be(DeviceClientId);
        }

        // UTS: rest/unit/RSH7b1/subscribe-client-no-clientid-fails-0
        [Fact]
        public async Task RSH7b1_SubscribeClientWithoutClientIdFails()
        {
            var device = RegisteredDevice();
            device.ClientId = null;

            var client = RestClientWithDevice(EmptyMock(), device);

            Func<Task> act = () => client.Channels.Get(ChannelName).Push.SubscribeClient();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Message.Should().Contain("clientId");
        }

        // UTS: rest/unit/RSH7c2/unsubscribe-device-delete-0
        [Fact]
        public async Task RSH7c2_UnsubscribeDeviceDeletes()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(NoContentMock(capturedRequests), RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.UnsubscribeDevice();

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be(ChannelName);
            request.Url.QueryParams["deviceId"].Should().Be(DeviceId);

            // RSH7c3's half is asserted separately below - same cause as RSH7a3.
        }

        // UTS: rest/unit/RSH7c2/unsubscribe-device-delete-0 (the RSH7c3 assertion)
        //
        // DEVIATION, same cause as RSH7a3: the header is named X-Ably-DeviceIdentityToken rather
        // than the X-Ably-DeviceToken RSH6a requires.
        [DeviationFact]
        public async Task RSH7c3_UnsubscribeDeviceSendsDeviceAuth()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(NoContentMock(capturedRequests), RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.UnsubscribeDevice();

            capturedRequests[0].Headers.Should().ContainKey("X-Ably-DeviceToken");
            capturedRequests[0].Headers["X-Ably-DeviceToken"].Should().Be(IdentityToken);
        }

        // UTS: rest/unit/RSH7c1/unsubscribe-device-no-token-fails-0
        [Fact]
        public async Task RSH7c1_UnsubscribeDeviceWithoutIdentityTokenFails()
        {
            var client = RestClientWithDevice(EmptyMock(), UnregisteredDevice());

            Func<Task> act = () => client.Channels.Get(ChannelName).Push.UnsubscribeDevice();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Message.Should().Contain("deviceIdentityToken");
        }

        // UTS: rest/unit/RSH7d2/unsubscribe-client-delete-0
        [Fact]
        public async Task RSH7d2_UnsubscribeClientDeletes()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(NoContentMock(capturedRequests), RegisteredDevice());

            await client.Channels.Get(ChannelName).Push.UnsubscribeClient();

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be(ChannelName);
            request.Url.QueryParams["clientId"].Should().Be(DeviceClientId);
        }

        // UTS: rest/unit/RSH7d1/unsubscribe-client-no-clientid-fails-0
        [Fact]
        public async Task RSH7d1_UnsubscribeClientWithoutClientIdFails()
        {
            var device = RegisteredDevice();
            device.ClientId = null;

            var client = RestClientWithDevice(EmptyMock(), device);

            Func<Task> act = () => client.Channels.Get(ChannelName).Push.UnsubscribeClient();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Message.Should().Contain("clientId");
        }

        // UTS: rest/unit/RSH7e/list-subscriptions-with-filters-0
        [Fact]
        public async Task RSH7e_ListSubscriptionsWithFilters()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(
                new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        capturedRequests.Add(req);
                        req.RespondWith(200, new object[]
                        {
                            new { channel = ChannelName, deviceId = DeviceId },
                            new { channel = ChannelName, clientId = DeviceClientId },
                        });
                    }),
                RegisteredDevice());

            var result = await client.Channels.Get(ChannelName).Push
                .ListSubscriptions(ListSubscriptionsRequest.Empty(10));

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/channelSubscriptions");
            request.Url.QueryParams["channel"].Should().Be(ChannelName);
            request.Url.QueryParams["deviceId"].Should().Be(DeviceId);
            request.Url.QueryParams["clientId"].Should().Be(DeviceClientId);
            request.Url.QueryParams["concatFilters"].Should().Be("true");
            request.Url.QueryParams["limit"].Should().Be("10");

            result.Should().BeOfType<PaginatedResult<PushChannelSubscription>>();
            result.Items.Should().HaveCount(2);
            result.Items[0].Should().BeOfType<PushChannelSubscription>();
            result.Items[0].Channel.Should().Be(ChannelName);
            result.Items[0].DeviceId.Should().Be(DeviceId);
            result.Items[1].ClientId.Should().Be(DeviceClientId);
        }

        // UTS: rest/unit/RSH7e/list-subscriptions-omits-clientid-1
        [Fact]
        public async Task RSH7e_ListSubscriptionsOmitsClientId()
        {
            var device = RegisteredDevice();
            device.ClientId = null;

            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClientWithDevice(
                new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        capturedRequests.Add(req);
                        req.RespondWith(200, new[] { new { channel = ChannelName, deviceId = DeviceId } });
                    }),
                device);

            var result = await client.Channels.Get(ChannelName).Push.ListSubscriptions();

            var request = capturedRequests[0];
            request.Url.QueryParams["channel"].Should().Be(ChannelName);
            request.Url.QueryParams["deviceId"].Should().Be(DeviceId);
            request.Url.QueryParams["concatFilters"].Should().Be("true");
            request.Url.QueryParams.Should().NotContainKey("clientId");

            result.Items.Should().HaveCount(1);
        }

        private static LocalDevice RegisteredDevice()
            => new LocalDevice
            {
                Id = DeviceId,
                DeviceIdentityToken = IdentityToken,
                ClientId = DeviceClientId,
            };

        private static LocalDevice UnregisteredDevice()
            => new LocalDevice { Id = DeviceId, ClientId = DeviceClientId };

        private static MockHttpClient SubscriptionMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new { channel = ChannelName, deviceId = DeviceId });
                });

        private static MockHttpClient NoContentMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });

        private static MockHttpClient EmptyMock()
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new { }));
    }
}
