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
    /// Derived from uts/rest/unit/push/push_admin_publish.md in ably/specification.
    ///
    /// Spec points: RSH1, RSH1a
    ///
    /// <para>
    /// The spec's <c>client.push</c> is <c>PubSubHttpClient.Push</c>, of type <c>PushHttp</c>, and
    /// <c>publish(recipient, data)</c> is <c>PushAdmin.PublishAsync(JObject, JObject)</c>. Recipient
    /// and payload are <c>JObject</c>s rather than language dictionaries, so the spec's object
    /// literals are written as <c>JObject</c>s. Idiomatic naming and typing, not deviations.
    /// </para>
    ///
    /// <para>
    /// RSH1's type assertions check the accessors resolve to the push admin surface. There is no
    /// separate <c>PushDeviceRegistrations</c>/<c>PushChannelSubscriptions</c> pair here -
    /// <c>PushAdmin</c> implements both <c>IDeviceRegistrations</c> and
    /// <c>IPushChannelSubscriptions</c> and returns itself from each accessor - so the assertions are
    /// against those interfaces.
    /// </para>
    /// </summary>
    public class PushAdminPublishTests : UtsTestBase
    {
        public PushAdminPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSH1/push-admin-accessible-0
        [Fact]
        public void RSH1_PushAdminAccessible()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new { })));

            client.Push.Should().BeOfType<PushHttp>();
            client.Push.Admin.Should().BeOfType<PushAdmin>();
            client.Push.Admin.DeviceRegistrations.Should().BeAssignableTo<IDeviceRegistrations>();
            client.Push.Admin.ChannelSubscriptions.Should().BeAssignableTo<IPushChannelSubscriptions>();
        }

        // UTS: rest/unit/RSH1a/publish-post-push-publish-0
        [Fact]
        public async Task RSH1a_PublishPostsToPushPublish()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            await client.Push.Admin.PublishAsync(
                new JObject
                {
                    { "transportType", "apns" },
                    { "deviceToken", "foo" },
                },
                new JObject
                {
                    {
                        "notification", new JObject
                        {
                            { "title", "Test" },
                            { "body", "Hello" },
                        }
                    },
                });

            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Url.Path.Should().Be("/push/publish");

            var body = JObject.Parse(request.BodyText);
            body["recipient"]["transportType"].Value<string>().Should().Be("apns");
            body["recipient"]["deviceToken"].Value<string>().Should().Be("foo");
            body["notification"]["title"].Value<string>().Should().Be("Test");
            body["notification"]["body"].Value<string>().Should().Be("Hello");
        }

        // UTS: rest/unit/RSH1a/publish-clientid-recipient-1
        [Fact]
        public async Task RSH1a_PublishWithClientIdRecipient()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            await client.Push.Admin.PublishAsync(
                new JObject { { "clientId", "user-123" } },
                new JObject { { "data", new JObject { { "key", "value" } } } });

            capturedRequests.Should().HaveCount(1);

            var body = JObject.Parse(capturedRequests[0].BodyText);
            body["recipient"]["clientId"].Value<string>().Should().Be("user-123");
            body["data"]["key"].Value<string>().Should().Be("value");
        }

        // UTS: rest/unit/RSH1a/publish-deviceid-recipient-2
        [Fact]
        public async Task RSH1a_PublishWithDeviceIdRecipient()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            await client.Push.Admin.PublishAsync(
                new JObject { { "deviceId", "device-abc" } },
                new JObject { { "notification", new JObject { { "title", "Device Push" } } } });

            capturedRequests.Should().HaveCount(1);

            var body = JObject.Parse(capturedRequests[0].BodyText);
            body["recipient"]["deviceId"].Value<string>().Should().Be("device-abc");
            body["notification"]["title"].Value<string>().Should().Be("Device Push");
        }

        // UTS: rest/unit/RSH1a/rejects-empty-recipient-3
        //
        // DEVIATION. RSH1a says empty values for `recipient` are "immediately rejected".
        // PushAdmin.PublishAsync's ValidateRequest (PushAdmin.cs:171) only rejects null, so an empty
        // JObject passes and a POST goes out with `{"recipient":{}}`. Recorded with the empty-data
        // case, which has the same cause. See Uts/deviations.md.
        [DeviationFact]
        public async Task RSH1a_RejectsEmptyRecipient()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject(),
                new JObject { { "notification", new JObject { { "title", "Test" } } } });

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            capturedRequests.Should().BeEmpty("the request is rejected before it reaches the wire");
        }

        // UTS: rest/unit/RSH1a/rejects-empty-data-4
        //
        // DEVIATION, same cause as the empty-recipient case above: only null is rejected, so an
        // empty payload produces a POST whose body is just `{"recipient":{...}}`.
        [DeviationFact]
        public async Task RSH1a_RejectsEmptyData()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject { { "clientId", "user-123" } },
                new JObject());

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            capturedRequests.Should().BeEmpty("the request is rejected before it reaches the wire");
        }

        // UTS: rest/unit/RSH1a/rejects-null-recipient-5
        [Fact]
        public async Task RSH1a_RejectsNullRecipient()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(CapturingMock(capturedRequests));

            Func<Task> act = () => client.Push.Admin.PublishAsync(
                null,
                new JObject { { "notification", new JObject { { "title", "Test" } } } });

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);

            capturedRequests.Should().BeEmpty();
        }

        // UTS: rest/unit/RSH1a/server-error-propagated-6
        [Fact]
        public async Task RSH1a_ServerErrorPropagated()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(400, new
                {
                    error = new { code = 40000, statusCode = 400, message = "Invalid recipient" },
                })));

            Func<Task> act = () => client.Push.Admin.PublishAsync(
                new JObject { { "transportType", "invalid" } },
                new JObject { { "notification", new JObject { { "title", "Test" } } } });

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40000);
            ((int)error.StatusCode.Value).Should().Be(400);
        }

        private static MockHttpClient CapturingMock(List<PendingHttpRequest> capturedRequests)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { });
                });
    }
}
