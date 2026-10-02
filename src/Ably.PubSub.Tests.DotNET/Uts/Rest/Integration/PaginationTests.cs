using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/pagination.md in ably/specification.
    ///
    /// Spec points: TG1, TG2, TG3, TG4, TG5
    ///
    /// Three translation notes that apply to the whole file:
    ///
    /// The spec's hasNext() and isLast() are the HasNext and IsLast properties here, and next() is
    /// NextAsync(). Those are spellings, not gaps.
    ///
    /// first() is a genuine gap: PaginatedResult&lt;T&gt; carries FirstQueryParams but no FirstAsync()
    /// — only HttpPaginatedResponse has one, and channel history returns the plain
    /// PaginatedResult&lt;Message&gt;. Rather than drop TG4's behavioural coverage, the one place that
    /// needs it goes through FirstPage below, which replays FirstQueryParams through
    /// HistoryAsync(query). That is exactly what a FirstAsync() would do: NextAsync() is
    /// ExecuteDataQueryFunc(NextQueryParams), and ExecuteDataQueryFunc for a history page *is*
    /// HttpChannel.HistoryAsync(PaginatedRequestParams). The missing public member is recorded
    /// separately.
    ///
    /// Every poll that follows a publish goes through UtsSandbox.WallClockPollUntil at the interval
    /// and timeout the spec asks for, which differ per test. Nothing here registers server-side
    /// state, so no test needs a cleanup block — a published message expires on its own and
    /// PubSubHttpClient is not disposable — but every channel name carries a UtsSandbox.RandomId
    /// suffix because the sandbox app is shared across the run.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PaginationTests : UtsIntegrationTestBase
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        public PaginationTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/TG1/items-and-navigation-0
        // Covers TG1 (items carries the current page) and TG2 (hasNext/isLast).
        [Fact]
        public async Task TG1_ItemsAndNavigation()
        {
            var client = await SandboxRestClient();
            var channelName = "pagination-basic-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            // Enough messages to require pagination.
            for (var i = 1; i <= 15; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 15 ? result : null;
                },
                "all fifteen messages to be persisted",
                TimeSpan.FromSeconds(15),
                PollInterval);

            // A small limit, to force pagination.
            var page1 = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });

            // TG1 — items contains the results for this page. "items IS List" is a static guarantee
            // here, Items being typed List<Message>, so only the not-null half can be asserted.
            page1.Items.Should().NotBeNull();
            page1.Items.Should().HaveCount(5);

            // TG2 — hasNext/isLast indicate more pages.
            page1.HasNext.Should().BeTrue();
            page1.IsLast.Should().BeFalse();
        }

        // UTS: rest/integration/TG3/next-retrieves-page-0
        [Fact]
        public async Task TG3_NextRetrievesPage()
        {
            var client = await SandboxRestClient();
            var channelName = "pagination-next-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            for (var i = 1; i <= 12; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 12 ? result : null;
                },
                "all twelve messages to be persisted",
                TimeSpan.FromSeconds(15),
                PollInterval);

            var page1 = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });
            var page2 = await page1.NextAsync();
            var page3 = await page2.NextAsync();

            page1.Items.Should().HaveCount(5);
            page2.Items.Should().HaveCount(5);

            // The remaining messages.
            page3.Items.Should().HaveCount(2);

            // No duplicate messages across the pages.
            var allIds = new List<string>();
            foreach (var page in new[] { page1, page2, page3 })
            {
                foreach (var item in page.Items)
                {
                    allIds.Contains(item.Id).Should().BeFalse(
                        "message id {0} must not appear on more than one page",
                        item.Id);
                    allIds.Add(item.Id);
                }
            }

            allIds.Should().HaveCount(12);
        }

        // UTS: rest/integration/TG4/first-retrieves-page-0
        [Fact]
        public async Task TG4_FirstRetrievesPage()
        {
            var client = await SandboxRestClient();
            var channelName = "pagination-first-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            for (var i = 1; i <= 10; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 10 ? result : null;
                },
                "all ten messages to be persisted",
                TimeSpan.FromSeconds(15),
                PollInterval);

            var page1 = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 3 });
            var page2 = await page1.NextAsync();

            // The spec's page2.first(). See the class summary for why this is a replay of the
            // first-page link rather than a FirstAsync() call.
            var firstPage = await FirstPage(channel, page2);

            firstPage.Items.Should().HaveCount(page1.Items.Count);

            for (var i = 0; i < firstPage.Items.Count; i++)
            {
                firstPage.Items[i].Id.Should().Be(page1.Items[i].Id);
            }
        }

        // UTS: rest/integration/TG5/iterate-all-pages-0
        [Fact]
        public async Task TG5_IterateAllPages()
        {
            const int messageCount = 25;

            var client = await SandboxRestClient();
            var channelName = "pagination-iterate-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            for (var i = 1; i <= messageCount; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == messageCount ? result : null;
                },
                "all twenty-five messages to be persisted",
                TimeSpan.FromSeconds(30),
                PollInterval);

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

            allMessages.Should().HaveCount(messageCount);

            var eventNames = allMessages.Select(message => message.Name).ToList();
            for (var i = 1; i <= messageCount; i++)
            {
                eventNames.Contains("event-" + i).Should().BeTrue(
                    "event-{0} should be among the messages the iteration retrieved",
                    i);
            }
        }

        // UTS: rest/integration/TG3/next-last-page-null-1
        [Fact]
        public async Task TG3_NextLastPageNull()
        {
            var client = await SandboxRestClient();
            var channelName = "pagination-lastnext-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            for (var i = 1; i <= 3; i++)
            {
                await channel.PublishAsync("event-" + i, i.ToString());
            }

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3 ? result : null;
                },
                "all three messages to be persisted",
                TimeSpan.FromSeconds(10),
                PollInterval);

            // A limit larger than the message count.
            var page = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 10 });

            page.Items.Should().HaveCount(3);
            page.HasNext.Should().BeFalse();
            page.IsLast.Should().BeTrue();

            // The spec allows either null or an empty result. NextAsync() takes the second branch:
            // with no next link it returns a fresh, empty PaginatedResult rather than null.
            var nextPage = await page.NextAsync();
            nextPage.Should().NotBeNull();
            nextPage.Items.Should().BeEmpty();
        }

        /// <summary>
        /// The spec's <c>page.first()</c>, in the one place TG4 needs it.
        ///
        /// PaginatedResult&lt;T&gt; exposes FirstQueryParams but no FirstAsync(), so the first-page
        /// link is replayed through the same entry point NextAsync() uses internally.
        /// </summary>
        private static async Task<PaginatedResult<Message>> FirstPage(
            IHttpChannel channel,
            PaginatedResult<Message> page)
        {
            page.FirstQueryParams.Should().NotBeNull();
            page.FirstQueryParams.IsEmpty.Should().BeFalse(
                "the response must carry a rel=\"first\" Link header for the first page to be fetched");

            return await channel.HistoryAsync(page.FirstQueryParams);
        }
    }
}
