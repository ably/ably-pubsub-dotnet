// REST Channel History Integration Tests (RSL2a, RSL2b, RSL2b1, RSL2b2, RSL2b3)
// Derived from uts/rest/integration/history.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    public class HistoryTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public HistoryTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL2a/history-returns-messages-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL2a")]
        public async Task RSL2a_HistoryReturnsMessages()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "history-test-RSL2a-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish some messages
            await channel.PublishAsync("event1", "data1");
            await channel.PublishAsync("event2", "data2");
            await channel.PublishAsync("event3", new JObject { ["key"] = "value" });

            // Poll until messages appear in history
            var history = await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 3 items");

            // Assertions
            history.Items.Should().HaveCount(3);

            // Default order is backwards (newest first)
            history.Items[0].Name.Should().Be("event3");
            JToken.DeepEquals(JToken.FromObject(history.Items[0].Data), new JObject { ["key"] = "value" }).Should().BeTrue("history.items[0].data == {{ \"key\": \"value\" }} (found {0})", history.Items[0].Data);

            history.Items[1].Name.Should().Be("event2");
            history.Items[1].Data.Should().Be("data2");

            history.Items[2].Name.Should().Be("event1");
            history.Items[2].Data.Should().Be("data1");

            // All messages should have timestamps
            history.Items.Should().NotContain(msg => !msg.Timestamp.HasValue);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL2b1/history-direction-forwards-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL2b1")]
        public async Task RSL2b1_HistoryDirectionForwards()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "history-direction-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish messages - ordering is determined by server timestamp
            await channel.PublishAsync("first", "1");
            await channel.PublishAsync("second", "2");
            await channel.PublishAsync("third", "3");

            // Poll until all messages appear
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 3 items");

            var history = await channel.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            // Assertions
            history.Items.Should().HaveCount(3);
            history.Items[0].Name.Should().Be("first");
            history.Items[1].Name.Should().Be("second");
            history.Items[2].Name.Should().Be("third");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL2b2/history-limit-parameter-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL2b2")]
        public async Task RSL2b2_HistoryLimitParameter()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "history-limit-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish multiple messages
            for (var i = 1; i <= 10; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            // Poll until all messages are persisted
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 10;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 10 items");

            var history = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });

            // Assertions
            history.Items.Should().HaveCount(5);

            // Should get the 5 most recent (backwards direction by default)
            history.Items[0].Name.Should().Be("event-10");
            history.Items[4].Name.Should().Be("event-6");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL2b3/history-time-range-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL2b3")]
        public async Task RSL2b3_HistoryTimeRange()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "history-timerange-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Publish early messages
            await channel.PublishAsync("early1", "e1");
            await channel.PublishAsync("early2", "e2");

            // Small delay to help ensure server assigns distinct timestamps between batches
            await Task.Delay(2); // spec WAIT kept: wall-clock

            // Publish late messages
            await channel.PublishAsync("late1", "l1");
            await channel.PublishAsync("late2", "l2");

            // Poll until all messages appear and retrieve with server timestamps
            var allMessages = await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 4 ? result.Items : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 4 items");

            // Use server-assigned timestamps to define the time boundary.
            // Client-side now() must not be used here — client and server clocks may
            // differ, and publishes may complete within the same client-clock millisecond.
            var earlyTimestamps = allMessages.Where(m => m.Name.StartsWith("early")).Select(m => m.Timestamp.Value.ToUnixTimeMilliseconds()).ToList();
            var lateTimestamps = allMessages.Where(m => m.Name.StartsWith("late")).Select(m => m.Timestamp.Value.ToUnixTimeMilliseconds()).ToList();

            var maxEarlyTs = earlyTimestamps.Max();
            var minLateTs = lateTimestamps.Min();
            var timeBoundary = (long)Math.Floor((maxEarlyTs + minLateTs) / 2.0);

            // Query only early messages (up to the boundary)
            var earlyHistory = await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(maxEarlyTs - 1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(timeBoundary),
            });

            // Query only late messages (from the boundary onwards)
            var lateHistory = await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(timeBoundary + 1),
                End = DateTimeOffset.FromUnixTimeMilliseconds(minLateTs + 1000),
            });

            // Assertions
            earlyHistory.Items.Count.Should().BeGreaterThanOrEqualTo(1);
            lateHistory.Items.Count.Should().BeGreaterThanOrEqualTo(1);

            // Early messages should contain "early" names
            earlyHistory.Items.Should().Contain(msg => msg.Name.StartsWith("early"));

            // Late messages should contain "late" names
            lateHistory.Items.Should().Contain(msg => msg.Name.StartsWith("late"));
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL2/history-empty-channel-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL2")]
        public async Task RSL2_HistoryEmptyChannel()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });

            // Use a fresh channel with no messages
            var channelName = "history-empty-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var history = await channel.HistoryAsync();

            // Assertions
            // ASSERT history.items IS List: guaranteed at compile time (Items is a List<Message>)
            history.Items.Should().NotBeNull();
            history.Items.Should().HaveCount(0);
            history.HasNext.Should().BeFalse();
            history.IsLast.Should().BeTrue();
        }
    }
}
