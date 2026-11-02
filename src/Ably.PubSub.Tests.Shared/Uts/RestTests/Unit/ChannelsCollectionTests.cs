// REST Channels Collection Tests (RSN1, RSN2, RSN3a, RSN3b, RSN3c, RSN4a, RSN4b)
// Derived from uts/rest/unit/channels_collection.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    public class ChannelsCollectionTests : UtsUnitTestBase
    {
        public ChannelsCollectionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSN1/channels-collection-accessible-0
        [Fact]
        [Trait("spec", "RSN1")]
        public void RSN1_ChannelsCollectionAccessible()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Assertions
            // ASSERT client.channels IS RestChannels: guaranteed at compile time (Channels is declared as HttpChannels)
            client.Channels.Should().NotBeNull();
        }

        // UTS: rest/unit/RSN2/check-channel-exists-0
        [Fact]
        [Trait("spec", "RSN2")]
        public void RSN2_CheckChannelExists()
        {
            // Setup
            var channelName = "test-RSN2-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Before creating any channel
            var existsBefore = client.Channels.Exists(channelName);

            // Create the channel
            var channel = client.Channels.Get(channelName);

            // After creating the channel
            var existsAfter = client.Channels.Exists(channelName);

            // Check for non-existent channel
            var otherChannelName = "test-RSN2-other-" + Guid.NewGuid().ToString("N");
            var existsOther = client.Channels.Exists(otherChannelName);

            // Assertions
            existsBefore.Should().BeFalse();
            existsAfter.Should().BeTrue();
            existsOther.Should().BeFalse();
        }

        // UTS: rest/unit/RSN2/iterate-channels-1
        [Fact]
        [Trait("spec", "RSN2")]
        public void RSN2_IterateChannels()
        {
            // Setup
            var channelNameA = "test-RSN2-a-" + Guid.NewGuid().ToString("N");
            var channelNameB = "test-RSN2-b-" + Guid.NewGuid().ToString("N");
            var channelNameC = "test-RSN2-c-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Create several channels
            client.Channels.Get(channelNameA);
            client.Channels.Get(channelNameB);
            client.Channels.Get(channelNameC);

            // Iterate channels
            var channelNames = client.Channels.Select(ch => ch.Name).ToList();

            // Assertions
            channelNames.Should().Contain(channelNameA);
            channelNames.Should().Contain(channelNameB);
            channelNames.Should().Contain(channelNameC);
            channelNames.Should().HaveCount(3);
        }

        // UTS: rest/unit/RSN3a/get-creates-new-channel-0
        [Fact]
        [Trait("spec", "RSN3a")]
        public void RSN3a_GetCreatesNewChannel()
        {
            // Setup
            var channelName = "test-RSN3a-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var channel = client.Channels.Get(channelName);

            // Assertions
            // ASSERT channel IS RestChannel: guaranteed at compile time (Get returns IHttpChannel)
            channel.Should().NotBeNull();
            channel.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
        }

        // UTS: rest/unit/RSN3a/get-returns-existing-channel-1
        [Fact]
        [Trait("spec", "RSN3a")]
        public void RSN3a_GetReturnsExistingChannel()
        {
            // Setup
            var channelName = "test-RSN3a-existing-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var channel1 = client.Channels.Get(channelName);
            var channel2 = client.Channels.Get(channelName);

            // Assertions
            channel1.Should().BeSameAs(channel2); // Same object reference
            channel1.Name.Should().Be(channelName);
        }

        // UTS: rest/unit/RSN3a/subscript-creates-or-returns-2
        [Fact]
        [Trait("spec", "RSN3a")]
        public void RSN3a_SubscriptCreatesOrReturns()
        {
            // Setup
            var channelName = "test-RSN3a-subscript-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var channel1 = client.Channels[channelName];
            var channel2 = client.Channels.Get(channelName);
            var channel3 = client.Channels[channelName];

            // Assertions
            channel1.Should().BeSameAs(channel2);
            channel2.Should().BeSameAs(channel3);
            channel1.Name.Should().Be(channelName);
        }

        // UTS: rest/unit/RSN4a/release-removes-channel-0
        [Fact]
        [Trait("spec", "RSN4a")]
        public void RSN4a_ReleaseRemovesChannel()
        {
            // Setup
            var channelName = "test-RSN4a-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            client.Channels.Get(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();

            client.Channels.Release(channelName);

            // Assertions
            client.Channels.Exists(channelName).Should().BeFalse();
        }

        // UTS: rest/unit/RSN4b/release-nonexistent-noop-0
        [Fact]
        [Trait("spec", "RSN4b")]
        public void RSN4b_ReleaseNonexistentNoop()
        {
            // Setup
            var channelName = "test-RSN4b-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Release a channel that was never created — should not throw
            client.Channels.Release(channelName);

            // Assertions
            client.Channels.Exists(channelName).Should().BeFalse();
        }

        // UTS: rest/unit/RSN3a/get-after-release-new-instance-3
        [Fact]
        [Trait("spec", "RSN3a")]
        public void RSN3a_GetAfterReleaseNewInstance()
        {
            // Setup
            var channelName = "test-RSN3a-release-" + Guid.NewGuid().ToString("N");

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var channel1 = client.Channels.Get(channelName);

            client.Channels.Release(channelName);

            var channel2 = client.Channels.Get(channelName);

            // Assertions
            channel1.Should().NotBeSameAs(channel2); // Different object instances
            channel2.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
        }
    }
}
