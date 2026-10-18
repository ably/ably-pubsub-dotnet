// Channel ERROR Protocol Message Tests (RTL14)
// Derived from uts/realtime/unit/channels/channel_error.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key ValidKey is the spec's own key; the mock answers ATTACH with ATTACHED as the spec's onMessageFromClient does; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelErrorTests : UtsUnitTestBase
    {
        public ChannelErrorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL14/attached-to-failed-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL14", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL14")]
        public async Task RTL14_AttachedToFailed()
        {
            // Setup
            var channelName = "test-RTL14-attached-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends channel-scoped ERROR (e.g., permission revoked)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted", 401));
            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            // Channel transitioned to FAILED
            channel.State.Should().Be(ChannelState.Failed);

            // errorReason is set
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int?)channel.ErrorReason.StatusCode).Should().Be(401);
            channel.ErrorReason.Message.Should().Contain("Not permitted");

            // State change event emitted
            channelStateChanges.Should().HaveCount(1);
            channelStateChanges[0].Current.Should().Be(ChannelState.Failed);
            channelStateChanges[0].Previous.Should().Be(ChannelState.Attached);
            channelStateChanges[0].Error.Should().NotBeNull();
            channelStateChanges[0].Error.Code.Should().Be(40160);

            // Connection stays open (channel-scoped ERROR does NOT close connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL14/attaching-to-failed-1
        [UtsFact]
        [Trait("spec", "RTL14")]
        public async Task RTL14_AttachingToFailed()
        {
            // Setup
            var channelName = "test-RTL14-attaching-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Respond with channel ERROR instead of ATTACHED
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(msg.Channel, 40160, "Not permitted", 401));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach should fail
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            // Channel is in FAILED state
            channel.State.Should().Be(ChannelState.Failed);

            // errorReason is set
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);

            // The error from attach() matches the channel error
            error.Code.Should().Be(40160);

            // Connection stays open
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL14/pending-detach-error-2
        [UtsFact]
        [Trait("spec", "RTL14")]
        public async Task RTL14_PendingDetachError()
        {
            // Setup
            var channelName = "test-RTL14-detaching-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    // Respond with ERROR instead of DETACHED
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(msg.Channel, 90198, "Detach failed", 500));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Detach should fail
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsFailure.Should().BeTrue();
            var error = detachResult.Error;

            // Assertions
            // Channel is in FAILED state (not DETACHED)
            channel.State.Should().Be(ChannelState.Failed);

            // errorReason is set
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(90198);

            // The error from detach() matches
            error.Code.Should().Be(90198);

            // Connection stays open
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL14/other-channels-unaffected-3
        [UtsFact]
        [Trait("spec", "RTL14")]
        public async Task RTL14_OtherChannelsUnaffected()
        {
            // Setup
            var channelNameA = "test-RTL14-a-" + Guid.NewGuid().ToString("N");
            var channelNameB = "test-RTL14-b-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channelA = client.Channels.Get(channelNameA);
            var channelB = client.Channels.Get(channelNameB);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachA = await UtsWait.WithTimeout(channelA.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachA.IsSuccess.Should().BeTrue("AWAIT channel_a.attach() succeeds ({0})", attachA.Error?.Message);
            var attachB = await UtsWait.WithTimeout(channelB.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachB.IsSuccess.Should().BeTrue("AWAIT channel_b.attach() succeeds ({0})", attachB.Error?.Message);
            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            // Send ERROR only for channel A
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ChannelErrorMessage(channelNameA, 40160, "Not permitted", 401));
            await UtsWait.AwaitChannelState(channelA, ChannelState.Failed);

            // Assertions
            // Channel A is FAILED
            channelA.State.Should().Be(ChannelState.Failed);
            channelA.ErrorReason.Should().NotBeNull();

            // Channel B is unaffected
            channelB.State.Should().Be(ChannelState.Attached);
            channelB.ErrorReason.Should().BeNull();

            // Connection stays open
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL14/cancels-pending-timers-4
        // DEVIATION: see deviations.md
        [DeviationFact("RTL14", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL14")]
        public async Task RTL14_CancelsPendingTimers()
        {
            // Setup
            var channelName = "test-RTL14-timers-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            // NOTE: enable_fake_timers() is hoisted above the client construction, as the fake clock must be on before Realtime(...)
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    if (Interlocked.Increment(ref attachCount) == 1)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }

                    // Don't respond to subsequent attaches
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
                ChannelRetryTimeout = TimeSpan.FromMilliseconds(200),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            Volatile.Read(ref attachCount).Should().Be(1);

            // Trigger server-initiated DETACHED -> reattach -> timeout -> SUSPENDED
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Detach", 500));
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Channel retry timer is now pending (channelRetryTimeout = 200ms)
            // Send ERROR before the retry fires
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted", 401));
            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            var attachCountAfterError = Volatile.Read(ref attachCount);

            // Advance time well past the channelRetryTimeout
            await AdvanceTime(500);

            // Assertions
            // Channel remains FAILED - no retry was attempted
            channel.State.Should().Be(ChannelState.Failed);
            Volatile.Read(ref attachCount).Should().Be(attachCountAfterError);
            await CloseClient(client);
        }
    }
}
