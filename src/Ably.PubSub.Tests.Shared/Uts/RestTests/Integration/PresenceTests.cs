// REST Presence Integration Tests (RSP1, RSP3, RSP3a, RSP4, RSP4b, RSP5)
// Derived from uts/rest/integration/presence.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class PresenceTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        private const string FixturesChannel = "persisted:presence_fixtures";

        public PresenceTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP1/access-presence-from-channel-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP1")]
        public Task RSP1_AccessPresenceFromChannel()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var presence = channel.Presence;

            presence.Should().NotBeNull();

            // ASSERT presence IS RestPresence: guaranteed at compile time (channel.Presence is an IPresence, the SDK's RestPresence)
            return Task.CompletedTask;
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/get-presence-members-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetPresenceMembers()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var result = await channel.Presence.GetAsync();

            // ASSERT result IS PaginatedResult: guaranteed at compile time (GetAsync returns PaginatedResult<PresenceMessage>)
            result.Items.Count.Should().BeGreaterThanOrEqualTo(5); // At least the non-encrypted fixtures

            // Verify expected clients are present
            var clientIds = result.Items.Select(msg => msg.ClientId).ToList();
            clientIds.Should().Contain("client_bool");
            clientIds.Should().Contain("client_string");
            clientIds.Should().Contain("client_json");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/presence-message-fields-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_PresenceMessageFields()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var result = await channel.Presence.GetAsync();

            // Find client_string member
            var member = result.Items.FirstOrDefault(msg => msg.ClientId == "client_string");

            member.Should().NotBeNull();

            // ASSERT member IS PresenceMessage: guaranteed at compile time (Items is a List<PresenceMessage>)
            member.Action.Should().Be(PresenceAction.Present);
            member.ClientId.Should().Be("client_string");
            member.Data.Should().Be("This is a string clientData payload");
            member.ConnectionId.Should().NotBeNull();
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3a1/get-with-limit-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3a1")]
        public async Task RSP3a1_GetWithLimit()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);

            // Request with small limit
            var result = await channel.Presence.GetAsync(limit: 2);

            result.Items.Count.Should().BeLessThanOrEqualTo(2);

            // If more members exist, pagination should be available
            if (result.HasNext)
            {
                result.Items.Should().HaveCount(2);
            }
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3a2/get-with-clientid-filter-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3a2")]
        public async Task RSP3a2_GetWithClientidFilter()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_json");

            result.Items.Should().HaveCount(1);
            result.Items[0].ClientId.Should().Be("client_json");

            // The fixture has no encoding field, so data is returned as a raw string
            result.Items[0].Data.Should().BeOfType<string>();
            result.Items[0].Data.Should().Be("{ \"test\": \"This is a JSONObject clientData payload\"}");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/get-empty-channel-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_GetEmptyChannel()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Use a unique channel name that has no presence members
            var channelName = "presence-empty-" + RandomId();
            var channel = client.Channels.Get(channelName);

            var result = await channel.Presence.GetAsync();

            // ASSERT result.items IS List: guaranteed at compile time (Items is a List<PresenceMessage>)
            result.Items.Should().NotBeNull();
            result.Items.Should().HaveCount(0);
            result.HasNext.Should().BeFalse();
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP4/history-returns-events-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP4")]
        public async Task RSP4_HistoryReturnsEvents()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channelName = "presence-history-" + RandomId();

            // Use realtime client to generate presence history
            var realtime = Realtime(new ClientOptions { Key = ApiKey, ClientId = "test-client" });

            var realtimeChannel = realtime.Channels.Get(channelName);
            AssertSucceeded(await realtimeChannel.Presence.EnterAsync("entered"), "realtime_channel.presence.enter()");
            AssertSucceeded(await realtimeChannel.Presence.UpdateAsync("updated"), "realtime_channel.presence.update()");
            AssertSucceeded(await realtimeChannel.Presence.LeaveAsync("left"), "realtime_channel.presence.leave()");
            await CloseClient(realtime);

            // Poll REST history until events appear
            var restChannel = client.Channels.Get(channelName);

            var history = await UtsWait.PollUntil(
                async () =>
                {
                    var result = await restChannel.Presence.HistoryAsync();
                    return result.Items.Count >= 3 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "presence history has 3 items");

            history.Items.Count.Should().BeGreaterThanOrEqualTo(3);

            // Check for expected actions (order depends on direction)
            var actions = history.Items.Select(msg => msg.Action).ToList();
            actions.Should().Contain(PresenceAction.Enter);
            actions.Should().Contain(PresenceAction.Update);
            actions.Should().Contain(PresenceAction.Leave);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP4b1/history-time-range-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP4b1")]
        public async Task RSP4b1_HistoryTimeRange()
        {
            var client = Rest(new ClientOptions { Key = ApiKey, ClientId = "test-client" });

            var channelName = "presence-history-time-" + RandomId();

            // Record time before any presence events
            var timeBefore = DateTimeOffset.UtcNow;

            // Generate presence events via realtime
            var realtime = Realtime(new ClientOptions { Key = ApiKey, ClientId = "time-test-client" });

            var realtimeChannel = realtime.Channels.Get(channelName);
            AssertSucceeded(await realtimeChannel.Presence.EnterAsync("test"), "realtime_channel.presence.enter()");
            AssertSucceeded(await realtimeChannel.Presence.LeaveAsync(), "realtime_channel.presence.leave()");
            await CloseClient(realtime);

            var timeAfter = DateTimeOffset.UtcNow;

            // Poll until events appear
            var restChannel = client.Channels.Get(channelName);
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await restChannel.Presence.HistoryAsync();
                    return result.Items.Count >= 2;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "presence history has 2 items");

            // Query with time range
            var history = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = timeBefore,
                End = timeAfter,
            });

            history.Items.Count.Should().BeGreaterThanOrEqualTo(2);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP4b2/history-direction-forwards-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP4b2")]
        public async Task RSP4b2_HistoryDirectionForwards()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channelName = "presence-direction-" + RandomId();

            // Generate ordered presence events
            var realtime = Realtime(new ClientOptions { Key = ApiKey, ClientId = "direction-client" });

            var realtimeChannel = realtime.Channels.Get(channelName);
            AssertSucceeded(await realtimeChannel.Presence.EnterAsync("first"), "realtime_channel.presence.enter()");
            AssertSucceeded(await realtimeChannel.Presence.UpdateAsync("second"), "realtime_channel.presence.update()");
            AssertSucceeded(await realtimeChannel.Presence.UpdateAsync("third"), "realtime_channel.presence.update()");
            await CloseClient(realtime);

            // Poll until events appear
            var restChannel = client.Channels.Get(channelName);
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await restChannel.Presence.HistoryAsync();
                    return result.Items.Count >= 3;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "presence history has 3 items");

            // Get history forwards (oldest first)
            var historyForwards = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            historyForwards.Items.Count.Should().BeGreaterThanOrEqualTo(3);
            historyForwards.Items[0].Data.Should().Be("first");

            // Get history backwards (newest first) - default
            var historyBackwards = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Backwards });

            historyBackwards.Items[0].Data.Should().Be("third");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP4b3/history-limit-pagination-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP4b3")]
        public async Task RSP4b3_HistoryLimitPagination()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channelName = "presence-limit-" + RandomId();

            // Generate multiple presence events
            var realtime = Realtime(new ClientOptions { Key = ApiKey, ClientId = "limit-client" });

            var realtimeChannel = realtime.Channels.Get(channelName);
            for (var i = 1; i <= 5; i++)
            {
                AssertSucceeded(await realtimeChannel.Presence.UpdateAsync("update-" + i), "realtime_channel.presence.update()");
            }

            await CloseClient(realtime);

            // Poll until all events appear
            var restChannel = client.Channels.Get(channelName);
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await restChannel.Presence.HistoryAsync();
                    return result.Items.Count >= 5;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "presence history has 5 items");

            // Request with small limit
            var page1 = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams { Limit = 2 });

            page1.Items.Should().HaveCount(2);
            page1.HasNext.Should().BeTrue();

            // Get next page
            var page2 = await page1.NextAsync();

            page2.Should().NotBeNull();
            page2.Items.Count.Should().BeGreaterThanOrEqualTo(1);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP5/decode-string-data-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeStringData()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_string");

            result.Items.Should().HaveCount(1);
            result.Items[0].Data.Should().BeOfType<string>();
            result.Items[0].Data.Should().Be("This is a string clientData payload");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP5/decode-json-data-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeJsonData()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channel = client.Channels.Get(FixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_decoded");

            result.Items.Should().HaveCount(1);

            // ASSERT result.items[0].data IS Object/Map: JSON-decoded data is a JObject (rest-mapping.md section 5)
            result.Items[0].Data.Should().BeAssignableTo<JObject>();
            ((string)((JObject)result.Items[0].Data)["example"]["json"]).Should().Be("Object");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP5/decode-encrypted-data-2
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeEncryptedData()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var cipherKey = Convert.FromBase64String("WUP6u0K7MXI5Zeo0VppPwg==");

            // NOTE: keyLength: 128 is implied by the 16-byte key (CipherParams derives the key length from the key)
            var channel = client.Channels.Get(FixturesChannel, new ChannelOptions(new CipherParams("aes", cipherKey, CipherMode.CBC)));

            var result = await channel.Presence.GetAsync(clientId: "client_encoded");

            // The encrypted fixture should be decrypted
            result.Items.Should().HaveCount(1);
            result.Items[0].Data.Should().NotBeNull();

            // Actual decrypted value depends on fixture content
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP5/decode-history-messages-3
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP5")]
        public async Task RSP5_DecodeHistoryMessages()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            var channelName = "presence-decode-history-" + RandomId();

            // Generate presence event with JSON data
            var realtime = Realtime(new ClientOptions { Key = ApiKey, ClientId = "decode-client" });

            var jsonData = new JObject { ["key"] = "value", ["number"] = 123 };
            var realtimeChannel = realtime.Channels.Get(channelName);
            AssertSucceeded(await realtimeChannel.Presence.EnterAsync(jsonData), "realtime_channel.presence.enter()");
            await CloseClient(realtime);

            // Poll and retrieve history
            var restChannel = client.Channels.Get(channelName);
            var history = await UtsWait.PollUntil(
                async () =>
                {
                    var result = await restChannel.Presence.HistoryAsync();
                    return result.Items.Count >= 1 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "presence history has 1 item");

            // ASSERT history.items[0].data IS Object/Map: JSON-decoded data is a JObject (rest-mapping.md section 5)
            history.Items[0].Data.Should().BeAssignableTo<JObject>();
            ((string)((JObject)history.Items[0].Data)["key"]).Should().Be("value");
            ((int)((JObject)history.Items[0].Data)["number"]).Should().Be(123);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/full-pagination-3
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_FullPagination()
        {
            var client = Rest(new ClientOptions { Key = ApiKey });

            // The fixture channel has multiple members
            var channel = client.Channels.Get(FixturesChannel);

            // Request with small limit to force pagination
            var page1 = await channel.Presence.GetAsync(limit: 2);

            var allMembers = new List<PresenceMessage>();
            allMembers.AddRange(page1.Items);

            var currentPage = page1;
            while (currentPage.HasNext)
            {
                currentPage = await currentPage.NextAsync();
                allMembers.AddRange(currentPage.Items);
            }

            // Should have retrieved all fixture members
            allMembers.Count.Should().BeGreaterThanOrEqualTo(5);

            // Verify no duplicates
            var clientIds = allMembers.Select(m => m.ClientId).ToList();
            clientIds.Distinct().Count().Should().Be(clientIds.Count);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/invalid-credentials-rejected-4
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_InvalidCredentialsRejected()
        {
            var client = Rest(new ClientOptions { Key = "invalid.key:secret" });

            Func<Task> get = () => client.Channels.Get("test").Presence.GetAsync();
            var error = (await get.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(401);
            error.Code.Should().BeGreaterThanOrEqualTo(40100);
            error.Code.Should().BeLessThan(40200);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSP3/subscribe-capability-sufficient-5
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSP3")]
        public async Task RSP3_SubscribeCapabilitySufficient()
        {
            // Use key with limited capabilities (keys[3] has subscribe only)
            var restrictedKey = App.Keys[3].KeyStr;

            var client = Rest(new ClientOptions { Key = restrictedKey });

            // This should work - subscribe capability is sufficient for presence.get
            var result = await client.Channels.Get(FixturesChannel).Presence.GetAsync();
            result.Should().NotBeNull();
        }
    }
}
