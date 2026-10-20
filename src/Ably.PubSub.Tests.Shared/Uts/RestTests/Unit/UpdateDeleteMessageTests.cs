// REST Channel UpdateMessage/DeleteMessage/AppendMessage Tests (RSL15, RSL15a, RSL15b, RSL15b1, RSL15b7, RSL15c, RSL15d, RSL15e, RSL15f)
// Derived from uts/rest/unit/channel/update_delete_message.md
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
    public class UpdateDeleteMessageTests : UtsUnitTestBase
    {
        public UpdateDeleteMessageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL15b/update-sends-patch-update-0
        [UtsFact]
        [Trait("spec", "RSL15b")]
        public async Task RSL15b_UpdateSendsPatchUpdate()
        {
            // Setup
            var channelName = "test-RSL15-update-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.UpdateMessageAsync(
                new Message { Serial = "msg-serial-1", Name = "updated", Data = "new-data" });

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("PATCH");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1");

            var body = JObject.Parse(request.BodyText);
            ((int)body["action"]).Should().Be(1); // MESSAGE_UPDATE numeric value
            ((string)body["name"]).Should().Be("updated");
            ((string)body["data"]).Should().Be("new-data");
        }

        // UTS: rest/unit/RSL15b/delete-sends-patch-delete-1
        [UtsFact]
        [Trait("spec", "RSL15b")]
        public async Task RSL15b_DeleteSendsPatchDelete()
        {
            // Setup
            var channelName = "test-RSL15-delete-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.DeleteMessageAsync(
                new Message { Serial = "msg-serial-1" });

            // Assertions
            var request = capturedRequests[0];
            request.Method.Should().Be("PATCH");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1");

            var body = JObject.Parse(request.BodyText);
            ((int)body["action"]).Should().Be(2); // MESSAGE_DELETE numeric value
        }

        // UTS: rest/unit/RSL15b/append-sends-patch-append-2
        [UtsFact]
        [Trait("spec", "RSL15b")]
        public async Task RSL15b_AppendSendsPatchAppend()
        {
            // Setup
            var channelName = "test-RSL15-append-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.AppendMessageAsync(
                new Message { Serial = "msg-serial-1", Data = "appended-data" });

            // Assertions
            var request = capturedRequests[0];
            request.Method.Should().Be("PATCH");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1");

            var body = JObject.Parse(request.BodyText);
            ((int)body["action"]).Should().Be(5); // MESSAGE_APPEND numeric value
            ((string)body["data"]).Should().Be("appended-data");
        }

        // UTS: rest/unit/RSL15b7/version-set-with-operation-0
        [UtsFact]
        [Trait("spec", "RSL15b7")]
        public async Task RSL15b7_VersionSetWithOperation()
        {
            // Setup
            var channelName = "test-RSL15b7-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" },
                operation: new MessageOperation
                {
                    ClientId = "user1",
                    Description = "fixed typo",
                    Metadata = new Dictionary<string, string> { ["reason"] = "typo" },
                });

            // Assertions
            var body = JObject.Parse(capturedRequests[0].BodyText);
            body.Properties().Should().Contain(p => p.Name == "version");
            ((string)body["version"]["clientId"]).Should().Be("user1");
            ((string)body["version"]["description"]).Should().Be("fixed typo");
            ((string)body["version"]["metadata"]["reason"]).Should().Be("typo");
        }

        // UTS: rest/unit/RSL15b7/version-absent-no-operation-1
        [UtsFact]
        [Trait("spec", "RSL15b7")]
        public async Task RSL15b7_VersionAbsentNoOperation()
        {
            // Setup
            var channelName = "test-RSL15b7-absent-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" });

            // Assertions
            var body = JObject.Parse(capturedRequests[0].BodyText);
            body.Properties().Should().NotContain(p => p.Name == "version");
        }

        // UTS: rest/unit/RSL15c/no-mutate-user-message-0
        [UtsFact]
        [Trait("spec", "RSL15c")]
        public async Task RSL15c_NoMutateUserMessage()
        {
            // Setup
            var channelName = "test-RSL15c-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var originalMsg = new Message { Serial = "s1", Name = "orig", Data = "original-data" };

            await channel.UpdateMessageAsync(originalMsg);

            // Assertions
            // Original message must not have been mutated
            originalMsg.Action.Should().BeNull(); // No action was set on original
            originalMsg.Name.Should().Be("orig");
            originalMsg.Data.Should().Be("original-data");

            // But the request body should contain the action
            var body = JObject.Parse(capturedRequests[0].BodyText);
            ((int)body["action"]).Should().Be(1); // MESSAGE_UPDATE
        }

        // UTS: rest/unit/RSL15e/returns-update-delete-result-0
        [UtsFact]
        [Trait("spec", "RSL15e")]
        public async Task RSL15e_ReturnsUpdateDeleteResult()
        {
            // Setup
            var channelName = "test-RSL15e-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject { ["versionSerial"] = "version-serial-abc" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var result = await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" });

            // Assertions
            result.Should().BeAssignableTo<UpdateDeleteResult>();
            result.VersionSerial.Should().Be("version-serial-abc");
        }

        // UTS: rest/unit/RSL15e/null-version-serial-1
        [UtsFact]
        [Trait("spec", "RSL15e")]
        public async Task RSL15e_NullVersionSerial()
        {
            // Setup
            var channelName = "test-RSL15e-null-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject { ["versionSerial"] = JValue.CreateNull() });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var result = await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" });

            // Assertions
            result.Should().BeAssignableTo<UpdateDeleteResult>();
            result.VersionSerial.Should().BeNull();
        }

        // UTS: rest/unit/RSL15f/params-sent-as-querystring-0
        [UtsFact]
        [Trait("spec", "RSL15f")]
        public async Task RSL15f_ParamsSentAsQuerystring()
        {
            // Setup
            var channelName = "test-RSL15f-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = "updated" },
                parameters: new Dictionary<string, string> { ["key"] = "value", ["num"] = "42" });

            // Assertions
            var request = capturedRequests[0];
            request.QueryParams["key"].Should().Be("value");
            request.QueryParams["num"].Should().Be("42");
        }

        // UTS: rest/unit/RSL15a/serial-required-throws-error-0
        [UtsFact]
        [Trait("spec", "RSL15a")]
        public async Task RSL15a_SerialRequiredThrowsError()
        {
            // Setup
            var channelName = "test-RSL15a-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps and Assertions
            // updateMessage without serial
            Func<Task> act = () => channel.UpdateMessageAsync(new Message("x", "y"));
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40003);

            // deleteMessage without serial
            act = () => channel.DeleteMessageAsync(new Message { Name = "x" });
            error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40003);

            // appendMessage without serial
            act = () => channel.AppendMessageAsync(new Message { Data = "y" });
            error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40003);
        }

        // UTS: rest/unit/RSL15d/body-encoded-per-rsl4-0
        [UtsFact]
        [Trait("spec", "RSL15d")]
        public async Task RSL15d_BodyEncodedPerRsl4()
        {
            // Setup
            var channelName = "test-RSL15d-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // JSON object data should be encoded per RSL4
            await channel.UpdateMessageAsync(
                new Message { Serial = "s1", Data = new JObject { ["key"] = "value" } });

            // Assertions
            var body = JObject.Parse(capturedRequests[0].BodyText);

            // JSON data should be JSON-encoded as a string with encoding field
            body["data"].Type.Should().Be(JTokenType.String); // JSON-encoded string
            ((string)body["encoding"]).Should().Be("json");
            JToken.DeepEquals(JToken.Parse((string)body["data"]), new JObject { ["key"] = "value" }).Should().BeTrue("parse_json(body[\"data\"]) == {{ \"key\": \"value\" }} (found {0})", body["data"]);
        }

        // UTS: rest/unit/RSL15b/serial-url-encoded-path-3
        [UtsFact]
        [Trait("spec", "RSL15b")]
        public async Task RSL15b_SerialUrlEncodedPath()
        {
            // Setup
            var channelName = "test-RSL15b-encode-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var serialWithSpecialChars = "serial/special:chars";

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject { ["versionSerial"] = "vs1" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.UpdateMessageAsync(
                new Message { Serial = serialWithSpecialChars, Data = "updated" });

            // Assertions
            var request = capturedRequests[0];
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/" + Uri.EscapeDataString(serialWithSpecialChars));
        }
    }
}
