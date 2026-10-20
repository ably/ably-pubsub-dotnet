// Pagination Integration Tests (TG1, TG2, TG3, TG4, TG5)
// Derived from uts/rest/integration/pagination.md
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
    public class PaginationTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 90000;

        public PaginationTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/TG1/items-and-navigation-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "TG1")]
        public async Task TG1_ItemsAndNavigation()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "pagination-basic-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Publish enough messages to require pagination
            for (var i = 1; i <= 15; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            // Poll until all messages are persisted
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 15;
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 15 items");

            // Test Steps
            // Request with small limit to force pagination
            var page1 = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });

            // Assertions
            // TG1 - items contains array of results
            // ASSERT page1.items IS List: guaranteed at compile time (Items is a List<Message>)
            page1.Items.Should().NotBeNull();
            page1.Items.Should().HaveCount(5);

            // TG2 - hasNext/isLast indicate more pages
            page1.HasNext.Should().BeTrue();
            page1.IsLast.Should().BeFalse();
        }

        // UTS: rest/integration/TG3/next-retrieves-page-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "TG3")]
        public async Task TG3_NextRetrievesPage()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "pagination-next-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Publish messages
            for (var i = 1; i <= 12; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            // Poll until all messages are persisted
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 12;
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 12 items");

            // Test Steps
            var page1 = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });
            var page2 = await page1.NextAsync();
            var page3 = await page2.NextAsync();

            // Assertions
            page1.Items.Should().HaveCount(5);
            page2.Items.Should().HaveCount(5);
            page3.Items.Should().HaveCount(2); // Remaining messages

            // Verify no duplicate messages across pages
            var allIds = new List<string>();
            foreach (var page in new[] { page1, page2, page3 })
            {
                foreach (var item in page.Items)
                {
                    allIds.Should().NotContain(item.Id);
                    allIds.Add(item.Id);
                }
            }

            allIds.Should().HaveCount(12);
        }

        // UTS: rest/integration/TG4/first-retrieves-page-0
        [Fact(Skip = "Missing SDK public API: PaginatedResult#first (TG4); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "TG4")]
        public Task TG4_FirstRetrievesPage()
        {
            // NOTE: PaginatedResult<T> exposes NextAsync, HasNext and IsLast but no FirstAsync (only the
            // HttpPaginatedResponse returned by request() has one), so page2.first() can't be called and the whole
            // test is a stub. Spec lines:
            // client = Rest(options: ClientOptions(key: api_key, endpoint: "nonprod:sandbox"))
            // channel_name = "pagination-first-" + random_id()
            // channel = client.channels.get(channel_name)
            // FOR i IN 1..10:
            // AWAIT channel.publish(name: "event-" + str(i), data: str(i))
            // # Poll until all messages are persisted
            // poll_until(
            // condition: FUNCTION() =>
            // result = AWAIT channel.history()
            // RETURN result.items.length == 10,
            // interval: 500ms,
            // timeout: 15s
            // )
            // page1 = AWAIT channel.history(limit: 3)
            // page2 = AWAIT page1.next()
            // first_page = AWAIT page2.first()
            // # first_page should have same items as page1
            // ASSERT first_page.items.length == page1.items.length
            // FOR i IN 0..first_page.items.length:
            // ASSERT first_page.items[i].id == page1.items[i].id
            // NOTE: the loop bound 0..first_page.items.length is ambiguous (inclusive would index past the end);
            // read it as 0..length-1 when the test is regenerated.
            return Task.CompletedTask;
        }

        // UTS: rest/integration/TG5/iterate-all-pages-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "TG5")]
        public async Task TG5_IterateAllPages()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "pagination-iterate-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Publish known set of messages
            var messageCount = 25;
            for (var i = 1; i <= messageCount; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            // Poll until all messages are persisted
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == messageCount;
                },
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has all messages");

            // Test Steps
            var allMessages = new List<Message>();
            var page = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 7 });

            while (true)
            {
                allMessages.AddRange(page.Items);

                if (!page.HasNext)
                {
                    break;
                }

                page = await page.NextAsync();
            }

            // Assertions
            allMessages.Should().HaveCount(messageCount);

            // Verify all messages retrieved
            var eventNames = allMessages.Select(msg => msg.Name).ToList();
            for (var i = 1; i <= messageCount; i++)
            {
                eventNames.Should().Contain("event-" + i);
            }
        }

        // UTS: rest/integration/TG3/next-last-page-null-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "TG3")]
        public async Task TG3_NextLastPageNull()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = ApiKey });
            var channelName = "pagination-lastnext-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Publish just a few messages
            for (var i = 1; i <= 3; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            // Poll until messages are persisted
            await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 3 items");

            // Test Steps
            var page = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 10 }); // Larger than message count

            // Assertions
            page.Items.Should().HaveCount(3);
            page.HasNext.Should().BeFalse();
            page.IsLast.Should().BeTrue();

            // Calling next() should return null (or empty result)
            var nextPage = await page.NextAsync();
            (nextPage == null || nextPage.Items.Count == 0).Should().BeTrue("next() on the last page returns null or an empty page");
        }
    }
}
