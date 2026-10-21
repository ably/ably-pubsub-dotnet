using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Ably.PubSub.Realtime;
using Ably.PubSub.Types;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    public class ChannelsSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTS3")]
        [Trait("spec", "RTS3a")]
        public async Task ShouldGetAChannelByName()
        {
            // Act
            var channel = await GetTestChannel();

            // Assert
            channel.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RTS3a")]
        public async Task ShouldReturnExistingChannel()
        {
            var client = await GetConnectedClient();

            // Arrange
            var channel = client.Channels.Get("test");

            // Act
            var channel2 = client.Channels.Get("test");

            // Assert
            channel.Should().BeSameAs(channel2);
        }

        [Fact]
        [Trait("spec", "RTS3b")]
        public async Task ShouldCreateChannelWithOptions()
        {
            // Arrange
            var client = await GetConnectedClient();
            var options = new ChannelOptions();

            // Act
            var channel = client.Channels.Get("test", options);

            // Assert
            Assert.Same(options, channel.Options);
        }

        [Fact]
        [Trait("spec", "RTS3c")]
        public async Task WithExistingChannelAndOptions_ShouldGetExistingChannelAndUpdateOptions()
        {
            // Arrange
            var client = await GetConnectedClient();
            ChannelOptions options = new ChannelOptions();
            _ = client.Channels.Get("test");

            // Act
            var existing = client.Channels.Get("test", options);

            // Assert
            existing.Should().NotBeNull();
            Assert.Same(options, existing.Options);
        }

        // UTS: realtime/unit/RTS4c/release-nonexistent-noop-0
        [Fact]
        [Trait("spec", "RTS4c")]
        public void Release_WhenChannelDoesNotExist_ShouldReturnWithoutError()
        {
            var client = GetClientWithFakeTransport(options => options.AutoConnect = false);

            client.Channels.Release("nonexistent").Should().BeFalse();

            client.Channels.Exists("nonexistent").Should().BeFalse();
        }

        // UTS: realtime/unit/RTS4d/release-removes-channel-0
        [Fact]
        [Trait("spec", "RTS4d")]
        public void Release_WhenChannelInitialized_ShouldRemoveChannel()
        {
            var client = GetClientWithFakeTransport(options => options.AutoConnect = false);
            var channel = client.Channels.Get(TestChannelName);
            channel.State.Should().Be(ChannelState.Initialized);

            client.Channels.Release(TestChannelName).Should().BeTrue();

            client.Channels.Exists(TestChannelName).Should().BeFalse();
        }

        // UTS: realtime/unit/RTS4d/release-after-detach-1
        [Fact]
        [Trait("spec", "RTS4d")]
        public async Task Release_WhenChannelDetached_ShouldRemoveChannel()
        {
            var (client, channel) = await GetClientAndChannel();
            await AttachChannel(client, channel);
            channel.Detach();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached, TestChannelName));
            await channel.WaitForState(ChannelState.Detached);

            client.Channels.Release(TestChannelName).Should().BeTrue();

            client.Channels.Exists(TestChannelName).Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "RTS4d")]
        public async Task Release_WhenChannelFailed_ShouldRemoveChannel()
        {
            var (client, channel) = await GetClientAndChannel();
            channel.Attach();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error, TestChannelName));
            await channel.WaitForState(ChannelState.Failed);

            client.Channels.Release(TestChannelName).Should().BeTrue();

            client.Channels.Exists(TestChannelName).Should().BeFalse();
        }

        // UTS: realtime/unit/RTS4e/release-attached-fails-0
        [Fact]
        [Trait("spec", "RTS4e")]
        public async Task Release_WhenChannelAttached_ShouldThrowAndLeaveChannelAttached()
        {
            var (client, channel) = await GetClientAndChannel();
            await AttachChannel(client, channel);
            var sentMessageCount = LastCreatedTransport.SentMessages.Count;

            var ex = Assert.Throws<AblyException>(() => client.Channels.Release(TestChannelName));

            ex.ErrorInfo.Code.Should().Be(ErrorCodes.ChannelReleaseInvalidState);
            ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            channel.State.Should().Be(ChannelState.Attached);
            client.Channels.Get(TestChannelName).Should().BeSameAs(channel);
            LastCreatedTransport.SentMessages.Should().HaveCount(sentMessageCount);
        }

        [Fact]
        [Trait("spec", "RTS4d")]
        public async Task ReleaseAll_WhenAllChannelsReleasable_ShouldRemoveThem()
        {
            var (client, failedChannel) = await GetClientAndChannel();
            failedChannel.Attach();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error, TestChannelName));
            await failedChannel.WaitForState(ChannelState.Failed);
            client.Channels.Get("initialized");

            client.Channels.ReleaseAll();

            client.Channels.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTS4e")]
        public async Task ReleaseAll_WhenAnyChannelAttached_ShouldThrowAndReleaseNothing()
        {
            var (client, attachedChannel) = await GetClientAndChannel();
            await AttachChannel(client, attachedChannel);
            var initializedChannel = client.Channels.Get("initialized");

            var ex = Assert.Throws<AblyException>(() => client.Channels.ReleaseAll());

            ex.ErrorInfo.Code.Should().Be(ErrorCodes.ChannelReleaseInvalidState);
            ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            client.Channels.Should().BeEquivalentTo(new[] { attachedChannel, initializedChannel });
        }

        private static async Task AttachChannel(PubSubRealtimeClient client, IRealtimeChannel channel)
        {
            channel.Attach();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached, channel.Name));
            await channel.WaitForState(ChannelState.Attached);
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        [Trait("spec", "RTS1")]
        public async Task AllowsEnumeration()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();

            // Act
            IEnumerator enumerator = (client.Channels as IEnumerable).GetEnumerator();
            enumerator.MoveNext();

            // Assert
            Assert.Same(channel, enumerator.Current);
            enumerator.MoveNext().Should().BeFalse();
        }

        [Fact]
        public async Task AllowEnumerationAndReturnSameOrder()
        {
            // Arrange
            var client = GetClientWithFakeTransport();

            var channel1 = client.Channels.Get("test");
            var channel2 = client.Channels.Get("test1");
            var channel4 = client.Channels.Get("test3");
            var channel5 = client.Channels.Get("test4");
            var channel6 = client.Channels.Get("test5");
            var channel7 = client.Channels.Get("test7");

            client.Channels.Should().HaveCount(6);
            client.Channels.Should().BeEquivalentTo(new[] { channel1, channel2, channel4, channel5, channel6, channel7 });
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task AllowsGenericEnumeration()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();

            // Act
            using var enumerator = ((IEnumerable<IRealtimeChannel>)client.Channels).GetEnumerator();
            enumerator.MoveNext();

            // Assert
            Assert.Same(channel, enumerator.Current);
            enumerator.MoveNext().Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "RTS3c")]
        public async Task WithExistingChannel_Get_WithNewChannelOptions_WillUpdateChannelOptions()
        {
            var client = GetClientWithFakeTransport(options => options.AutoConnect = false);

            var channelOptions1 = new ChannelOptions(true);
            var channelOptions2 = new ChannelOptions();
            var channel = client.Channels.Get("Test", channelOptions1);
            var channel2 = client.Channels.Get("Test", channelOptions2);

            channel.Should().BeSameAs(channel2);
            channel2.Options.Should().BeSameAs(channelOptions2);
        }

        [Fact]
        [Trait("spec", "RTS3c1")]
        public async Task WithExistingChannel_Get_WithNewChannelOptionsButMatchingModesAndParams_WillUpdateChannelOptions()
        {
            var client = await GetConnectedClient();

            var channelOptions1 = new ChannelOptions(true)
            {
                Modes = new ChannelModes(ChannelMode.Presence, ChannelMode.Publish),
                Params = { { "test", "best" }, { "best", "test" } },
            };
            var channelOptions2 = new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Publish, ChannelMode.Presence),
                Params = { { "best", "test" }, { "test", "best" }, },
            };
            var channel = client.Channels.Get("Test", channelOptions1);
            // Make the channel attaching
            channel.Attach();
            await channel.WaitForState(ChannelState.Attaching);
            var channel2 = client.Channels.Get("Test", channelOptions2);

            channel.Should().BeSameAs(channel2);
            channel2.Options.Should().BeSameAs(channelOptions2);
        }

        [Fact]
        [Trait("spec", "RTS3c")]
        public async Task WithExistingChannel_Get_WithNewChannelOptionsButDifferentModesAndParams_WillUpdateChannelOptions()
        {
            var client = await GetConnectedClient();

            var channelOptions1 = new ChannelOptions(true)
            {
                Modes = new ChannelModes(ChannelMode.Presence, ChannelMode.Publish),
                Params = { { "test", "best" }, },
            };
            var channelOptions2 = new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Presence, ChannelMode.Publish),
            };
            var channel = client.Channels.Get("Test", channelOptions1);
            // Make the channel attaching
            channel.Attach();
            await channel.WaitForState(ChannelState.Attaching);
            var ex = Assert.Throws<AblyException>(() => client.Channels.Get("Test", channelOptions2));

            ex.ErrorInfo.Code.Should().Be(ErrorCodes.BadRequest);
            ex.Message.Should().Contain("SetOptions");
        }

        public ChannelsSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        [Trait("issue", "167")]
        public async Task PublishShouldNotAlterChannelOptions()
        {
            var key = Convert.FromBase64String("dDGE8dYl8M9+uyUTIv0+ncs1hEa++HiNDu75Dyj4kmw=");
            var cipherParams = new CipherParams(key);
            var options = new ChannelOptions(cipherParams); // enable encryption
            var client = await GetConnectedClient();
            var channel = client.Channels.Get("test", options);

            var channel2 = client.Channels.Get("test");

            channel.Publish(new Message(null, "This is a test", Guid.NewGuid().ToString()));

            await client.ProcessCommands();

            Assert.Equal(options.ToJson(), channel2.Options.ToJson());
            options.CipherParams.Equals(cipherParams).Should().BeTrue();
        }
    }
}
