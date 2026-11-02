// RealtimeChannel State and Events Tests (RTL2, RTL2a, RTL2b, RTL2d, RTL2g, RTL2i, TH1, TH2, TH3, TH5, TH6)
// Derived from uts/realtime/unit/channels/channel_state_events.md
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
    public class ChannelStateEventsTests : UtsUnitTestBase
    {
        public ChannelStateEventsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's recurring mock: CONNECTED on connect, and the given reply to every ATTACH
        private MockWebSocket AttachMock(string channelName, Func<ProtocolMessage, JObject> onAttach)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(onAttach(msg));
                    }
                });
            InstallMock(mockWs);
            return mockWs;
        }

        // UTS: realtime/unit/RTL2b/channel-state-attribute-0
        [UtsFact]
        [Trait("spec", "RTL2b")]
        public async Task RTL2b_ChannelStateAttribute()
        {
            // Setup
            var channelName = "test-RTL2b-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Assertions
            // ASSERT channel.state IS ChannelState -- compile-time guarantee: State is declared as the ChannelState enum
            channel.State.Should().Be(ChannelState.Initialized);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2b/initial-state-initialized-1
        [UtsFact]
        [Trait("spec", "RTL2b")]
        public async Task RTL2b_InitialStateInitialized()
        {
            // Setup
            var channelName = "test-RTL2b-init-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            var channel = client.Channels.Get(channelName);

            // Assertions
            channel.State.Should().Be(ChannelState.Initialized);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2a/state-change-events-emitted-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL2a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL2a")]
        public async Task RTL2a_StateChangeEventsEmitted()
        {
            // Setup
            var channelName = "test-RTL2a-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            var stateChanges = UtsRecord.ChannelStateChanges(channel);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Trigger attach - should emit attaching then attached
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
            };

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            // Should have emitted attaching and attached state changes
            var changes = stateChanges.Snapshot();
            changes.Count.Should().BeGreaterThanOrEqualTo(2);
            changes[0].Current.Should().Be(ChannelState.Attaching);
            changes[0].Previous.Should().Be(ChannelState.Initialized);
            changes[1].Current.Should().Be(ChannelState.Attached);
            changes[1].Previous.Should().Be(ChannelState.Attaching);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2d/state-change-object-structure-0
        [UtsFact]
        [Trait("spec", "RTL2d")]
        public async Task RTL2d_StateChangeObjectStructure()
        {
            // Setup
            var channelName = "test-RTL2d-" + Guid.NewGuid().ToString("N");

            AttachMock(channelName, msg => ProtocolMessages.AttachedMessage(channelName));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            ChannelStateChange capturedChange = null;
            channel.On(change =>
            {
                if (change.Current == ChannelState.Attaching)
                {
                    capturedChange = change;
                }
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            capturedChange.Should().NotBeNull();
            capturedChange.Should().BeAssignableTo<ChannelStateChange>();
            capturedChange.Current.Should().Be(ChannelState.Attaching);
            capturedChange.Previous.Should().Be(ChannelState.Initialized);
            capturedChange.Event.Should().Be(ChannelEvent.Attaching);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2d/state-change-error-reason-1
        [UtsFact]
        [Trait("spec", "RTL2d")]
        public async Task RTL2d_StateChangeErrorReason()
        {
            // Setup
            var channelName = "test-RTL2d-error-" + Guid.NewGuid().ToString("N");

            // Server rejects attachment with error
            AttachMock(channelName, msg => ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Channel denied", 401));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            ChannelStateChange capturedChange = null;
            channel.On(ChannelEvent.Failed, change =>
            {
                capturedChange = change;
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();

            // NOTE: the attach result can resolve before the FAILED event reaches the listener, so the delivery is drained first
            await ProcessPendingEvents();

            // Assertions
            capturedChange.Should().NotBeNull();
            capturedChange.Current.Should().Be(ChannelState.Failed);
            capturedChange.Error.Should().NotBeNull();
            capturedChange.Error.Code.Should().Be(40160);
            capturedChange.Error.Message.Should().Be("Channel denied");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2/filtered-event-subscription-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL2")]
        public async Task RTL2_FilteredEventSubscription()
        {
            // Setup
            var channelName = "test-RTL2-filtered-" + Guid.NewGuid().ToString("N");

            AttachMock(channelName, msg => ProtocolMessages.AttachedMessage(channelName));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            var attachedEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Attached, change => attachedEvents.Add(change));

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            // Should only receive attached event, not attaching
            attachedEvents.Count.Should().Be(1);
            attachedEvents[0].Current.Should().Be(ChannelState.Attached);
            attachedEvents[0].Event.Should().Be(ChannelEvent.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2g/update-event-condition-change-0
        [UtsFact]
        [Trait("spec", "RTL2g")]
        public async Task RTL2g_UpdateEventConditionChange()
        {
            // Setup
            var channelName = "test-RTL2g-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachMock(channelName, msg => ProtocolMessages.AttachedMessage(channelName));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            var updateEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Server sends another ATTACHED message without RESUMED flag
            // (e.g., loss of message continuity after transport resume)
            // Per RTL12, this should trigger UPDATE because resumed=false
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            // Wait for the event to be processed
            await ProcessPendingEvents();

            // Assertions
            // State unchanged
            channel.State.Should().Be(ChannelState.Attached);
            updateEvents.Count.Should().Be(1);
            updateEvents[0].Event.Should().Be(ChannelEvent.Update);
            updateEvents[0].Current.Should().Be(ChannelState.Attached);
            updateEvents[0].Previous.Should().Be(ChannelState.Attached);
            updateEvents[0].Resumed.Should().BeFalse();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL2g/no-duplicate-state-events-1
        [UtsFact]
        [Trait("spec", "RTL2g")]
        public async Task RTL2g_NoDuplicateStateEvents()
        {
            // Setup
            var channelName = "test-RTL2g-nodup-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachMock(channelName, msg => ProtocolMessages.AttachedMessage(channelName));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            var allEvents = UtsRecord.ChannelStateChanges(channel);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var initialCount = allEvents.Count;

            // Server sends another ATTACHED message (no RESUMED flag)
            // Per RTL12, this triggers UPDATE (not a duplicate state event)
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            await ProcessPendingEvents();

            // Assertions
            // Should have received UPDATE event, not another ATTACHED state event
            // Count all events where current == attached AND event == attached (state event)
            initialCount.Should().BeGreaterThan(0);
            var attachedStateEvents = allEvents.Snapshot().Where(e => e.Current == ChannelState.Attached && e.Event == ChannelEvent.Attached).ToList();

            // Only the original attach
            attachedStateEvents.Should().HaveCount(1);
            await CloseClient(client);
        }

        // UTS-NA: realtime/unit/RTL2i/has-backlog-flag-true-0 — not applicable: hasBacklog is optional in the spec (TH6 "may contain", RTL2i "may optionally expose") and this SDK does not implement it
            // channel_name = "test-RTL2i-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name,
            //         flags: HAS_BACKLOG
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name)
            //
            // captured_change = null
            // channel.on(ChannelEvent.attached).listen((change) => {
            //   captured_change = change
            // })
            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            // ASSERT captured_change IS NOT null
            // ASSERT captured_change.hasBacklog == true
            // CLOSE_CLIENT(client)

        // UTS-NA: realtime/unit/RTL2i/has-backlog-flag-false-1 — not applicable: hasBacklog is optional in the spec (TH6 "may contain", RTL2i "may optionally expose") and this SDK does not implement it
            // channel_name = "test-RTL2i-false-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(CONNECTED_MESSAGE),
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name
            //         # No HAS_BACKLOG flag
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // channel = client.channels.get(channel_name)
            //
            // captured_change = null
            // channel.on(ChannelEvent.attached).listen((change) => {
            //   captured_change = change
            // })
            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            // ASSERT captured_change IS NOT null
            // ASSERT captured_change.hasBacklog == false OR captured_change.hasBacklog IS null
            // CLOSE_CLIENT(client)

        // UTS: realtime/unit/RTL2d/resumed-flag-propagated-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTL2d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL2d")]
        public async Task RTL2d_ResumedFlagPropagated()
        {
            // Setup
            var channelName = "test-RTL2d-resumed-" + Guid.NewGuid().ToString("N");

            AttachMock(
                channelName,
                msg => ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = (int)ProtocolMessage.Flag.Resumed }));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            ChannelStateChange capturedChange = null;
            channel.On(ChannelEvent.Attached, change =>
            {
                capturedChange = change;
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            capturedChange.Should().NotBeNull();
            capturedChange.Resumed.Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL24/error-reason-populated-0
        [UtsFact]
        [Trait("spec", "RTL24")]
        public async Task RTL24_ErrorReasonPopulated()
        {
            // Setup
            var channelName = "test-errorReason-" + Guid.NewGuid().ToString("N");

            AttachMock(channelName, msg => ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not authorized", 401));

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();

            // Assertions
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            channel.ErrorReason.Message.Should().Be("Not authorized");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c/error-reason-cleared-attach-0
        [UtsFact]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_ErrorReasonClearedAttach()
        {
            // Setup
            var channelName = "test-errorReason-clear-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            AttachMock(
                channelName,
                msg =>
                {
                    if (Interlocked.Increment(ref attachCount) == 1)
                    {
                        // First attach fails
                        return ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Denied", 401);
                    }

                    // Second attach succeeds
                    return ProtocolMessages.AttachedMessage(channelName);
                });

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // First attach fails
            var firstAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            firstAttach.IsFailure.Should().BeTrue();
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();

            // Second attach succeeds
            var secondAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            secondAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", secondAttach.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull();
            await CloseClient(client);
        }
    }
}
