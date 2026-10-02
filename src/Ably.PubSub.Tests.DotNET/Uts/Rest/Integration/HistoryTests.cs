using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/history.md in ably/specification.
    ///
    /// Spec points: RSL2, RSL2a, RSL2b1, RSL2b2, RSL2b3
    ///
    /// Four translation notes that apply to the whole file:
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// The spec passes history parameters as a map of unix milliseconds. The .NET equivalent is
    /// <see cref="PaginatedRequestParams"/>, whose Start and End are DateTimeOffset and whose
    /// Direction is the <see cref="QueryDirection"/> enum, so each spec value is converted at the
    /// call site. The spec's hasNext() and isLast() are the HasNext and IsLast properties.
    ///
    /// Every poll that follows a publish goes through UtsSandbox.WallClockPollUntil at the interval
    /// and timeout the spec asks for: nothing is consistent immediately after a write, and a fixed
    /// delay would either flake or spend the whole budget.
    ///
    /// No test here registers server-side state, so none needs a cleanup block — a published message
    /// expires on its own, a channel needs no release over REST, and PubSubHttpClient is not
    /// disposable. Every channel name still carries a UtsSandbox.RandomId suffix, because the sandbox
    /// app is shared across the run.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class HistoryTests : UtsIntegrationTestBase
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        public HistoryTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSL2a/history-returns-messages-0
        [Fact]
        public async Task RSL2a_HistoryReturnsMessages()
        {
            var client = await SandboxRestClient();
            var channelName = "history-test-RSL2a-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event1", "data1");
            await channel.PublishAsync("event2", "data2");
            await channel.PublishAsync("event3", JObject.Parse("{\"key\":\"value\"}"));

            var history = await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3 ? result : null;
                },
                "the three published messages to appear in history",
                PollTimeout,
                PollInterval);

            history.Items.Should().HaveCount(3);

            // The default direction is backwards, so the newest message is first.
            history.Items[0].Name.Should().Be("event3");

            // The spec's native map is a JObject here: the JSON encoder decodes an object payload
            // with JsonHelper.Deserialize and no target type, which yields a JToken.
            history.Items[0].Data.Should().BeOfType<JObject>();
            JToken.DeepEquals((JToken)history.Items[0].Data, JObject.Parse("{\"key\":\"value\"}"))
                .Should().BeTrue();

            history.Items[1].Name.Should().Be("event2");
            history.Items[1].Data.Should().Be("data2");

            history.Items[2].Name.Should().Be("event1");
            history.Items[2].Data.Should().Be("data1");

            history.Items.Should().OnlyContain(message => message.Timestamp.HasValue);
        }

        // UTS: rest/integration/RSL2b1/history-direction-forwards-0
        [Fact]
        public async Task RSL2b1_HistoryDirectionForwards()
        {
            var client = await SandboxRestClient();
            var channelName = "history-direction-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            // Ordering is determined by the server timestamp, not by the order of these calls.
            await channel.PublishAsync("first", "1");
            await channel.PublishAsync("second", "2");
            await channel.PublishAsync("third", "3");

            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 3 ? result : null;
                },
                "all three messages to appear in history",
                PollTimeout,
                PollInterval);

            var history = await channel.HistoryAsync(
                new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            history.Items.Should().HaveCount(3);
            history.Items[0].Name.Should().Be("first");
            history.Items[1].Name.Should().Be("second");
            history.Items[2].Name.Should().Be("third");
        }

        // UTS: rest/integration/RSL2b2/history-limit-parameter-0
        [Fact]
        public async Task RSL2b2_HistoryLimitParameter()
        {
            var client = await SandboxRestClient();
            var channelName = "history-limit-" + UtsSandbox.RandomId();
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
                PollTimeout,
                PollInterval);

            var history = await channel.HistoryAsync(new PaginatedRequestParams { Limit = 5 });

            history.Items.Should().HaveCount(5);

            // Backwards is the default direction, so these are the five most recent.
            history.Items[0].Name.Should().Be("event-10");
            history.Items[4].Name.Should().Be("event-6");
        }

        // UTS: rest/integration/RSL2b3/history-time-range-0
        [Fact]
        public async Task RSL2b3_HistoryTimeRange()
        {
            var client = await SandboxRestClient();
            var channelName = "history-timerange-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("early1", "e1");
            await channel.PublishAsync("early2", "e2");

            // The spec's "WAIT 2ms". This is not a settling wait — it is the spec's deliberate
            // separator so the server assigns distinct timestamps to the two batches — so it stays a
            // delay rather than becoming a poll.
            await Task.Delay(2);

            await channel.PublishAsync("late1", "l1");
            await channel.PublishAsync("late2", "l2");

            var allMessages = await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count == 4 ? result.Items : null;
                },
                "all four messages to appear in history",
                PollTimeout,
                PollInterval);

            allMessages.Should().OnlyContain(
                message => message.Timestamp.HasValue,
                "the time boundary is computed from the server-assigned timestamps");

            // Server timestamps, not a client clock: the two clocks may differ and two publishes can
            // complete within the same client-clock millisecond.
            var earlyTimestamps = allMessages
                .Where(message => message.Name.StartsWith("early", StringComparison.Ordinal))
                .Select(message => message.Timestamp.Value.ToUnixTimeInMilliseconds())
                .ToList();

            var lateTimestamps = allMessages
                .Where(message => message.Name.StartsWith("late", StringComparison.Ordinal))
                .Select(message => message.Timestamp.Value.ToUnixTimeInMilliseconds())
                .ToList();

            var maxEarlyTimestamp = earlyTimestamps.Max();
            var minLateTimestamp = lateTimestamps.Min();

            // Integer division of two non-negative longs is the spec's floor.
            var timeBoundary = (maxEarlyTimestamp + minLateTimestamp) / 2;

            var earlyHistory = await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(maxEarlyTimestamp - 1000),
                End = DateTimeOffset.FromUnixTimeMilliseconds(timeBoundary),
            });

            var lateHistory = await channel.HistoryAsync(new PaginatedRequestParams
            {
                Start = DateTimeOffset.FromUnixTimeMilliseconds(timeBoundary + 1),
                End = DateTimeOffset.FromUnixTimeMilliseconds(minLateTimestamp + 1000),
            });

            earlyHistory.Items.Should().HaveCountGreaterOrEqualTo(1);
            lateHistory.Items.Should().HaveCountGreaterOrEqualTo(1);

            earlyHistory.Items.Should().Contain(
                message => message.Name.StartsWith("early", StringComparison.Ordinal));
            lateHistory.Items.Should().Contain(
                message => message.Name.StartsWith("late", StringComparison.Ordinal));
        }

        // UTS: rest/integration/RSL2/history-empty-channel-0
        [Fact]
        public async Task RSL2_HistoryEmptyChannel()
        {
            var client = await SandboxRestClient();

            // A fresh channel with no messages.
            var channelName = "history-empty-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            var history = await channel.HistoryAsync();

            // "items IS List" is a static guarantee here — Items is typed List<Message> — so the
            // runtime type assertion cannot fail; asserting it is not null keeps that half of the
            // spec's assertion meaningful.
            history.Items.Should().NotBeNull();
            history.Items.Should().BeEmpty();
            history.HasNext.Should().BeFalse();
            history.IsLast.Should().BeTrue();
        }
    }
}
