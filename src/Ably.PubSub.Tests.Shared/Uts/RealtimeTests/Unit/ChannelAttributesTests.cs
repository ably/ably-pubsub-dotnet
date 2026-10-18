// RealtimeChannel Attributes (RTL23, RTL24)
// Derived from uts/realtime/unit/channels/channel_attributes.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelAttributesTests : UtsUnitTestBase
    {
        public ChannelAttributesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL23/name-attribute-0
        [UtsFact]
        [Trait("spec", "RTL23")]
        public async Task RTL23_NameAttribute()
        {
            // Setup
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Assertions
            var channel = client.Channels.Get("my-channel");
            channel.Name.Should().Be("my-channel");

            // Also works with special characters
            var channel2 = client.Channels.Get("namespace:channel-name");
            channel2.Name.Should().Be("namespace:channel-name");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL24/error-reason-channel-error-0
        [UtsFact]
        [Trait("spec", "RTL24")]
        public async Task RTL24_ErrorReasonChannelError()
        {
            // Setup
            var channelName = "test-RTL24-error-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Verify errorReason is initially null
            channel.ErrorReason.Should().BeNull();

            // Send an ERROR ProtocolMessage for this channel
            mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 90001, "Channel error occurred", 500));

            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(90001);
            ((int?)channel.ErrorReason.StatusCode).Should().Be(500);
            channel.ErrorReason.Message.Should().Be("Channel error occurred");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL24/error-reason-attach-failure-1
        [UtsFact]
        [Trait("spec", "RTL24")]
        public async Task RTL24_ErrorReasonAttachFailure()
        {
            // Setup
            var channelName = "test-RTL24-attach-fail-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Reject attach with DETACHED + error
                        mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 40160, "Permission denied", 401));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach should fail
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();

            // Assertions
            // errorReason is set from the DETACHED response error
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int?)channel.ErrorReason.StatusCode).Should().Be(401);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c/error-cleared-on-attach-0
        [UtsFact]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_ErrorClearedOnAttach()
        {
            // Setup
            var channelName = "test-RTL24-clear-attach-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        if (Interlocked.Increment(ref attachCount) == 1)
                        {
                            // First attach: reject
                            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 50000, "Temporary error", 500));
                        }
                        else
                        {
                            // Subsequent attaches: succeed
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
                        }
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // First attach fails — errorReason set
            var firstAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            firstAttach.IsFailure.Should().BeTrue();
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(50000);

            // Second attach succeeds — errorReason cleared
            var secondAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            secondAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", secondAttach.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c/error-cleared-preserved-detach-1
        [UtsFact]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_ErrorClearedPreservedDetach()
        {
            // Setup
            var channelName = "test-RTL24-clear-detach-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
                    }

                    if (msg.Action == ProtocolMessage.MessageAction.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Send ERROR — channel transitions to FAILED, errorReason is set (RTL14)
            mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 90002, "Channel error", 500));

            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(90002);

            // Reattach — errorReason cleared on successful attach
            var reattachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            reattachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", reattachResult.Error?.Message);
            channel.ErrorReason.Should().BeNull();

            // Now detach — errorReason stays null
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            channel.ErrorReason.Should().BeNull();
            await CloseClient(client);
        }
    }
}
