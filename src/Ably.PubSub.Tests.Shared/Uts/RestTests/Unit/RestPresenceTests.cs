// REST Presence Unit Tests (RSL3, RSP1, RSP1a, RSP1b, RSP3, RSP3a1, RSP3a2, RSP3a3, RSP4, RSP4a, RSP4b1, RSP4b2, RSP4b3, RSP5)
// Derived from uts/rest/unit/presence/rest_presence.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
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
    public class RestPresenceTests : UtsUnitTestBase
    {
        public RestPresenceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSP1a/presence-channel-attribute-0
        [Fact]
        [Trait("spec", "RSP1a")]
        public void RSP1a_PresenceChannelAttribute()
        {
            // Setup
            var channelName = "test-RSP1a-" + Guid.NewGuid().ToString("N");

            // Given a REST client with mocked HTTP
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // And a channel channel_name
            var channel = client.Channels.Get(channelName);

            // When accessing channel.presence
            var presence = channel.Presence;

            // Then a RestPresence object is returned
            // ASSERT presence IS RestPresence: guaranteed at compile time (Presence is declared as IPresence)
            presence.Should().NotBeNull();

            // And the presence object is associated with channel_name
            // NOTE: IPresence exposes no channel name; the SDK's HttpChannel implements IPresence itself, so the name is read through its IHttpChannel face
            ((IHttpChannel)presence).Name.Should().Be(channelName);
        }

        // UTS: rest/unit/RSP1b/same-instance-returned-0
        [Fact]
        [Trait("spec", "RSP1b")]
        public void RSP1b_SameInstanceReturned()
        {
            // Setup
            var channelName = "test-RSP1b-" + Guid.NewGuid().ToString("N");

            // Given a REST client with mocked HTTP
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // And a channel = client.channels.get(channel_name)
            var channel = client.Channels.Get(channelName);

            // When accessing channel.presence multiple times
            var presence1 = channel.Presence;
            var presence2 = channel.Presence;
            var presence3 = client.Channels.Get(channelName).Presence;

            // Then the same RestPresence instance is returned each time
            presence1.Should().BeSameAs(presence2);
            presence2.Should().BeSameAs(presence3);
        }

        // UTS: rest/unit/RSP3a/get-request-endpoint-0
        [UtsFact]
        [Trait("spec", "RSP3a")]
        public async Task RSP3a_GetRequestEndpoint()
        {
            // Setup
            var channelName = "test-RSP3a-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    Interlocked.Increment(ref requestCount);
                    req.RespondWith(200, new JArray
                    {
                        new JObject
                        {
                            ["action"] = 1,
                            ["clientId"] = "client1",
                            ["data"] = "hello",
                        },
                        new JObject
                        {
                            ["action"] = 1,
                            ["clientId"] = "client2",
                            ["data"] = "world",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            Volatile.Read(ref requestCount).Should().Be(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/presence");
            // ASSERT result IS PaginatedResult<PresenceMessage>: guaranteed at compile time (GetAsync returns PaginatedResult<PresenceMessage>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(2);
        }

        // UTS: rest/unit/RSP3b/get-returns-presence-messages-0
        [UtsFact]
        [Trait("spec", "RSP3b")]
        public async Task RSP3b_GetReturnsPresenceMessages()
        {
            // Setup
            var channelName = "test-RSP3b-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "user123",
                            ["connectionId"] = "conn456",
                            ["data"] = "status data",
                            ["encoding"] = null,
                            ["timestamp"] = 1234567890000L,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items.Should().HaveCount(1);
            // ASSERT result.items[0] IS PresenceMessage: guaranteed at compile time (Items is List<PresenceMessage>)
            result.Items[0].Action.Should().Be(PresenceAction.Present); // action 1
            result.Items[0].ClientId.Should().Be("user123");
            result.Items[0].ConnectionId.Should().Be("conn456");
            result.Items[0].Data.Should().Be("status data");
            result.Items[0].Timestamp.Value.ToUnixTimeMilliseconds().Should().Be(1234567890000L);
        }

        // UTS: rest/unit/RSP3c/get-empty-members-0
        [UtsFact]
        [Trait("spec", "RSP3c")]
        public async Task RSP3c_GetEmptyMembers()
        {
            // Setup
            var channelName = "test-RSP3c-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            // ASSERT result.items IS List: guaranteed at compile time (Items is List<PresenceMessage>)
            result.Items.Should().HaveCount(0);
            result.HasNext.Should().BeFalse();
        }

        // UTS: rest/unit/RSP3a1/get-limit-parameter-0
        [UtsFact]
        [Trait("spec", "RSP3a1")]
        public async Task RSP3a1_GetLimitParameter()
        {
            // Setup
            var channelName = "test-RSP3a1a-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "client1",
                        },
                        new JObject
                        {
                            ["action"] = 1,
                            ["clientId"] = "client2",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync(limit: 50);

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP3a1/get-limit-default-100-1
        [UtsFact]
        [Trait("spec", "RSP3a1")]
        public async Task RSP3a1_GetLimitDefault100()
        {
            // Setup
            var channelName = "test-RSP3a1b-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            (!capturedRequests[0].Url.QueryParams.ContainsKey("limit") || capturedRequests[0].Url.QueryParams["limit"] == "100")
                .Should().BeTrue("the limit is either not sent or is the default 100");
        }

        // UTS: rest/unit/RSP3a1/get-limit-max-1000-2
        [UtsFact]
        [Trait("spec", "RSP3a1")]
        public async Task RSP3a1_GetLimitMax1000()
        {
            // Setup
            var channelName = "test-RSP3a1c-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync(limit: 1000);

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1000");
        }

        // UTS: rest/unit/RSP3a2/get-clientid-filter-0
        [UtsFact]
        [Trait("spec", "RSP3a2")]
        public async Task RSP3a2_GetClientidFilter()
        {
            // Setup
            var channelName = "test-RSP3a2-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "specific-client",
                            ["data"] = "filtered",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync(clientId: "specific-client");

            // Assertions
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("specific-client");
        }

        // UTS: rest/unit/RSP3a3/get-connectionid-filter-0
        [UtsFact]
        [Trait("spec", "RSP3a3")]
        public async Task RSP3a3_GetConnectionidFilter()
        {
            // Setup
            var channelName = "test-RSP3a3-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "client1",
                            ["connectionId"] = "conn123",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync(connectionId: "conn123");

            // Assertions
            capturedRequests[0].Url.QueryParams["connectionId"].Should().Be("conn123");
        }

        // UTS: rest/unit/RSP3/get-multiple-filters-0
        [UtsFact]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetMultipleFilters()
        {
            // Setup
            var channelName = "test-RSP3-multi-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync(
                limit: 25,
                clientId: "user1",
                connectionId: "conn1");

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("25");
            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("user1");
            capturedRequests[0].Url.QueryParams["connectionId"].Should().Be("conn1");
        }

        // UTS: rest/unit/RSP4a/history-request-endpoint-0
        [UtsFact]
        [Trait("spec", "RSP4a")]
        public async Task RSP4a_HistoryRequestEndpoint()
        {
            // Setup
            var channelName = "test-RSP4a-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 2,
                            ["clientId"] = "client1",
                            ["data"] = "entered",
                        },
                        new JObject
                        {
                            ["action"] = 4,
                            ["clientId"] = "client1",
                            ["data"] = "left",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/presence/history");
            // ASSERT result IS PaginatedResult<PresenceMessage>: guaranteed at compile time (HistoryAsync returns PaginatedResult<PresenceMessage>)
            result.Should().NotBeNull();
        }

        // UTS: rest/unit/RSP4a/history-returns-paginated-1
        [UtsFact]
        [Trait("spec", "RSP4a")]
        public async Task RSP4a_HistoryReturnsPaginated()
        {
            // Setup
            var channelName = "test-RSP4a-result-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 2,
                            ["clientId"] = "user1",
                            ["data"] = "d1",
                            ["timestamp"] = 1000,
                        },
                        new JObject
                        {
                            ["action"] = 3,
                            ["clientId"] = "user1",
                            ["data"] = "d2",
                            ["timestamp"] = 2000,
                        },
                        new JObject
                        {
                            ["action"] = 4,
                            ["clientId"] = "user1",
                            ["data"] = "d3",
                            ["timestamp"] = 3000,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            // ASSERT result IS PaginatedResult<PresenceMessage>: guaranteed at compile time (HistoryAsync returns PaginatedResult<PresenceMessage>)
            result.Should().NotBeNull();
            result.Items.Should().HaveCount(3);
            result.Items[0].Action.Should().Be(PresenceAction.Enter); // action 2
            result.Items[1].Action.Should().Be(PresenceAction.Leave); // action 3
            result.Items[2].Action.Should().Be(PresenceAction.Update); // action 4
        }

        // UTS: rest/unit/RSP4b1/history-start-parameter-0
        [UtsFact]
        [Trait("spec", "RSP4b1")]
        public async Task RSP4b1_HistoryStartParameter()
        {
            // Setup
            var channelName = "test-RSP4b1a-" + Guid.NewGuid().ToString("N");
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
            var startTime = 1609459200000L; // 2021-01-01 00:00:00 UTC

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Start = DateTimeOffset.FromUnixTimeMilliseconds(startTime) });

            // Assertions
            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
        }

        // UTS: rest/unit/RSP4b1/history-end-parameter-1
        [UtsFact]
        [Trait("spec", "RSP4b1")]
        public async Task RSP4b1_HistoryEndParameter()
        {
            // Setup
            var channelName = "test-RSP4b1b-" + Guid.NewGuid().ToString("N");
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
            var endTime = 1609545600000L; // 2021-01-02 00:00:00 UTC

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { End = DateTimeOffset.FromUnixTimeMilliseconds(endTime) });

            // Assertions
            capturedRequests[0].Url.QueryParams["end"].Should().Be("1609545600000");
        }

        // UTS: rest/unit/RSP4b1/history-start-end-params-2
        [UtsFact]
        [Trait("spec", "RSP4b1")]
        public async Task RSP4b1_HistoryStartEndParams()
        {
            // Setup
            var channelName = "test-RSP4b1c-" + Guid.NewGuid().ToString("N");
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
            var startTime = 1609459200000L;
            var endTime = 1609545600000L;

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(startTime),
                End = DateTimeOffset.FromUnixTimeMilliseconds(endTime),
            });

            // Assertions
            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
            capturedRequests[0].Url.QueryParams["end"].Should().Be("1609545600000");
        }

        // UTS: rest/unit/RSP4b1/history-datetime-objects-3
        [UtsFact]
        [Trait("spec", "RSP4b1")]
        public async Task RSP4b1_HistoryDatetimeObjects()
        {
            // Setup
            var channelName = "test-RSP4b1d-" + Guid.NewGuid().ToString("N");
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
            // Language-specific: if the language supports DateTime/Date objects
            var startDatetime = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero); // language-specific

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Start = startDatetime });

            // Assertions
            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
        }

        // UTS: rest/unit/RSP4b2/history-direction-backwards-default-0
        [UtsFact]
        [Trait("spec", "RSP4b2")]
        public async Task RSP4b2_HistoryDirectionBackwardsDefault()
        {
            // Setup
            var channelName = "test-RSP4b2a-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            (!capturedRequests[0].Url.QueryParams.ContainsKey("direction") || capturedRequests[0].Url.QueryParams["direction"] == "backwards")
                .Should().BeTrue("the direction is either not sent or is the default backwards");
        }

        // UTS: rest/unit/RSP4b2/history-direction-forwards-1
        [UtsFact]
        [Trait("spec", "RSP4b2")]
        public async Task RSP4b2_HistoryDirectionForwards()
        {
            // Setup
            var channelName = "test-RSP4b2b-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            // Assertions
            capturedRequests[0].Url.QueryParams["direction"].Should().Be("forwards");
        }

        // UTS: rest/unit/RSP4b2/history-direction-backwards-explicit-2
        [UtsFact]
        [Trait("spec", "RSP4b2")]
        public async Task RSP4b2_HistoryDirectionBackwardsExplicit()
        {
            // Setup
            var channelName = "test-RSP4b2c-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Backwards });

            // Assertions
            capturedRequests[0].Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSP4b3/history-limit-parameter-0
        [UtsFact]
        [Trait("spec", "RSP4b3")]
        public async Task RSP4b3_HistoryLimitParameter()
        {
            // Setup
            var channelName = "test-RSP4b3a-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Limit = 50 });

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP4b3/history-limit-default-100-1
        [UtsFact]
        [Trait("spec", "RSP4b3")]
        public async Task RSP4b3_HistoryLimitDefault100()
        {
            // Setup
            var channelName = "test-RSP4b3b-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            (!capturedRequests[0].Url.QueryParams.ContainsKey("limit") || capturedRequests[0].Url.QueryParams["limit"] == "100")
                .Should().BeTrue("the limit is either not sent or is the default 100");
        }

        // UTS: rest/unit/RSP4b3/history-limit-max-1000-2
        [UtsFact]
        [Trait("spec", "RSP4b3")]
        public async Task RSP4b3_HistoryLimitMax1000()
        {
            // Setup
            var channelName = "test-RSP4b3c-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams { Limit = 1000 });

            // Assertions
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1000");
        }

        // UTS: rest/unit/RSP4/history-all-parameters-0
        [UtsFact]
        [Trait("spec", "RSP4")]
        public async Task RSP4_HistoryAllParameters()
        {
            // Setup
            var channelName = "test-RSP4-all-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(1609459200000L),
                End = DateTimeOffset.FromUnixTimeMilliseconds(1609545600000L),
                Direction = QueryDirection.Forwards,
                Limit = 50,
            });

            // Assertions
            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
            capturedRequests[0].Url.QueryParams["end"].Should().Be("1609545600000");
            capturedRequests[0].Url.QueryParams["direction"].Should().Be("forwards");
            capturedRequests[0].Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP5/decode-string-data-0
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeStringData()
        {
            // Setup
            var channelName = "test-RSP5a-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "c1",
                            ["data"] = "plain string data",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items[0].Data.Should().Be("plain string data");
            result.Items[0].Data.Should().BeAssignableTo<string>();
        }

        // UTS: rest/unit/RSP5/decode-json-data-1
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeJsonData()
        {
            // Setup
            var channelName = "test-RSP5b-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "c1",
                            ["data"] = "{\"status\":\"online\",\"count\":42}",
                            ["encoding"] = "json",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items[0].Data.Should().BeAssignableTo<JObject>();
            ((string)((JObject)result.Items[0].Data)["status"]).Should().Be("online");
            ((int)((JObject)result.Items[0].Data)["count"]).Should().Be(42);
            // DEVIATION: the SDK leaves an empty string, not null, once the encoding is consumed — see deviations.md (RSL6 / TM3)
            result.Items[0].Encoding.Should().BeEmpty(); // encoding consumed
        }

        // UTS: rest/unit/RSP5/decode-base64-binary-2
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeBase64Binary()
        {
            // Setup
            var channelName = "test-RSP5c-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "c1",
                            ["data"] = "SGVsbG8gV29ybGQ=", // "Hello World" in base64
                            ["encoding"] = "base64",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items[0].Data.Should().BeAssignableTo<byte[]>();
            ((byte[])result.Items[0].Data).Should().Equal(Encoding.UTF8.GetBytes("Hello World"));
            // DEVIATION: the SDK leaves an empty string, not null, once the encoding is consumed — see deviations.md (RSL6 / TM3)
            result.Items[0].Encoding.Should().BeEmpty(); // encoding consumed
        }

        // UTS-NA: rest/unit/RSP5/decode-msgpack-binary-3 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // channel_name = "test-RSP5-msgpack-binary-${random_id()}"
        //
        // # Binary payload using msgpack bin type (valid UTF-8 bytes)
        // binary_payload = bytes([0x73, 0x6F, 0x6D, 0x65, 0x20, 0x64, 0x61, 0x74, 0x61])  # "some data"
        //
        // mock_http = MockHttpClient(
        //   onConnectionAttempt: (conn) => conn.respond_with_success(),
        //   onRequest: (req) => {
        //     req.respond_with(200,
        //       body: msgpack_encode([
        //         { "action": 1, "clientId": "client1", "data": binary_payload }
        //       ]),
        //       headers: { "Content-Type": "application/x-msgpack" }
        //     )
        //   }
        // )
        // install_mock(mock_http)
        //
        // client = Rest(options: ClientOptions(
        //   key: "appId.keyId:keySecret",
        //   useBinaryProtocol: true
        // ))
        //
        // result = AWAIT client.channels.get(channel_name).presence.get()
        //
        // # Binary data must remain binary, NOT be converted to a string
        // ASSERT result.items[0].data IS Binary/Uint8List/[]byte
        // ASSERT result.items[0].data == bytes([0x73, 0x6F, 0x6D, 0x65, 0x20, 0x64, 0x61, 0x74, 0x61])
        // ASSERT result.items[0].encoding IS null

        // UTS: rest/unit/RSP5/decode-utf8-data-4
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeUtf8Data()
        {
            // Setup
            var channelName = "test-RSP5d-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "c1",
                            ["data"] = "SGVsbG8gV29ybGQ=", // base64 of UTF-8 bytes
                            ["encoding"] = "utf-8/base64",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items[0].Data.Should().Be("Hello World");
            result.Items[0].Data.Should().BeAssignableTo<string>();
        }

        // UTS: rest/unit/RSP5/decode-chained-encoding-5
        // DEVIATION: see deviations.md
        [DeviationFact("RSL6a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeChainedEncoding()
        {
            // Setup
            var channelName = "test-RSP5e-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 1,
                            ["clientId"] = "c1",
                            ["data"] = "eyJrZXkiOiJ2YWx1ZSJ9", // base64 of {"key":"value"}
                            ["encoding"] = "json/base64",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            // Decoding order: base64 first, then json
            result.Items[0].Data.Should().BeAssignableTo<JObject>();
            ((string)((JObject)result.Items[0].Data)["key"]).Should().Be("value");
        }

        // UTS: rest/unit/RSP5/decode-history-messages-6
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeHistoryMessages()
        {
            // Setup
            var channelName = "test-RSP5f-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 2,
                            ["clientId"] = "c1",
                            ["data"] = "{\"event\":\"entered\"}",
                            ["encoding"] = "json",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            result.Items[0].Data.Should().BeAssignableTo<JObject>();
            ((string)((JObject)result.Items[0].Data)["event"]).Should().Be("entered");
        }

        // UTS: rest/unit/RSP5/decode-cipher-channel-7
        [UtsFact(Skip = "UTS spec error RSP5: the encrypted fixture is truncated and cannot be decrypted; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSP5")]
        public Task RSP5_DecodeCipherChannel()
        {
            // SPEC ERROR RSP5: the fixture `HO4cYSP8LybPYBPZPHQOtuD53yrD3YV3NBoTEYBh4U0=` is 32 bytes (IV plus one block) and decrypts with the given key to the truncated text `{"example":{"jso`, not `{"secret":"data"}`; no valid PKCS7 padding.
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSP5 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSP3/get-pagination-link-header-1
        [UtsFact]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetPaginationLinkHeader()
        {
            // Setup
            var channelName = "test-RSP-pagination1-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(
                        200,
                        new JArray
                        {
                            new JObject
                            {
                                ["action"] = 1,
                                ["clientId"] = "client1",
                            },
                            new JObject
                            {
                                ["action"] = 1,
                                ["clientId"] = "client2",
                            },
                        },
                        new Dictionary<string, string> { ["Link"] = "</channels/" + channelName + "/presence?page=2>; rel=\"next\"" });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            result.Items.Should().HaveCount(2);
            result.HasNext.Should().BeTrue();
        }

        // UTS: rest/unit/RSP3/get-pagination-next-page-2
        [UtsFact]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetPaginationNextPage()
        {
            // Setup
            var channelName = "test-RSP-pagination2-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            new JArray { new JObject { ["action"] = 1, ["clientId"] = "client1" } },
                            new Dictionary<string, string> { ["Link"] = "</channels/" + channelName + "/presence?page=2>; rel=\"next\"" });
                    }
                    else
                    {
                        req.RespondWith(200, new JArray { new JObject { ["action"] = 1, ["clientId"] = "client2" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var page1 = await client.Channels.Get(channelName).Presence.GetAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            page1.Items[0].ClientId.Should().Be("client1");
            page2.Items[0].ClientId.Should().Be("client2");
            page2.HasNext.Should().BeFalse();
        }

        // UTS: rest/unit/RSP4/history-pagination-1
        [UtsFact(Skip = "UTS spec error RSP4: the second page returns wire action 4 but asserts LEAVE, which is action 3; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSP4")]
        public Task RSP4_HistoryPagination()
        {
            // SPEC ERROR RSP4: the second page is `{ "action": 4, ... }` yet the test asserts `PresenceAction.leave`; TP2 defines UPDATE as 4 and LEAVE as 3 (and the spec's own RSP4a and action-mapping tests use 3 for leave).
            // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSP4 — fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSP3/get-server-error-3
        // DEVIATION: see deviations.md
        [DeviationFact("RSC15a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetServerError()
        {
            // Setup
            var channelName = "test-RSP-error1-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(500, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 50000,
                            ["statusCode"] = 500,
                            ["message"] = "Internal server error",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Channels.Get(channelName).Presence.GetAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(50000);
            ((int?)error.StatusCode).Should().Be(500);
        }

        // UTS: rest/unit/RSP4/history-auth-error-2
        [UtsFact]
        [Trait("spec", "RSP4")]
        public async Task RSP4_HistoryAuthError()
        {
            // Setup
            var channelName = "test-RSP-error2-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(401, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40101,
                            ["statusCode"] = 401,
                            ["message"] = "Invalid credentials",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "invalid.key:secret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Channels.Get(channelName).Presence.HistoryAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40101);
            ((int?)error.StatusCode).Should().Be(401);
        }

        // UTS: rest/unit/RSP3/get-channel-not-found-4
        [UtsFact]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetChannelNotFound()
        {
            // Setup
            var channelName = "test-RSP-error3-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(404, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["code"] = 40400,
                            ["statusCode"] = 404,
                            ["message"] = "Channel not found",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps and Assertions
            Func<Task> act = () => client.Channels.Get(channelName).Presence.GetAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40400);
            ((int?)error.StatusCode).Should().Be(404);
        }

        // UTS: rest/unit/RSP3/get-standard-headers-5
        [UtsFact]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetStandardHeaders()
        {
            // Setup
            var channelName = "test-RSP-headers1-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            capturedRequests[0].Headers.ContainsKey("X-Ably-Version").Should().BeTrue();
            capturedRequests[0].Headers["Ably-Agent"].Should().Contain("ably-");
            capturedRequests[0].Headers.ContainsKey("Accept").Should().BeTrue();
        }

        // UTS: rest/unit/RSP4/history-auth-header-3
        [UtsFact]
        [Trait("spec", "RSP4")]
        public async Task RSP4_HistoryAuthHeader()
        {
            // Setup
            var channelName = "test-RSP-headers2-" + Guid.NewGuid().ToString("N");
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

            // Test Steps
            await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            capturedRequests[0].Headers.ContainsKey("Authorization").Should().BeTrue();
            capturedRequests[0].Headers["Authorization"].Should().StartWith("Basic ");
        }

        // UTS: rest/unit/RSP3/get-request-id-enabled-6
        // DEVIATION: see deviations.md
        [DeviationFact("RSC7c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetRequestIdEnabled()
        {
            // Setup
            var channelName = "test-RSP-headers3-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AddRequestIds = true,
            });

            // Test Steps
            await client.Channels.Get(channelName).Presence.GetAsync();

            // Assertions
            capturedRequests[0].Url.QueryParams.ContainsKey("request_id").Should().BeTrue();
            capturedRequests[0].Url.QueryParams["request_id"].Should().NotBeNullOrEmpty();
        }

        // UTS: rest/unit/RSP5/presence-action-mapping-8
        [UtsFact]
        [Trait("spec", "RSP5")]
        public async Task RSP5_PresenceActionMapping()
        {
            // Setup
            var channelName = "test-RSP-action1-" + Guid.NewGuid().ToString("N");
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
                            ["action"] = 0,
                            ["clientId"] = "c1",
                        },
                        new JObject
                        {
                            ["action"] = 1,
                            ["clientId"] = "c2",
                        },
                        new JObject
                        {
                            ["action"] = 2,
                            ["clientId"] = "c3",
                        },
                        new JObject
                        {
                            ["action"] = 3,
                            ["clientId"] = "c4",
                        },
                        new JObject
                        {
                            ["action"] = 4,
                            ["clientId"] = "c5",
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var result = await client.Channels.Get(channelName).Presence.HistoryAsync();

            // Assertions
            result.Items[0].Action.Should().Be(PresenceAction.Absent);
            result.Items[1].Action.Should().Be(PresenceAction.Present);
            result.Items[2].Action.Should().Be(PresenceAction.Enter);
            result.Items[3].Action.Should().Be(PresenceAction.Leave);
            result.Items[4].Action.Should().Be(PresenceAction.Update);

            // Note: Action values may vary by SDK. The wire protocol uses:
            // - 0 = absent
            // - 1 = present
            // - 2 = enter
            // - 3 = leave (some SDKs use 4)
            // - 4 = update (some SDKs use 3)
            //
            // Verify against your SDK's specific mapping.
        }
    }
}
