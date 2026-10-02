using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration
{
    /// <summary>
    /// Derived from uts/realtime/integration/channel_history_test.md in ably/specification.
    ///
    /// Spec points: RTL10d
    ///
    /// Integration tier: nothing sits in front of the client, so the channel name carries a
    /// <see cref="UtsSandbox.RandomId"/> suffix and the history read is a wall-clock poll — history
    /// lags a publish, so a single fetch straight after the third ACK would flake.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    ///
    /// The spec's <c>AWAIT pub_channel.publish(...)</c> and <c>AWAIT pub_channel.attach()</c> reject
    /// on failure; .NET returns a <see cref="Result"/> rather than throwing, so the equivalent
    /// translation asserts <c>IsSuccess</c> on it. Unlike the other realtime integration specs this
    /// one does not pass <c>autoConnect: false</c>, so the library default is kept and the explicit
    /// <c>connect()</c> the spec makes is kept with it.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ChannelHistoryTests : UtsRealtimeIntegrationTestBase
    {
        public ChannelHistoryTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTL10d/history-cross-client-0
        [Fact]
        public async Task RTL10d_HistoryCrossClient()
        {
            var channelName = "history-RTL10d-" + UtsSandbox.RandomId();

            var publisher = await SandboxRealtimeClient();
            var subscriber = await SandboxRealtimeClient();

            publisher.Connect();
            subscriber.Connect();

            await AwaitConnectionState(
                publisher.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));
            await AwaitConnectionState(
                subscriber.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(10));

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            var pubAttach = await pubChannel.AttachAsync();
            pubAttach.IsSuccess.Should().BeTrue("attach must succeed: {0}", pubAttach.Error);

            var subAttach = await subChannel.AttachAsync();
            subAttach.IsSuccess.Should().BeTrue("attach must succeed: {0}", subAttach.Error);

            // Publish messages from the publisher client and await confirmation.
            var firstPublish = await pubChannel.PublishAsync("event1", "data1");
            firstPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", firstPublish.Error);

            var secondPublish = await pubChannel.PublishAsync("event2", "data2");
            secondPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", secondPublish.Error);

            var thirdPublish = await pubChannel.PublishAsync("event3", "data3");
            thirdPublish.IsSuccess.Should().BeTrue("publish must succeed: {0}", thirdPublish.Error);

            // Retrieve history from the subscriber client, polling until all messages appear.
            Func<Task<PaginatedResult<Message>>> fetchHistory = async () =>
            {
                var result = await subChannel.HistoryAsync();
                return result.Items.Count == 3 ? result : null;
            };

            var history = await UtsSandbox.WallClockPollUntil(
                fetchHistory,
                "the subscriber's history to contain all three published messages",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            history.Items.Should().HaveCount(3);

            // The default order is backwards: newest first.
            history.Items[0].Name.Should().Be("event3");
            history.Items[0].Data.Should().Be("data3");

            history.Items[1].Name.Should().Be("event2");
            history.Items[1].Data.Should().Be("data2");

            history.Items[2].Name.Should().Be("event1");
            history.Items[2].Data.Should().Be("data1");

            publisher.Close();
            subscriber.Close();
        }
    }
}
