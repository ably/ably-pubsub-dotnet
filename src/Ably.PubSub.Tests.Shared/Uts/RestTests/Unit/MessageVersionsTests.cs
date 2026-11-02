// REST Channel GetMessageVersions Tests (RSL14, RSL14a, RSL14a1, RSL14b, RSL14c)
// Derived from uts/rest/unit/channel/message_versions.md
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
    /// The uts/rest/unit/channel/message_versions.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="MessageVersionsTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: ChannelMessageVersionsTests (realtime/unit/RTL31/identical-to-rest-0).
    /// </summary>
    public abstract class MessageVersionsTestsBase<TClient, TChannel> : UtsUnitTestBase
    {
        protected MessageVersionsTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the client under test.</summary>
        protected abstract TClient NewClient(ClientOptions options);

        /// <summary>The spec's <c>client.channels.get(name)</c>.</summary>
        protected abstract TChannel GetChannel(TClient client, string channelName);

        /// <summary>The spec's <c>channel.getMessageVersions(serial, params)</c>.</summary>
        protected abstract Task<PaginatedResult<Message>> GetMessageVersionsAsync(TChannel channel, string serial, PaginatedRequestParams query = null);

        // UTS: rest/unit/RSL14b/get-correct-endpoint-0
        [UtsFact]
        [Trait("spec", "RSL14b")]
        public async Task RSL14b_GetCorrectEndpoint()
        {
            // Setup
            var channelName = "test-RSL14b-" + Guid.NewGuid().ToString("N");
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
                            ["name"] = "evt",
                            ["data"] = "v2-data",
                            ["serial"] = "msg-serial-1",
                            ["action"] = 1,
                            ["version"] = new JObject { ["serial"] = "vs2", ["timestamp"] = 1700000002000 },
                        },
                        new JObject
                        {
                            ["name"] = "evt",
                            ["data"] = "v1-data",
                            ["serial"] = "msg-serial-1",
                            ["action"] = 0,
                            ["version"] = new JObject { ["serial"] = "vs1", ["timestamp"] = 1700000001000 },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var result = await GetMessageVersionsAsync(channel, "msg-serial-1");

            // Assertions
            capturedRequests.Should().HaveCount(1);

            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages/msg-serial-1/versions");
        }

        // UTS: rest/unit/RSL14c/returns-paginated-result-0
        [UtsFact]
        [Trait("spec", "RSL14c")]
        public async Task RSL14c_ReturnsPaginatedResult()
        {
            // Setup
            var channelName = "test-RSL14c-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["name"] = "evt",
                            ["data"] = "updated-data",
                            ["serial"] = "msg-serial-1",
                            ["action"] = 1,
                            ["version"] = new JObject
                            {
                                ["serial"] = "vs2",
                                ["timestamp"] = 1700000002000,
                                ["clientId"] = "user-1",
                                ["description"] = "edit",
                            },
                        },
                        new JObject
                        {
                            ["name"] = "evt",
                            ["data"] = "original-data",
                            ["serial"] = "msg-serial-1",
                            ["action"] = 0,
                            ["version"] = new JObject
                            {
                                ["serial"] = "vs1",
                                ["timestamp"] = 1700000001000,
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var result = await GetMessageVersionsAsync(channel, "msg-serial-1");

            // Assertions
            result.Should().BeAssignableTo<PaginatedResult<Message>>();
            result.Items.Should().HaveCount(2);

            result.Items[0].Should().BeAssignableTo<Message>();
            result.Items[0].Data.Should().Be("updated-data");
            result.Items[0].Action.Should().Be(MessageAction.MessageUpdate);
            result.Items[0].Version.Serial.Should().Be("vs2");
            result.Items[0].Version.Description.Should().Be("edit");

            result.Items[1].Data.Should().Be("original-data");
            result.Items[1].Action.Should().Be(MessageAction.MessageCreate);
        }

        // UTS: rest/unit/RSL14a/params-as-querystring-0
        [UtsFact]
        [Trait("spec", "RSL14a")]
        public async Task RSL14a_ParamsAsQuerystring()
        {
            // Setup
            var channelName = "test-RSL14a-params-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = GetChannel(client, channelName);

            // Test Steps
            var result = await GetMessageVersionsAsync(channel, "msg-serial-1", new PaginatedRequestParams
            {
                Direction = QueryDirection.Backwards,
                Limit = 10,
            });

            // Assertions
            var request = capturedRequests[0];
            request.QueryParams["direction"].Should().Be("backwards");
            request.QueryParams["limit"].Should().Be("10");
        }
    }

    /// <summary>The uts/rest/unit/channel/message_versions.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class MessageVersionsTests : MessageVersionsTestsBase<PubSubHttpClient, IHttpChannel>
    {
        public MessageVersionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override PubSubHttpClient NewClient(ClientOptions options) => Rest(options);

        protected override IHttpChannel GetChannel(PubSubHttpClient client, string channelName) => client.Channels.Get(channelName);

        protected override Task<PaginatedResult<Message>> GetMessageVersionsAsync(IHttpChannel channel, string serial, PaginatedRequestParams query = null) => channel.GetMessageVersionsAsync(serial, query);
    }
}
