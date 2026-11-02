// REST Channel GetMessage Tests (RSL11, RSL11a, RSL11a1, RSL11b, RSL11c)
// Derived from uts/rest/unit/channel/get_message.md
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
    /// <summary>
    /// The uts/rest/unit/channel/get_message.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="GetMessageTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: ChannelGetMessageTests (realtime/unit/RTL28/identical-to-rest-0).
    /// </summary>
    public abstract class GetMessageTestsBase<TClient, TChannel> : UtsUnitTestBase
    {
        protected GetMessageTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the client under test.</summary>
        protected abstract TClient NewClient(ClientOptions options);

        /// <summary>The spec's <c>client.channels.get(name)</c>.</summary>
        protected abstract TChannel GetChannel(TClient client, string channelName);

        /// <summary>The spec's <c>channel.getMessage(serial)</c>.</summary>
        protected abstract Task<Message> GetMessageAsync(TChannel channel, string serial);

        // UTS: rest/unit/RSL11b/get-correct-endpoint-0
        [UtsFact]
        [Trait("spec", "RSL11b")]
        public async Task RSL11b_GetCorrectEndpoint()
        {
            // Setup
            var channelName = "test-RSL11b-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject
                    {
                        ["name"] = "evt",
                        ["data"] = "hello",
                        ["serial"] = "msg-serial-123",
                        ["timestamp"] = 1700000000000,
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var result = await GetMessageAsync(channel, "msg-serial-123");

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-123");
            (request.Body == null || request.Body.Length == 0).Should().BeTrue("request.body IS null OR request.body IS empty (found {0})", request.BodyText);
        }

        // UTS: rest/unit/RSL11c/returns-decoded-message-0
        [UtsFact]
        [Trait("spec", "RSL11c")]
        public async Task RSL11c_ReturnsDecodedMessage()
        {
            // Setup
            var channelName = "test-RSL11c-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["id"] = "msg-id-1",
                        ["name"] = "test-event",
                        ["data"] = "hello world",
                        ["serial"] = "serial-xyz",
                        ["clientId"] = "client-1",
                        ["timestamp"] = 1700000000000,
                        ["extras"] = new JObject
                        {
                            ["push"] = new JObject
                            {
                                ["notification"] = new JObject { ["title"] = "Test" },
                            },
                        },
                        ["version"] = new JObject
                        {
                            ["serial"] = "version-serial-1",
                            ["timestamp"] = 1700000000000,
                            ["clientId"] = "client-1",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var msg = await GetMessageAsync(channel, "serial-xyz");

            // Assertions
            msg.Should().BeAssignableTo<Message>();
            msg.Id.Should().Be("msg-id-1");
            msg.Name.Should().Be("test-event");
            msg.Data.Should().Be("hello world");
            msg.Serial.Should().Be("serial-xyz");
            msg.ClientId.Should().Be("client-1");
            msg.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000L));
            msg.Version.Serial.Should().Be("version-serial-1");
        }

        // UTS: rest/unit/RSL11b/url-encodes-serial-1
        [UtsFact]
        [Trait("spec", "RSL11b")]
        public async Task RSL11b_UrlEncodesSerial()
        {
            // Setup
            var channelName = "test-RSL11b-encode-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var serialWithSpecialChars = "serial/with:special+chars";

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JObject
                    {
                        ["name"] = "evt",
                        ["data"] = "hello",
                        ["serial"] = serialWithSpecialChars,
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            await GetMessageAsync(channel, serialWithSpecialChars);

            // Assertions
            var request = capturedRequests[0];
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/" + Uri.EscapeDataString(serialWithSpecialChars));
        }

        // UTS: rest/unit/RSL11a/missing-serial-error-0
        [UtsFact]
        [Trait("spec", "RSL11a")]
        public async Task RSL11a_MissingSerialError()
        {
            // Setup
            var channelName = "test-RSL11a-error-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps and Assertions
            // Empty string serial
            Func<Task> act = () => GetMessageAsync(channel, string.Empty);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40003);
        }
    }

    /// <summary>The uts/rest/unit/channel/get_message.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class GetMessageTests : GetMessageTestsBase<PubSubHttpClient, IHttpChannel>
    {
        public GetMessageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override PubSubHttpClient NewClient(ClientOptions options) => Rest(options);

        protected override IHttpChannel GetChannel(PubSubHttpClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<Message> GetMessageAsync(IHttpChannel channel, string serial) => channel.GetMessageAsync(serial);
    }
}
