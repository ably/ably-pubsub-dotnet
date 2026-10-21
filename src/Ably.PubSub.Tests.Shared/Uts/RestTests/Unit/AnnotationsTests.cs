// REST Channel Annotations Tests (RSL10, RSAN1, RSAN1a, RSAN1a2, RSAN1a3, RSAN1c, RSAN1c1, RSAN1c2, RSAN1c3, RSAN1c4, RSAN1c5, RSAN1c6, RSAN2, RSAN2a, RSAN3, RSAN3a, RSAN3b, RSAN3c)
// Derived from uts/rest/unit/channel/annotations.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Http;
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
    public class AnnotationsTests : UtsUnitTestBase
    {
        public AnnotationsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL10/annotations-attribute-type-0
        [Fact]
        [Trait("spec", "RSL10")]
        public void RSL10_AnnotationsAttributeType()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new JObject()));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test-RSL10");

            // Assertions
            channel.Annotations.Should().BeAssignableTo<RestAnnotations>();
        }

        // UTS: rest/unit/RSAN1c6/publish-post-annotation-create-0
        [UtsFact]
        [Trait("spec", "RSAN1c6")]
        public async Task RSAN1c6_PublishPostAnnotationCreate()
        {
            // Setup
            var channelName = "test-RSAN1-publish-" + Guid.NewGuid().ToString("N");
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
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.Annotations.PublishAsync("msg-serial-1", new Annotation(
                type: "com.example.reaction",
                name: "like"));

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1/annotations");

            var body = JToken.Parse(request.BodyText);
            body.Type.Should().Be(JTokenType.Array);
            body.Should().HaveCount(1);

            var annotation = body[0];
            ((int)annotation["action"]).Should().Be(0); // ANNOTATION_CREATE numeric value
            ((string)annotation["messageSerial"]).Should().Be("msg-serial-1");
            ((string)annotation["type"]).Should().Be("com.example.reaction");
            ((string)annotation["name"]).Should().Be("like");
        }

        // UTS: rest/unit/RSAN1a3/publish-type-required-0
        [UtsFact]
        [Trait("spec", "RSAN1a3")]
        public async Task RSAN1a3_PublishTypeRequired()
        {
            // Setup
            var channelName = "test-RSAN1a3-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(201, new JObject()));
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps and Assertions
            // Annotation without type
            Func<Task> act = () => channel.Annotations.PublishAsync("msg-serial-1", new Annotation
            {
                Name = "like",
            });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40003);
        }

        // UTS: rest/unit/RSAN1c3/annotation-data-encoded-0
        [UtsFact]
        [Trait("spec", "RSAN1c3")]
        public async Task RSAN1c3_AnnotationDataEncoded()
        {
            // Setup
            var channelName = "test-RSAN1c3-" + Guid.NewGuid().ToString("N");
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
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.Annotations.PublishAsync("msg-serial-1", new Annotation(
                type: "com.example.data",
                data: new JObject
                {
                    ["key"] = "value",
                    ["nested"] = new JObject { ["a"] = 1 },
                }));

            // Assertions
            var body = JArray.Parse(capturedRequests[0].BodyText);
            var annotation = body[0];

            // JSON data should be encoded as a string with encoding field
            annotation["data"].Type.Should().Be(JTokenType.String);
            ((string)annotation["encoding"]).Should().Be("json");
            JToken.DeepEquals(
                JToken.Parse((string)annotation["data"]),
                new JObject { ["key"] = "value", ["nested"] = new JObject { ["a"] = 1 } }).Should().BeTrue("parse_json(annotation[\"data\"]) == {{ \"key\": \"value\", \"nested\": {{ \"a\": 1 }} }} (found {0})", annotation["data"]);
        }

        // UTS: rest/unit/RSAN1c4/idempotent-id-generated-0
        [UtsFact]
        [Trait("spec", "RSAN1c4")]
        public async Task RSAN1c4_IdempotentIdGenerated()
        {
            // Setup
            var channelName = "test-RSAN1c4-enabled-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.Annotations.PublishAsync("msg-serial-1", new Annotation(
                type: "com.example.reaction"));

            // Assertions
            var body = JArray.Parse(capturedRequests[0].BodyText);
            var annotation = (JObject)body[0];

            annotation.Properties().Should().Contain(p => p.Name == "id");
            var annotationId = (string)annotation["id"];

            // Format: <base64>:0
            var parts = annotationId.Split(':');
            parts.Should().HaveCount(2);

            // NOTE: "matches pattern" is read as a full-string match, so the pattern is anchored. RSL1k1/RSAN1c4 require "base64-encoding" (not url-safe); the SDK uses standard base64, so + / and = padding are allowed here - see deviations.md (UTS Spec Errors, RSL1k1)
            parts[0].Should().MatchRegex("^[A-Za-z0-9+/_-]+=*$");
            parts[0].Length.Should().BeGreaterThanOrEqualTo(12); // At least 9 bytes base64 encoded
            parts[1].Should().Be("0");
        }

        // UTS: rest/unit/RSAN1c4/idempotent-id-not-generated-1
        [UtsFact]
        [Trait("spec", "RSAN1c4")]
        public async Task RSAN1c4_IdempotentIdNotGenerated()
        {
            // Setup
            var channelName = "test-RSAN1c4-disabled-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.Annotations.PublishAsync("msg-serial-1", new Annotation(
                type: "com.example.reaction"));

            // Assertions
            var body = JArray.Parse(capturedRequests[0].BodyText);
            var annotation = (JObject)body[0];

            annotation.Properties().Should().NotContain(p => p.Name == "id");
        }

        // NOTE: no Test ID in the spec: RSAN2a — delete sends POST with ANNOTATION_DELETE
        [UtsFact]
        [Trait("spec", "RSAN2a")]
        public async Task NoId_Rsan2aDeleteSendsPostWithAnnotationDelete()
        {
            // Setup
            var channelName = "test-RSAN2-delete-" + Guid.NewGuid().ToString("N");
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
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.Annotations.DeleteAsync("msg-serial-1", new Annotation(
                type: "com.example.reaction",
                name: "like"));

            // Assertions
            var request = capturedRequests[0];
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1/annotations");

            var body = JToken.Parse(request.BodyText);
            body.Type.Should().Be(JTokenType.Array);
            body.Should().HaveCount(1);

            var annotation = body[0];
            ((int)annotation["action"]).Should().Be(1); // ANNOTATION_DELETE numeric value
            ((string)annotation["messageSerial"]).Should().Be("msg-serial-1");
            ((string)annotation["type"]).Should().Be("com.example.reaction");
            ((string)annotation["name"]).Should().Be("like");
        }

        // NOTE: no Test ID in the spec: RSAN3b — get sends GET to correct endpoint
        [UtsFact]
        [Trait("spec", "RSAN3b")]
        public async Task NoId_Rsan3bGetSendsGetToCorrectEndpoint()
        {
            // Setup
            var channelName = "test-RSAN3-get-" + Guid.NewGuid().ToString("N");
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
                            ["id"] = "ann-1",
                            ["action"] = 0,
                            ["type"] = "com.example.reaction",
                            ["name"] = "like",
                            ["clientId"] = "user-1",
                            ["serial"] = "ann-serial-1",
                            ["messageSerial"] = "msg-serial-1",
                            ["timestamp"] = 1700000000000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var result = await channel.Annotations.GetAsync("msg-serial-1");

            // Assertions
            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1/annotations");
        }

        // NOTE: no Test ID in the spec: RSAN3c — get returns PaginatedResult of Annotations
        [UtsFact]
        [Trait("spec", "RSAN3c")]
        public async Task NoId_Rsan3cGetReturnsPaginatedResultOfAnnotations()
        {
            // Setup
            var channelName = "test-RSAN3c-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["id"] = "ann-1",
                            ["action"] = 0,
                            ["type"] = "com.example.reaction",
                            ["name"] = "like",
                            ["clientId"] = "user-1",
                            ["count"] = 1,
                            ["data"] = "thumbs-up",
                            ["serial"] = "ann-serial-1",
                            ["messageSerial"] = "msg-serial-1",
                            ["timestamp"] = 1700000000000,
                            ["extras"] = new JObject { ["custom"] = "metadata" },
                        },
                        new JObject
                        {
                            ["id"] = "ann-2",
                            ["action"] = 0,
                            ["type"] = "com.example.reaction",
                            ["name"] = "heart",
                            ["clientId"] = "user-2",
                            ["serial"] = "ann-serial-2",
                            ["messageSerial"] = "msg-serial-1",
                            ["timestamp"] = 1700000001000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var result = await channel.Annotations.GetAsync("msg-serial-1");

            // Assertions
            result.Should().BeAssignableTo<PaginatedResult<Annotation>>();
            result.Items.Should().HaveCount(2);

            var ann1 = result.Items[0];
            ann1.Should().BeAssignableTo<Annotation>();
            ann1.Id.Should().Be("ann-1");
            ann1.Action.Should().Be(AnnotationAction.Create);
            ann1.Type.Should().Be("com.example.reaction");
            ann1.Name.Should().Be("like");
            ann1.ClientId.Should().Be("user-1");
            ann1.Count.Should().Be(1);
            ann1.Data.Should().Be("thumbs-up");
            ann1.Serial.Should().Be("ann-serial-1");
            ann1.MessageSerial.Should().Be("msg-serial-1");
            ann1.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000L));
            ((string)ann1.Extras.ToJson()["custom"]).Should().Be("metadata");

            var ann2 = result.Items[1];
            ann2.Name.Should().Be("heart");
            ann2.ClientId.Should().Be("user-2");
        }

        // NOTE: no Test ID in the spec: RSAN3b — get passes params as querystring
        [UtsFact]
        [Trait("spec", "RSAN3b")]
        public async Task NoId_Rsan3bGetPassesParamsAsQuerystring()
        {
            // Setup
            var channelName = "test-RSAN3b-params-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // AWAIT channel.annotations.get("msg-serial-1", params: { "limit": "50" })
            await channel.Annotations.GetAsync("msg-serial-1", new AnnotationsRequestParams { ExtraParameters = new Dictionary<string, string> { ["limit"] = "50" } });

            // Assertions
            var request = capturedRequests[0];
            request.QueryParams["limit"].Should().Be("50");
        }
    }
}
