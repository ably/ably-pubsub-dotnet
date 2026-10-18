// RealtimeChannels Collection Tests (RTS1, RTS2, RTS3a, RTS4c, RTS4d, RTS4e)
// Derived from uts/realtime/unit/channels/channels_collection.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key via the spec's own key (no stand-ins); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelsCollectionTests : UtsUnitTestBase
    {
        public ChannelsCollectionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTS1/channels-collection-accessible-0
        [UtsFact]
        [Trait("spec", "RTS1")]
        public async Task RTS1_ChannelsCollectionAccessible()
        {
            // Setup
            // NOTE: the spec sets no autoConnect, so the client auto-connects; the strict default mock refuses the WebSocket (no network)
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            var channels = client.Channels;

            // Assertions
            channels.Should().BeAssignableTo<RealtimeChannels>();
            channels.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS2/channel-exists-check-0
        [UtsFact]
        [Trait("spec", "RTS2")]
        public async Task RTS2_ChannelExistsCheck()
        {
            // Setup
            var channelName = "test-RTS2-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Before creating any channel
            var existsBefore = client.Channels.Exists(channelName);

            // Create the channel
            var channel = client.Channels.Get(channelName);

            // After creating the channel
            var existsAfter = client.Channels.Exists(channelName);

            // Check for non-existent channel
            var otherChannelName = "test-RTS2-other-" + Guid.NewGuid().ToString("N");
            var existsOther = client.Channels.Exists(otherChannelName);

            // Assertions
            existsBefore.Should().BeFalse();
            existsAfter.Should().BeTrue();
            existsOther.Should().BeFalse();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS2/iterate-channels-1
        [UtsFact]
        [Trait("spec", "RTS2")]
        public async Task RTS2_IterateChannels()
        {
            // Setup
            var channelNameA = "test-RTS2-a-" + Guid.NewGuid().ToString("N");
            var channelNameB = "test-RTS2-b-" + Guid.NewGuid().ToString("N");
            var channelNameC = "test-RTS2-c-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Create several channels
            client.Channels.Get(channelNameA);
            client.Channels.Get(channelNameB);
            client.Channels.Get(channelNameC);

            // Get all channel names
            // NOTE: the collection is enumerable (IEnumerable<IRealtimeChannel>); the names are the channels' Name values
            var names = client.Channels.Select(c => c.Name).ToList();

            // Assertions
            names.Should().Contain(channelNameA);
            names.Should().Contain(channelNameB);
            names.Should().Contain(channelNameC);
            names.Should().HaveCount(3);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3a/get-creates-new-channel-0
        [UtsFact]
        [Trait("spec", "RTS3a")]
        public async Task RTS3a_GetCreatesNewChannel()
        {
            // Setup
            var channelName = "test-RTS3a-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Get a channel that doesn't exist yet
            var channel = client.Channels.Get(channelName);

            // Assertions
            channel.Should().BeAssignableTo<IRealtimeChannel>();
            channel.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3a/get-returns-existing-channel-1
        [UtsFact]
        [Trait("spec", "RTS3a")]
        public async Task RTS3a_GetReturnsExistingChannel()
        {
            // Setup
            var channelName = "test-RTS3a-existing-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Get a channel
            var channel1 = client.Channels.Get(channelName);

            // Get the same channel again
            var channel2 = client.Channels.Get(channelName);

            // Assertions
            // Same object reference
            channel1.Should().BeSameAs(channel2);
            channel1.Name.Should().Be(channelName);
            channel2.Name.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3a/subscript-operator-channel-2
        [UtsFact]
        [Trait("spec", "RTS3a")]
        public async Task RTS3a_SubscriptOperatorChannel()
        {
            // Setup
            var channelName = "test-RTS3a-subscript-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Use subscript to get channel
            var channel1 = client.Channels[channelName];

            // Use get() to get same channel
            var channel2 = client.Channels.Get(channelName);

            // Use subscript again
            var channel3 = client.Channels[channelName];

            // Assertions
            channel1.Should().BeSameAs(channel2);
            channel2.Should().BeSameAs(channel3);
            channel1.Name.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS4c/release-nonexistent-noop-0
        [UtsFact]
        [Trait("spec", "RTS4c")]
        public async Task RTS4c_ReleaseNonexistentNoop()
        {
            // Setup
            var channelName = "test-RTS4c-nonexistent-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Release a channel that was never created
            client.Channels.Release(channelName);

            // Assertions
            // Should complete without throwing
            client.Channels.Exists(channelName).Should().BeFalse();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS4d/release-removes-channel-0
        [UtsFact]
        [Trait("spec", "RTS4d")]
        public async Task RTS4d_ReleaseRemovesChannel()
        {
            // Setup
            var channelName = "test-RTS4d-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            var channel = client.Channels.Get(channelName);
            channel.State.Should().Be(ChannelState.Initialized);
            client.Channels.Exists(channelName).Should().BeTrue();

            client.Channels.Release(channelName);

            // Assertions
            client.Channels.Exists(channelName).Should().BeFalse();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS4d/release-after-detach-1
        [UtsFact]
        [Trait("spec", "RTS4d")]
        public async Task RTS4d_ReleaseAfterDetach()
        {
            // Setup
            var channelName = "test-RTS4d-detached-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);

            client.Channels.Release(channelName);

            // Assertions
            client.Channels.Exists(channelName).Should().BeFalse();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS4e/release-attached-fails-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTS4e", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTS4e")]
        public async Task RTS4e_ReleaseAttachedFails()
        {
            // Setup
            var channelName = "test-RTS4e-attached-" + Guid.NewGuid().ToString("N");

            var capturedDetachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Detach)
                    {
                        capturedDetachMessages.Add(msg);
                    }

                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            Action release = () => client.Channels.Release(channelName);
            var error = release.Should().Throw<AblyException>().Which.ErrorInfo;

            // Assertions
            error.Code.Should().Be(90011);
            ((int?)error.StatusCode).Should().Be(400);
            channel.State.Should().Be(ChannelState.Attached);
            client.Channels.Exists(channelName).Should().BeTrue();
            client.Channels.Get(channelName).Should().BeSameAs(channel);
            capturedDetachMessages.Count.Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3a/get-after-release-new-3
        [UtsFact]
        [Trait("spec", "RTS3a")]
        public async Task RTS3a_GetAfterReleaseNew()
        {
            // Setup
            var channelName = "test-RTS3a-release-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Test Steps
            // Create a channel
            var channel1 = client.Channels.Get(channelName);

            // Release it
            client.Channels.Release(channelName);

            // Get the same channel name again
            var channel2 = client.Channels.Get(channelName);

            // Assertions
            // Different object instances
            channel1.Should().NotBeSameAs(channel2);
            channel2.Name.Should().Be(channelName);
            client.Channels.Exists(channelName).Should().BeTrue();
            await CloseClient(client);
        }
    }
}
