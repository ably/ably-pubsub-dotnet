using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using IO.Ably.Realtime;
using IO.Ably.Types;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
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

        [Fact]
        [Trait("spec", "RTS4")]
        [Trait("spec", "RTS4a")]
        public async Task Release_ShouldDetachChannel()
        {
            // Arrange
            var client = await GetConnectedClient();
            var channel = client.Channels.Get("test");
            channel.Attach();

            // Act
            client.Channels.Release("test");

            // Assert
            channel.State.Should().Be(ChannelState.Detaching);
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task Release_ShouldNotRemoveChannelBeforeDetached()
        {
            // Arrange
            var client = await GetConnectedClient();
            var channel = client.Channels.Get("test");
            channel.Attach();

            // Act
            client.Channels.Release("test");

            // Assert
            Assert.Same(channel, client.Channels.Single());
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task Release_ShouldRemoveChannelWhenDetached()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();

            channel.Attach();
            client.Channels.Release(TestChannelName);

            // Act
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached, TestChannelName));

            await client.ProcessCommands();

            // Assert
            client.Channels.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task Release_RemovesChannelWhenFailed()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();
            channel.Attach();
            client.Channels.Release("test");

            // Act
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error, "test"));

            await channel.WaitForState(ChannelState.Failed);

            // Assert
            client.Channels.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task ReleaseAll_ShouldDetachChannel()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();

            channel.Attach();

            // Act
            client.Channels.ReleaseAll();

            // Assert
            channel.State.Should().Be(ChannelState.Detaching);
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task ReleaseAll_ShouldNotRemoveChannelBeforeDetached()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();
            channel.Attach();

            // Act
            client.Channels.ReleaseAll();

            // Assert
            Assert.Same(channel, client.Channels.Single());
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task ReleaseAll_ShouldRemoveChannelWhenDetached()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();
            channel.Attach();
            client.Channels.ReleaseAll();

            // Act
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached, TestChannelName));

            await new ChannelAwaiter(channel, ChannelState.Detached).WaitAsync();

            // Assert
            client.Channels.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RTS4a")]
        public async Task ReleaseAll_ShouldRemoveChannelWhenFailed()
        {
            // Arrange
            var (client, channel) = await GetClientAndChannel();

            channel.Attach();
            client.Channels.ReleaseAll();

            // Act
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error, "test"));

            await client.ProcessCommands();

            // Assert
            client.Channels.Should().BeEmpty();
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

        [Theory]
        [InlineData(ChannelState.Initialized, false)]
        [InlineData(ChannelState.Attached, true)]
        [InlineData(ChannelState.Detaching, false)]
        [InlineData(ChannelState.Failed, false)]
        [Trait("spec", "RTL4j1")]
        public async Task WhenChannelMovesToState_AttachResumeShouldHaveCorrectValue(ChannelState state, bool expectedAttachResumeValue)
        {
            var client = GetRealtimeClient();
            var channel = client.Channels.Get("Test") as RealtimeChannel;
            channel.SetChannelState(state);
            channel.AttachResume.Should().Be(expectedAttachResumeValue);
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
