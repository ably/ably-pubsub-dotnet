// PushDeviceRegistrations Tests (RSH1b, RSH1b1, RSH1b2, RSH1b3, RSH1b4, RSH1b5)
// Derived from uts/rest/unit/push/push_device_registrations.md
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
    public class PushDeviceRegistrationsTests : UtsUnitTestBase
    {
        public PushDeviceRegistrationsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1b1/get-device-details-0
        [UtsFact]
        [Trait("spec", "RSH1b1")]
        public async Task RSH1b1_GetDeviceDetails()
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
                        ["id"] = "device-001",
                        ["clientId"] = "client-abc",
                        ["formFactor"] = "phone",
                        ["platform"] = "ios",
                        ["metadata"] = new JObject { ["model"] = "iPhone 14" },
                        ["push"] = new JObject
                        {
                            ["recipient"] = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-123" },
                            ["state"] = "Active",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.GetAsync("device-001");
            result.IsSuccess.Should().BeTrue("AWAIT deviceRegistrations.get() succeeds ({0})", result.Error?.Message);
            var device = result.Value;

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/deviceRegistrations/" + Uri.EscapeDataString("device-001"));

            // ASSERT device IS DeviceDetails: guaranteed at compile time (Result<DeviceDetails>.Value)
            device.Should().NotBeNull();
            device.Id.Should().Be("device-001");
            device.ClientId.Should().Be("client-abc");
            device.FormFactor.Should().Be("phone");
            device.Platform.Should().Be("ios");
            ((string)device.Metadata["model"]).Should().Be("iPhone 14");
            ((string)device.Push.Recipient["transportType"]).Should().Be("apns");
            device.Push.State.Should().Be("Active");
        }

        // UTS: rest/unit/RSH1b1/get-unknown-device-error-1
        [UtsFact]
        [Trait("spec", "RSH1b1")]
        public async Task RSH1b1_GetUnknownDeviceError()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(404, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40400,
                            ["statusCode"] = 404,
                            ["message"] = "Device not found",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Push.Admin.DeviceRegistrations.GetAsync("nonexistent-device");
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40400);
            ((int?)error.StatusCode).Should().Be(404);
        }

        // UTS: rest/unit/RSH1b1/get-url-encodes-deviceid-2
        // DEVIATION: see deviations.md
        [DeviationFact("RSH1b1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSH1b1")]
        public async Task RSH1b1_GetUrlEncodesDeviceid()
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
                        ["id"] = "device/with special:chars",
                        ["platform"] = "ios",
                        ["formFactor"] = "phone",
                        ["push"] = new JObject
                        {
                            ["recipient"] = new JObject(),
                            ["state"] = "Active",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.GetAsync("device/with special:chars");
            result.IsSuccess.Should().BeTrue("AWAIT deviceRegistrations.get() succeeds ({0})", result.Error?.Message);

            // Assertions
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations/" + Uri.EscapeDataString("device/with special:chars"));
        }

        // UTS: rest/unit/RSH1b2/list-filtered-by-deviceid-0
        [UtsFact]
        [Trait("spec", "RSH1b2")]
        public async Task RSH1b2_ListFilteredByDeviceid()
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
                            ["id"] = "device-001",
                            ["clientId"] = "client-abc",
                            ["platform"] = "ios",
                            ["formFactor"] = "phone",
                            ["push"] = new JObject { ["recipient"] = new JObject(), ["state"] = "Active" },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.WithDeviceId("device-001"));

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Url.Path.Should().Be("/push/deviceRegistrations");
            request.Url.QueryParams["deviceId"].Should().Be("device-001");

            // ASSERT result IS PaginatedResult: guaranteed at compile time (List returns PaginatedResult<DeviceDetails>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(1);
            // ASSERT result.items[0] IS DeviceDetails: guaranteed at compile time (Items is List<DeviceDetails>)
            result.Items[0].Id.Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1b2/list-filtered-by-clientid-1
        [UtsFact]
        [Trait("spec", "RSH1b2")]
        public async Task RSH1b2_ListFilteredByClientid()
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
                            ["id"] = "device-001",
                            ["clientId"] = "client-abc",
                            ["platform"] = "ios",
                            ["formFactor"] = "phone",
                            ["push"] = new JObject { ["recipient"] = new JObject(), ["state"] = "Active" },
                        },
                        new JObject
                        {
                            ["id"] = "device-002",
                            ["clientId"] = "client-abc",
                            ["platform"] = "android",
                            ["formFactor"] = "tablet",
                            ["push"] = new JObject { ["recipient"] = new JObject(), ["state"] = "Active" },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.WithClientId("client-abc"));

            // Assertions
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("client-abc");
            result.Items.Should().HaveCount(2);
            result.Items[0].ClientId.Should().Be("client-abc");
            result.Items[1].ClientId.Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1b2/list-with-limit-param-2
        [UtsFact]
        [Trait("spec", "RSH1b2")]
        public async Task RSH1b2_ListWithLimitParam()
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
                            ["id"] = "device-001",
                            ["platform"] = "ios",
                            ["formFactor"] = "phone",
                            ["push"] = new JObject { ["recipient"] = new JObject(), ["state"] = "Active" },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Push.Admin.DeviceRegistrations.List(ListDeviceDetailsRequest.Empty(2));

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("2");
        }

        // UTS: rest/unit/RSH1b3/save-put-device-details-0
        [UtsFact]
        [Trait("spec", "RSH1b3")]
        public async Task RSH1b3_SavePutDeviceDetails()
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
                        ["id"] = "device-001",
                        ["clientId"] = "client-abc",
                        ["platform"] = "ios",
                        ["formFactor"] = "phone",
                        ["metadata"] = new JObject(),
                        ["push"] = new JObject
                        {
                            ["recipient"] = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-123" },
                            ["state"] = "Active",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var device = new DeviceDetails
            {
                Id = "device-001",
                ClientId = "client-abc",
                Platform = "ios",
                FormFactor = "phone",
                Push = new DeviceDetails.PushData
                {
                    Recipient = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-123" },
                },
            };

            var result = await client.Push.Admin.DeviceRegistrations.SaveAsync(device);

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("PUT");
            request.Url.Path.Should().Be("/push/deviceRegistrations/" + Uri.EscapeDataString("device-001"));

            var body = JObject.Parse(request.BodyText);
            ((string)body["id"]).Should().Be("device-001");
            ((string)body["clientId"]).Should().Be("client-abc");
            ((string)body["platform"]).Should().Be("ios");
            ((string)body["formFactor"]).Should().Be("phone");
            ((string)body["push"]["recipient"]["transportType"]).Should().Be("apns");

            // ASSERT result IS DeviceDetails: guaranteed at compile time (SaveAsync returns DeviceDetails)
            result.Should().NotBeNull();
            result.Id.Should().Be("device-001");
            result.Push.State.Should().Be("Active");
        }

        // UTS: rest/unit/RSH1b3/save-updates-existing-1
        [UtsFact]
        [Trait("spec", "RSH1b3")]
        public async Task RSH1b3_SaveUpdatesExisting()
        {
            // Setup
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First save — initial registration
                        req.RespondWith(200, new JObject
                        {
                            ["id"] = "device-001",
                            ["platform"] = "ios",
                            ["formFactor"] = "phone",
                            ["push"] = new JObject
                            {
                                ["recipient"] = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-old" },
                                ["state"] = "Active",
                            },
                        });
                    }
                    else
                    {
                        // Second save — update
                        req.RespondWith(200, new JObject
                        {
                            ["id"] = "device-001",
                            ["platform"] = "ios",
                            ["formFactor"] = "phone",
                            ["push"] = new JObject
                            {
                                ["recipient"] = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-new" },
                                ["state"] = "Active",
                            },
                        });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var device = new DeviceDetails
            {
                Id = "device-001",
                Platform = "ios",
                FormFactor = "phone",
                Push = new DeviceDetails.PushData
                {
                    Recipient = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-old" },
                },
            };

            var result1 = await client.Push.Admin.DeviceRegistrations.SaveAsync(device);

            var updatedDevice = new DeviceDetails
            {
                Id = "device-001",
                Platform = "ios",
                FormFactor = "phone",
                Push = new DeviceDetails.PushData
                {
                    Recipient = new JObject { ["transportType"] = "apns", ["deviceToken"] = "token-new" },
                },
            };

            var result2 = await client.Push.Admin.DeviceRegistrations.SaveAsync(updatedDevice);

            // Assertions
            ((string)result1.Push.Recipient["deviceToken"]).Should().Be("token-old");
            ((string)result2.Push.Recipient["deviceToken"]).Should().Be("token-new");
            Volatile.Read(ref requestCount).Should().Be(2);
        }

        // UTS: rest/unit/RSH1b3/save-error-propagated-2
        [UtsFact]
        [Trait("spec", "RSH1b3")]
        public async Task RSH1b3_SaveErrorPropagated()
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
                            ["message"] = "Invalid device details",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            var device = new DeviceDetails
            {
                Id = "device-001",
                Platform = "ios",
                FormFactor = "phone",
                Push = new DeviceDetails.PushData { Recipient = new JObject() },
            };

            Func<Task> act = () => client.Push.Admin.DeviceRegistrations.SaveAsync(device);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int?)error.StatusCode).Should().Be(400);
        }

        // UTS: rest/unit/RSH1b4/remove-delete-device-0
        [UtsFact]
        [Trait("spec", "RSH1b4")]
        public async Task RSH1b4_RemoveDeleteDevice()
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
            await client.Push.Admin.DeviceRegistrations.RemoveAsync("device-001");

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/deviceRegistrations/" + Uri.EscapeDataString("device-001"));
        }

        // UTS: rest/unit/RSH1b4/remove-nonexistent-succeeds-1
        [UtsFact]
        [Trait("spec", "RSH1b4")]
        public async Task RSH1b4_RemoveNonexistentSucceeds()
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
            // Should not throw — server returns success even for nonexistent devices
            await client.Push.Admin.DeviceRegistrations.RemoveAsync("nonexistent-device");
        }

        // UTS: rest/unit/RSH1b5/remove-where-clientid-0
        [UtsFact]
        [Trait("spec", "RSH1b5")]
        public async Task RSH1b5_RemoveWhereClientid()
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
            await client.Push.Admin.DeviceRegistrations.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = "client-abc" });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("DELETE");
            request.Url.Path.Should().Be("/push/deviceRegistrations");
            request.Url.QueryParams["clientId"].Should().Be("client-abc");
        }

        // UTS: rest/unit/RSH1b5/remove-where-deviceid-1
        [UtsFact]
        [Trait("spec", "RSH1b5")]
        public async Task RSH1b5_RemoveWhereDeviceid()
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
            await client.Push.Admin.DeviceRegistrations.RemoveWhereAsync(new Dictionary<string, string> { ["deviceId"] = "device-001" });

            // Assertions
            capturedRequests[0].Method.Should().Be("DELETE");
            capturedRequests[0].Url.Path.Should().Be("/push/deviceRegistrations");
            capturedRequests[0].Url.QueryParams["deviceId"].Should().Be("device-001");
        }

        // UTS: rest/unit/RSH1b5/remove-where-no-match-succeeds-2
        [UtsFact]
        [Trait("spec", "RSH1b5")]
        public async Task RSH1b5_RemoveWhereNoMatchSucceeds()
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
            // Should not throw — server returns success even with no matching devices
            await client.Push.Admin.DeviceRegistrations.RemoveWhereAsync(new Dictionary<string, string> { ["clientId"] = "nonexistent-client" });
        }
    }
}
