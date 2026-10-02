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
    /// Derived from uts/rest/unit/push/push_device_registrations.md in ably/specification.
    ///
    /// Spec points: RSH1b1, RSH1b2, RSH1b3, RSH1b4, RSH1b5
    ///
    /// <para>
    /// Three shape differences, all idiomatic rather than deviations:
    /// <c>get()</c> is <c>GetAsync</c> and returns a <c>Result&lt;DeviceDetails&gt;</c> rather than
    /// the device itself; <c>list(params)</c> is <c>List(ListDeviceDetailsRequest)</c>, built through
    /// the named factories rather than from a dictionary; and <c>remove()</c> is
    /// <c>RemoveAsync</c>. The members live on <c>IDeviceRegistrations</c>, which <c>PushAdmin</c>
    /// implements explicitly, so the calls go through the interface.
    /// </para>
    ///
    /// <para>
    /// On RSH1b1's not-found case, see N2 in Uts/deviations.md: the <c>Result.Fail</c> branch in
    /// <c>PushAdmin</c> is unreachable because the 404 throws first. The spec asks only for an
    /// error, so the test asserts the thrown one.
    /// </para>
    /// </summary>
    public class PushDeviceRegistrationsTests : UtsTestBase
    {
        public PushDeviceRegistrationsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1b1/get-device-details-0
        [Fact]
        public async Task RSH1b1_GetDeviceDetails()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, DeviceBody("device-001", "client-abc", "token-123"));
                }));

            var device = await Registrations(client).GetAsync("device-001");

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations/device-001");

            device.IsSuccess.Should().BeTrue();
            var details = device.Value;
            details.Should().BeOfType<DeviceDetails>();
            details.Id.Should().Be("device-001");
            details.ClientId.Should().Be("client-abc");
            details.FormFactor.Should().Be("phone");
            details.Platform.Should().Be("ios");
            details.Metadata["model"].Value<string>().Should().Be("iPhone 14");
            details.Push.Recipient["transportType"].Value<string>().Should().Be("apns");
            details.Push.State.Should().Be("Active");
        }

        // UTS: rest/unit/RSH1b1/get-unknown-device-error-1
        [Fact]
        public async Task RSH1b1_GetUnknownDeviceError()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(404, new
                {
                    error = new { code = 40400, statusCode = 404, message = "Device not found" },
                })));

            Func<Task> act = () => Registrations(client).GetAsync("nonexistent-device");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40400);
            ((int)error.StatusCode.Value).Should().Be(404);
        }

        // UTS: rest/unit/RSH1b1/get-url-encodes-deviceid-2
        //
        // DEVIATION. The deviceId is interpolated into the path raw -
        // `$"/push/deviceRegistrations/{deviceId}"` at PushAdmin.cs:340 - so a deviceId containing
        // `/`, a space or `:` reaches the wire unescaped and addresses a different resource. Same
        // class of bug as D1 (`StatusAsync` and the channel name); recorded in Uts/deviations.md.
        [DeviationFact]
        public async Task RSH1b1_GetUrlEncodesDeviceId()
        {
            const string DeviceId = "device/with special:chars";

            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, DeviceBody(DeviceId, "client-abc", "token-123"));
                }));

            await Registrations(client).GetAsync(DeviceId);

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Url.Path
                .Should().Be("/push/deviceRegistrations/" + Uri.EscapeDataString(DeviceId));
        }

        // UTS: rest/unit/RSH1b2/list-filtered-by-deviceid-0
        [Fact]
        public async Task RSH1b2_ListFilteredByDeviceId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { DeviceBody("device-001", "client-abc", "token-123") });
                }));

            var result = await Registrations(client).List(ListDeviceDetailsRequest.WithDeviceId("device-001"));

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");

            result.Should().BeOfType<PaginatedResult<DeviceDetails>>();
            result.Items.Should().HaveCount(1);
            result.Items[0].Should().BeOfType<DeviceDetails>();
            result.Items[0].Id.Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1b2/list-filtered-by-clientid-1
        [Fact]
        public async Task RSH1b2_ListFilteredByClientId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        DeviceBody("device-001", "client-abc", "token-123"),
                        DeviceBody("device-002", "client-abc", "token-456"),
                    });
                }));

            var result = await Registrations(client).List(ListDeviceDetailsRequest.WithClientId("client-abc"));

            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
            result.Items.Should().HaveCount(2);
            result.Items[0].ClientId.Should().Be("client-abc");
            result.Items[1].ClientId.Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1b2/list-with-limit-param-2
        [Fact]
        public async Task RSH1b2_ListWithLimitParam()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        DeviceBody("device-001", "client-abc", "token-123"),
                        DeviceBody("device-002", "client-abc", "token-456"),
                    });
                }));

            await Registrations(client).List(ListDeviceDetailsRequest.Empty(2));

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("2");
        }

        // UTS: rest/unit/RSH1b3/save-put-device-details-0
        [Fact]
        public async Task RSH1b3_SavePutsDeviceDetails()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, DeviceBody("device-001", "client-abc", "token-123"));
                }));

            var result = await Registrations(client).SaveAsync(NewDevice("device-001", "client-abc", "token-123"));

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("PUT");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations/device-001");

            var body = JObject.Parse(capturedRequests[0].BodyText);
            body["id"].Value<string>().Should().Be("device-001");
            body["clientId"].Value<string>().Should().Be("client-abc");
            body["platform"].Value<string>().Should().Be("ios");
            body["formFactor"].Value<string>().Should().Be("phone");
            body["push"]["recipient"]["transportType"].Value<string>().Should().Be("apns");

            result.Should().BeOfType<DeviceDetails>();
            result.Id.Should().Be("device-001");
            result.Push.State.Should().Be("Active");
        }

        // UTS: rest/unit/RSH1b3/save-updates-existing-1
        [Fact]
        public async Task RSH1b3_SaveUpdatesExisting()
        {
            var requestCount = 0;
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    req.RespondWith(200, requestCount == 1
                        ? DeviceBody("device-001", "client-abc", "token-old")
                        : DeviceBody("device-001", "client-abc", "token-new"));
                }));

            var result1 = await Registrations(client).SaveAsync(NewDevice("device-001", "client-abc", "token-old"));
            var result2 = await Registrations(client).SaveAsync(NewDevice("device-001", "client-abc", "token-new"));

            result1.Push.Recipient["deviceToken"].Value<string>().Should().Be("token-old");
            result2.Push.Recipient["deviceToken"].Value<string>().Should().Be("token-new");
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSH1b3/save-error-propagated-2
        [Fact]
        public async Task RSH1b3_SaveErrorPropagated()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(400, new
                {
                    error = new { code = 40000, statusCode = 400, message = "Invalid device" },
                })));

            Func<Task> act = () => Registrations(client).SaveAsync(NewDevice("device-001", "client-abc", "token-123"));

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int)error.StatusCode.Value).Should().Be(400);
        }

        // UTS: rest/unit/RSH1b4/remove-delete-device-0
        [Fact]
        public async Task RSH1b4_RemoveDeletesDevice()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Registrations(client).RemoveAsync("device-001");

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations/device-001");
        }

        // UTS: rest/unit/RSH1b4/remove-nonexistent-succeeds-1
        [Fact]
        public async Task RSH1b4_RemoveNonexistentSucceeds()
        {
            var client = RestClient(NoContentMock(new List<PendingHttpRequest>()));

            Func<Task> act = () => Registrations(client).RemoveAsync("nonexistent-device");

            await act.Should().NotThrowAsync("the server reports success whether or not it matched");
        }

        // UTS: rest/unit/RSH1b5/remove-where-clientid-0
        [Fact]
        public async Task RSH1b5_RemoveWhereClientId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Registrations(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "clientId", "client-abc" },
            });

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1b5/remove-where-deviceid-1
        [Fact]
        public async Task RSH1b5_RemoveWhereDeviceId()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(NoContentMock(capturedRequests));

            await Registrations(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "deviceId", "device-001" },
            });

            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1b5/remove-where-no-match-succeeds-2
        [Fact]
        public async Task RSH1b5_RemoveWhereNoMatchSucceeds()
        {
            var client = RestClient(NoContentMock(new List<PendingHttpRequest>()));

            Func<Task> act = () => Registrations(client).RemoveWhereAsync(new Dictionary<string, string>
            {
                { "clientId", "nonexistent-client" },
            });

            await act.Should().NotThrowAsync();
        }

        private static IDeviceRegistrations Registrations(PubSubHttpClient client)
            => client.Push.Admin.DeviceRegistrations;

        private static MockHttpClient NoContentMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(204, null);
                });

        private static DeviceDetails NewDevice(string id, string clientId, string deviceToken)
            => new DeviceDetails
            {
                Id = id,
                ClientId = clientId,
                Platform = "ios",
                FormFactor = "phone",
                Metadata = new JObject { { "model", "iPhone 14" } },
                Push = new DeviceDetails.PushData
                {
                    Recipient = new JObject
                    {
                        { "transportType", "apns" },
                        { "deviceToken", deviceToken },
                    },
                },
            };

        private static object DeviceBody(string id, string clientId, string deviceToken) => new
        {
            id,
            clientId,
            formFactor = "phone",
            platform = "ios",
            metadata = new { model = "iPhone 14" },
            push = new
            {
                recipient = new { transportType = "apns", deviceToken },
                state = "Active",
            },
        };
    }
}
