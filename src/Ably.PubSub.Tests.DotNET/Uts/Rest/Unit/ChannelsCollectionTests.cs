using System;
using System.Linq;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/channels_collection.md in ably/specification.
    ///
    /// Spec points: RSN1, RSN2, RSN3a, RSN4a, RSN4b
    ///
    /// The collection is in-memory and nothing here reaches the network, as the spec says. The .NET
    /// names are the spec's methods spelled for this SDK: <c>exists()</c> is <c>Exists(name)</c>,
    /// <c>get()</c> is <c>Get(name)</c>, <c>release()</c> is <c>Release(name)</c> (which also returns
    /// whether anything was removed, a return the spec does not read), the subscript is the indexer, and
    /// the spec's list comprehension over the collection is LINQ over <c>IChannels{IHttpChannel}</c>.
    ///
    /// The spec file's header also lists RSN3b and RSN3c, for which it defines no tests.
    /// </summary>
    public class ChannelsCollectionTests : UtsTestBase
    {
        public ChannelsCollectionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSN1/channels-collection-accessible-0
        [Fact]
        public void RSN1_ChannelsCollectionAccessible()
        {
            var client = ChannelsClient();

            var channels = client.Channels;

            // The spec's "channels IS RestChannels" is a static guarantee here, because
            // PubSubHttpClient.Channels is itself typed HttpChannels and the runtime check cannot fail.
            // What carries the coverage is that the collection is there and is the IChannels<IHttpChannel>
            // collection of RestChannel objects RSN1 describes.
            channels.Should().NotBeNull();
            channels.Should().BeAssignableTo<IChannels<IHttpChannel>>();
        }

        // UTS: rest/unit/RSN2/check-channel-exists-0
        [Fact]
        public void RSN2_CheckChannelExists()
        {
            var channelName = "test-RSN2-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            // Before creating any channel.
            var existsBefore = client.Channels.Exists(channelName);

            // Create the channel.
            _ = client.Channels.Get(channelName);

            // After creating the channel.
            var existsAfter = client.Channels.Exists(channelName);

            // Check for a non-existent channel.
            var otherChannelName = "test-RSN2-other-" + UtsSandbox.RandomId();
            var existsOther = client.Channels.Exists(otherChannelName);

            existsBefore.Should().BeFalse();
            existsAfter.Should().BeTrue();
            existsOther.Should().BeFalse();
        }

        // UTS: rest/unit/RSN2/iterate-channels-1
        [Fact]
        public void RSN2_IterateChannels()
        {
            var channelNameA = "test-RSN2-a-" + UtsSandbox.RandomId();
            var channelNameB = "test-RSN2-b-" + UtsSandbox.RandomId();
            var channelNameC = "test-RSN2-c-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            // Create several channels.
            _ = client.Channels.Get(channelNameA);
            _ = client.Channels.Get(channelNameB);
            _ = client.Channels.Get(channelNameC);

            // Iterate channels.
            var channelNames = client.Channels.Select(channel => channel.Name).ToList();

            channelNames.Should().Contain(channelNameA);
            channelNames.Should().Contain(channelNameB);
            channelNames.Should().Contain(channelNameC);
            channelNames.Should().HaveCount(3);
        }

        // UTS: rest/unit/RSN3a/get-creates-new-channel-0
        [Fact]
        public void RSN3a_GetCreatesNewChannel()
        {
            var channelName = "test-RSN3a-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            var channel = client.Channels.Get(channelName);

            // Unlike RSN1 this one is a real runtime check: Get is declared to return the IHttpChannel
            // interface, and HttpChannel is the RestChannel the spec means.
            channel.Should().BeOfType<HttpChannel>();
            channel.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
        }

        // UTS: rest/unit/RSN3a/get-returns-existing-channel-1
        [Fact]
        public void RSN3a_GetReturnsExistingChannel()
        {
            var channelName = "test-RSN3a-existing-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            var channel1 = client.Channels.Get(channelName);
            var channel2 = client.Channels.Get(channelName);

            // The spec's "IS SAME AS": the same object reference.
            channel1.Should().BeSameAs(channel2);
            channel1.Name.Should().Be(channelName);
        }

        // UTS: rest/unit/RSN3a/subscript-creates-or-returns-2
        [Fact]
        public void RSN3a_SubscriptCreatesOrReturns()
        {
            var channelName = "test-RSN3a-subscript-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            var channel1 = client.Channels[channelName];
            var channel2 = client.Channels.Get(channelName);
            var channel3 = client.Channels[channelName];

            channel1.Should().BeSameAs(channel2);
            channel2.Should().BeSameAs(channel3);
            channel1.Name.Should().Be(channelName);
        }

        // UTS: rest/unit/RSN4a/release-removes-channel-0
        [Fact]
        public void RSN4a_ReleaseRemovesChannel()
        {
            var channelName = "test-RSN4a-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            _ = client.Channels.Get(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();

            client.Channels.Release(channelName);

            client.Channels.Exists(channelName).Should().BeFalse();
        }

        // UTS: rest/unit/RSN4b/release-nonexistent-noop-0
        [Fact]
        public void RSN4b_ReleaseNonexistentNoop()
        {
            var channelName = "test-RSN4b-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            // Release a channel that was never created — the spec requires this to return without error.
            Action release = () => client.Channels.Release(channelName);

            release.Should().NotThrow();

            client.Channels.Exists(channelName).Should().BeFalse();
        }

        // UTS: rest/unit/RSN3a/get-after-release-new-instance-3
        [Fact]
        public void RSN3a_GetAfterReleaseNewInstance()
        {
            var channelName = "test-RSN3a-release-" + UtsSandbox.RandomId();

            var client = ChannelsClient();

            var channel1 = client.Channels.Get(channelName);

            client.Channels.Release(channelName);

            var channel2 = client.Channels.Get(channelName);

            // The spec's "IS NOT SAME AS": a fresh instance, not the released one.
            channel1.Should().NotBeSameAs(channel2);
            channel2.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
        }

        /// <summary>
        /// The spec's <c>Rest(options: ClientOptions(key: "appId.keyId:keySecret"))</c>, which is what
        /// <c>UtsTestBase.RestClient</c> builds by default. The spec notes that no mock infrastructure is
        /// needed for these tests; one is installed all the same so that a request nobody expects fails
        /// inside the mock rather than reaching the network.
        /// </summary>
        private PubSubHttpClient ChannelsClient() => RestClient(new MockHttpClient());
    }
}
