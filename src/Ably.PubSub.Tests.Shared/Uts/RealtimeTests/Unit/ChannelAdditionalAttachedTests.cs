// Additional ATTACHED Message Handling Tests (RTL12)
// Derived from uts/realtime/unit/channels/channel_additional_attached.md
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
    public class ChannelAdditionalAttachedTests : UtsUnitTestBase
    {
        public ChannelAdditionalAttachedTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL12/update-emits-with-error-0
        [UtsFact]
        [Trait("spec", "RTL12")]
        public async Task RTL12_UpdateEmitsWithError()
        {
            // Setup
            var channelName = "test-RTL12-update-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
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

            var updateEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            // Server sends additional ATTACHED without RESUMED flag, with an error
            // (e.g., loss of message continuity after transport resume)
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["error"] = ProtocolMessages.ErrorObject(50000, "generic serverside failure", 500) }));

            // NOTE: AWAIT Future.delayed(Duration.zero) is a delivery wait; rendered as a drain of pending events
            await ProcessPendingEvents();

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Count.Should().Be(1);
            updateEvents[0].Event.Should().Be(ChannelEvent.Update);
            updateEvents[0].Current.Should().Be(ChannelState.Attached);
            updateEvents[0].Previous.Should().Be(ChannelState.Attached);
            updateEvents[0].Resumed.Should().BeFalse();
            updateEvents[0].Error.Code.Should().Be(50000);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL12/resumed-no-update-1
        [UtsFact]
        [Trait("spec", "RTL12")]
        public async Task RTL12_ResumedNoUpdate()
        {
            // Setup
            var channelName = "test-RTL12-no-update-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
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

            var updateEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            // Server sends additional ATTACHED WITH RESUMED flag
            // This indicates successful resume with no loss of continuity
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken> { ["flags"] = (int)ProtocolMessage.Flag.Resumed }));

            // NOTE: AWAIT Future.delayed(Duration.zero) is a delivery wait; rendered as a drain of pending events
            await ProcessPendingEvents();

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Count.Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL12/no-error-null-reason-2
        [UtsFact]
        [Trait("spec", "RTL12")]
        public async Task RTL12_NoErrorNullReason()
        {
            // Setup
            var channelName = "test-RTL12-no-error-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
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

            var updateEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            // Server sends additional ATTACHED without RESUMED flag and without error
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            // NOTE: AWAIT Future.delayed(Duration.zero) is a delivery wait; rendered as a drain of pending events
            await ProcessPendingEvents();

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Count.Should().Be(1);
            updateEvents[0].Resumed.Should().BeFalse();
            updateEvents[0].Error.Should().BeNull();
            await CloseClient(client);
        }
    }
}
