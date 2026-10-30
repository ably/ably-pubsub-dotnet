// RealtimeChannel History Integration Test (RTL10d)
// Derived from uts/realtime/integration/channel_history_test.md
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

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class ChannelHistoryTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ChannelHistoryTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL10d/history-cross-client-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL10d")]
        public async Task RTL10d_HistoryCrossClient()
        {
            // Setup
            var channelName = "history-RTL10d-" + RandomId();

            var publisher = Realtime();
            var subscriber = Realtime();

            publisher.Connect();
            subscriber.Connect();

            await AwaitConnectionState(publisher.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            await AwaitConnectionState(subscriber.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            var pubChannel = publisher.Channels.Get(channelName);
            var subChannel = subscriber.Channels.Get(channelName);

            AssertSucceeded(await pubChannel.AttachAsync(), "pub_channel.attach()");
            AssertSucceeded(await subChannel.AttachAsync(), "sub_channel.attach()");

            // Test Steps
            // Publish messages from publisher client and await confirmation
            AssertSucceeded(await pubChannel.PublishAsync("event1", "data1"), "pub_channel.publish(event1)");
            AssertSucceeded(await pubChannel.PublishAsync("event2", "data2"), "pub_channel.publish(event2)");
            AssertSucceeded(await pubChannel.PublishAsync("event3", "data3"), "pub_channel.publish(event3)");

            // Retrieve history from subscriber client
            // Poll until all messages appear
            var history = await UtsWait.PollUntil<PaginatedResult<Message>>(
                async () =>
                {
                    var result = await subChannel.HistoryAsync();
                    return result.Items.Count == 3 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has 3 items");

            // Assertions
            history.Items.Should().HaveCount(3);

            // Default order is backwards (newest first)
            history.Items[0].Name.Should().Be("event3");
            history.Items[0].Data.Should().Be("data3");

            history.Items[1].Name.Should().Be("event2");
            history.Items[1].Data.Should().Be("data2");

            history.Items[2].Name.Should().Be("event1");
            history.Items[2].Data.Should().Be("data1");

            // Cleanup
            // AFTER TEST: publisher.close() and subscriber.close() are done by the base teardown
        }
    }
}
