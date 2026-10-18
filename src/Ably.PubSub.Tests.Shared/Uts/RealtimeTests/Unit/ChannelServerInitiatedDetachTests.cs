// Server-Initiated DETACHED and Channel Retry Tests (RTL13, RTL13a, RTL13b, RTL13c)
// Derived from uts/realtime/unit/channels/channel_server_initiated_detach.md
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
    public class ChannelServerInitiatedDetachTests : UtsUnitTestBase
    {
        public ChannelServerInitiatedDetachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL13a/attached-reattach-triggered-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL13a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL13a")]
        public async Task RTL13a_AttachedReattachTriggered()
        {
            // Setup
            var channelName = "test-RTL13a-attached-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Increment(ref attachCount);
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
            Volatile.Read(ref attachCount).Should().Be(1);

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends unsolicited DETACHED with error
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Server detached channel", 500));

            // Channel should reattach automatically
            // NOTE: AWAIT_STATE attached would return at once (still ATTACHED); the recorded changes are polled for the ATTACHED that follows the ATTACHING
            await UtsWait.PollUntil(
                () => channelStateChanges.Snapshot().Count >= 2,
                description: "channel reattached after the server DETACHED");

            // Assertions
            // Two ATTACH messages total: initial + reattach
            Volatile.Read(ref attachCount).Should().Be(2);

            // State change sequence: ATTACHING (with error) -> ATTACHED
            var changes = channelStateChanges.Snapshot();
            changes.Count.Should().BeGreaterThanOrEqualTo(2);
            changes[0].Current.Should().Be(ChannelState.Attaching);
            changes[0].Previous.Should().Be(ChannelState.Attached);
            changes[0].Error.Should().NotBeNull();
            changes[0].Error.Code.Should().Be(90198);
            changes[1].Current.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL13a/suspended-reattach-triggered-1
        [UtsFact]
        [Trait("spec", "RTL13a")]
        public async Task RTL13a_SuspendedReattachTriggered()
        {
            // Setup
            var channelName = "test-RTL13a-suspended-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        var count = Interlocked.Increment(ref attachCount);
                        if (count == 1)
                        {
                            // First attach succeeds
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                        else if (count == 2)
                        {
                            // Second attach (after timeout) - don't respond, causing timeout -> SUSPENDED
                        }
                        else
                        {
                            // Third attach (after server DETACHED) - succeed
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                    }
                });
            InstallMock(mockWs);

            // NOTE: enable_fake_timers() moved before Realtime(...): a timer already running stays on the real clock
            EnableFakeTimers();
            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
                ChannelRetryTimeout = TimeSpan.FromMilliseconds(60000),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Force channel into SUSPENDED state by triggering a reattach that times out:
            // Send server-initiated DETACHED to trigger RTL13a reattach
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Detach 1", 500));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Let the reattach timeout -> SUSPENDED
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Now send another server-initiated DETACHED while SUSPENDED
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90199, "Detach 2", 500));

            // Channel should immediately attempt to reattach and succeed
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // 3 total ATTACH messages: initial + RTL13a reattach + RTL13a reattach from SUSPENDED
            Volatile.Read(ref attachCount).Should().Be(3);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL13b/failed-reattach-suspended-retry-0
        [UtsFact]
        [Trait("spec", "RTL13b")]
        public async Task RTL13b_FailedReattachSuspendedRetry()
        {
            // Setup
            var channelName = "test-RTL13b-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        var count = Interlocked.Increment(ref attachCount);
                        if (count == 1)
                        {
                            // First attach succeeds
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                        else if (count == 2)
                        {
                            // Reattach after server DETACHED - don't respond (timeout)
                        }
                        else if (count == 3)
                        {
                            // Automatic retry after channelRetryTimeout - succeed
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                    }
                });
            InstallMock(mockWs);

            // NOTE: enable_fake_timers() moved before Realtime(...): a timer already running stays on the real clock
            EnableFakeTimers();
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
            channel.State.Should().Be(ChannelState.Attached);

            // Record state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends unsolicited DETACHED
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Server detached", 500));

            // Channel should be in ATTACHING (RTL13a)
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // NOTE: the SDK enters ATTACHING before its ATTACH reaches the mock, so the queued send is drained before the count is read
            await ProcessPendingEvents();
            Volatile.Read(ref attachCount).Should().Be(2);

            // Let reattach timeout -> SUSPENDED (RTL13b)
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // NOTE: infra-driving: the SDK registers the channel retry timer from a Task.Run (RealtimeChannel.ReattachAfterTimeout) after SUSPENDED is emitted, so a short real yield lets it reach the fake clock before ADVANCE_TIME
            await Task.Delay(100);

            // Wait for channelRetryTimeout to trigger automatic retry and succeed
            await AdvanceTime(250);
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            Volatile.Read(ref attachCount).Should().Be(3);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachCount).Should().Be(3);

            // Verify state sequence: ATTACHING -> SUSPENDED -> ATTACHING -> ATTACHED
            UtsAssert.ContainsInOrder(
                channelStateChanges.Snapshot().Select(c => c.Current).ToList(),
                new[] { ChannelState.Attaching, ChannelState.Suspended, ChannelState.Attaching, ChannelState.Attached });
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL13b/attaching-detached-to-suspended-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL13b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL13b")]
        public async Task RTL13b_AttachingDetachedToSuspended()
        {
            // Setup
            var channelName = "test-RTL13b-attaching-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        var count = Interlocked.Increment(ref attachCount);
                        if (count == 1)
                        {
                            // First attach - don't respond immediately, leave channel in ATTACHING
                        }
                        else if (count == 2)
                        {
                            // Automatic retry from SUSPENDED - succeed
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                    }
                });
            InstallMock(mockWs);

            // NOTE: enable_fake_timers() moved before Realtime(...): a timer already running stays on the real clock
            EnableFakeTimers();
            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500),
                ChannelRetryTimeout = TimeSpan.FromMilliseconds(200),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't await it (mock won't respond)
            _ = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Record state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends DETACHED while channel is still ATTACHING
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Server detached", 500));

            // Channel should go directly to SUSPENDED (RTL13b), not try another reattach
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Only the original attach, no second attempt
            Volatile.Read(ref attachCount).Should().Be(1);

            // Wait for channelRetryTimeout — automatic retry should succeed
            await AdvanceTime(250);
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            Volatile.Read(ref attachCount).Should().Be(2);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // Verify direct transition to SUSPENDED (no intermediate ATTACHING)
            var changes = channelStateChanges.Snapshot();
            changes[0].Current.Should().Be(ChannelState.Suspended);
            changes[0].Previous.Should().Be(ChannelState.Attaching);
            changes[0].Error.Should().NotBeNull();
            changes[0].Error.Code.Should().Be(90198);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL13b/repeated-failure-cycle-2
        [UtsFact]
        [Trait("spec", "RTL13b")]
        public async Task RTL13b_RepeatedFailureCycle()
        {
            // Setup
            var channelName = "test-RTL13b-repeat-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        var count = Interlocked.Increment(ref attachCount);
                        if (count == 1)
                        {
                            // First attach succeeds
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                        else if (count <= 3)
                        {
                            // Reattach attempts 2 and 3 - don't respond (timeout)
                        }
                        else
                        {
                            // Fourth attempt succeeds
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }
                    }
                });
            InstallMock(mockWs);

            // NOTE: enable_fake_timers() moved before Realtime(...): a timer already running stays on the real clock
            EnableFakeTimers();
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

            // Record state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends DETACHED
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Detach", 500));

            // Cycle 1: ATTACHING (reattach) -> timeout -> SUSPENDED -> retry
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // NOTE: infra-driving: the SDK registers the channel retry timer from a Task.Run (RealtimeChannel.ReattachAfterTimeout) after SUSPENDED is emitted, so a short real yield lets it reach the fake clock before ADVANCE_TIME
            await Task.Delay(100);
            await AdvanceTime(250);

            // NOTE: the retry runs on a thread-pool continuation of the fake-clock timer; the recorded states are polled for the re-entry into ATTACHING instead of AWAIT_STATE
            await UtsWait.PollUntil(
                () => channelStateChanges.Snapshot().Count(c => c.Current == ChannelState.Attaching) >= 2,
                description: "channel re-entered ATTACHING after SUSPENDED");

            // NOTE: the SDK enters ATTACHING before its ATTACH reaches the mock, so the queued send is drained before the count is read
            await ProcessPendingEvents();
            Volatile.Read(ref attachCount).Should().Be(3);

            // Cycle 2: ATTACHING (retry) -> timeout -> SUSPENDED -> retry
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // NOTE: infra-driving: the SDK registers the channel retry timer from a Task.Run (RealtimeChannel.ReattachAfterTimeout) after SUSPENDED is emitted, so a short real yield lets it reach the fake clock before ADVANCE_TIME
            await Task.Delay(100);
            await AdvanceTime(250);

            // Fourth attempt succeeds
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            Volatile.Read(ref attachCount).Should().Be(4);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachCount).Should().Be(4);

            // Verify repeated cycling
            UtsAssert.ContainsInOrder(
                channelStateChanges.Snapshot().Select(c => c.Current).ToList(),
                new[]
                {
                    ChannelState.Attaching,
                    ChannelState.Suspended,
                    ChannelState.Attaching,
                    ChannelState.Suspended,
                    ChannelState.Attaching,
                    ChannelState.Attached,
                });
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL13c/retry-cancelled-disconnected-0
        [UtsFact(Skip = "UTS spec error RTL13c: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTL13c")]
        public Task RTL13c_RetryCancelledDisconnected()
        {
            // SPEC ERROR RTL13c: the mock accepts every connection attempt, so after simulate_disconnect() the client reconnects at once (RTN15a, RTN15h3)
            // and RTL3d re-attaches the SUSPENDED channel; "no additional ATTACH" and "still SUSPENDED" contradict the features spec.
            // Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTL13c - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTL13a/detaching-not-server-initiated-2
        [UtsFact]
        [Trait("spec", "RTL13a")]
        public async Task RTL13a_DetachingNotServerInitiated()
        {
            // Setup
            var channelName = "test-RTL13-detaching-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Increment(ref attachCount);
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
            channel.State.Should().Be(ChannelState.Attached);

            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            // Channel should be cleanly DETACHED, not re-attached
            channel.State.Should().Be(ChannelState.Detached);

            // Only one ATTACH message (the initial attach, no reattach)
            Volatile.Read(ref attachCount).Should().Be(1);
            await CloseClient(client);
        }
    }
}
