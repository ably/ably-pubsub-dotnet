// RealtimePresence Channel State Tests (RTL9, RTL9a, RTL11, RTL11a, RTP1, RTP5, RTP5a, RTP5b, RTP5f, RTP13)
// Derived from uts/realtime/unit/presence/realtime_presence_channel_state.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key "fake.key:secret" as the spec sets it; no fake timers (the spec enables none); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class RealtimePresenceChannelStateTests : UtsUnitTestBase
    {
        public RealtimePresenceChannelStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The specs' PresenceMessage(action: PRESENT, ...) entries of a SYNC message
        private static JObject PresentEntry(string clientId, string connectionId, string id, long timestamp)
        {
            return ProtocolMessages.PresenceEntry((int)PresenceAction.Present, clientId, connectionId, id, timestamp);
        }

        // UTS: realtime/unit/RTP1/has-presence-triggers-sync-0
        [UtsFact]
        [Trait("spec", "RTP1")]
        public async Task RTP1_HasPresenceTriggersSync()
        {
            // Setup
            var channelName = "test-RTP1-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));

                        // Server follows up with SYNC
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", new JArray { PresentEntry("alice", "c1", "c1:0:0", 100) }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Wait for sync to complete
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");
            channel.Presence.SyncComplete.Should().BeTrue();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP1/no-has-presence-empty-1
        [UtsFact]
        [Trait("spec", "RTP1")]
        public async Task RTP1_NoHasPresenceEmpty()
        {
            // Setup
            var channelName = "test-RTP1-empty-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // No HAS_PRESENCE flag
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            members.Should().HaveCount(0);

            // Immediately in sync
            channel.Presence.SyncComplete.Should().BeTrue();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP1/no-has-presence-clears-existing-2
        [UtsFact]
        [Trait("spec", "RTP1")]
        public async Task RTP1_NoHasPresenceClearsExisting()
        {
            // Setup
            var channelName = "test-RTP19a-" + Guid.NewGuid().ToString("N");

            var connectionCount = 0;
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    var count = Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-" + count));
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        if (Volatile.Read(ref connectionCount) == 1)
                        {
                            // First attach: has presence
                            mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                            mockWs.SendToClient(ProtocolMessages.SyncMessage(
                                channelName,
                                "seq1:",
                                new JArray
                                {
                                    PresentEntry("alice", "c1", "c1:0:0", 100),
                                    PresentEntry("bob", "c2", "c2:0:0", 100),
                                }));
                        }
                        else
                        {
                            // Second attach: no HAS_PRESENCE
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                        }
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Verify members exist after first sync
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();
            members.Should().HaveCount(2);

            // Track LEAVE events
            var leaveEvents = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, leaveEvent => leaveEvents.Add(leaveEvent));

            // Simulate disconnect and reconnect
            // NOTE: DISCONNECTED may be left again at once (instant first retry), so the states are recorded before the stimulus and the record is polled instead of AWAIT_STATE
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Reconnect — this time ATTACHED without HAS_PRESENCE
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED");

            // NOTE: AWAIT_STATE channel attached would return at once (the channel is already ATTACHED); wait for the second ATTACH instead, then drain the delivery of its answer
            await UtsWait.PollUntil(
                () => mockWs.MessagesFromClientOfAction(ProtocolMessages.Attach).Count >= 2,
                description: "the client re-attached after the reconnect");
            await ProcessPendingEvents();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            var membersAfter = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // All members removed
            membersAfter.Should().HaveCount(0);

            // LEAVE events emitted for each member
            leaveEvents.Should().HaveCount(2);
            leaveEvents.Snapshot().Should().Contain(e => e.ClientId == "alice");
            leaveEvents.Snapshot().Should().Contain(e => e.ClientId == "bob");

            // LEAVE events have id=null per RTP19a
            leaveEvents.Snapshot().Should().NotContain(e => !(e.Id == null));

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP5a/detached-clears-presence-maps-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL33b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP5a")]
        public async Task RTP5a_DetachedClearsPresenceMaps()
        {
            // Setup
            var channelName = "test-RTP5a-detached-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", new JArray { PresentEntry("alice", "c1", "c1:0:0", 100) }));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Verify member exists
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();
            members.Should().HaveCount(1);

            // Track events — LEAVE should NOT be emitted on clear
            var leaveEvents = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, leaveEvent => leaveEvents.Add(leaveEvent));

            // Detach the channel
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);

            // Assertions
            // RTP5a: No LEAVE events emitted when clearing on DETACHED
            leaveEvents.Should().HaveCount(0);

            // Presence map is cleared
            var membersAfter = (await UtsWait.WithTimeout(channel.Presence.GetAsync(waitForSync: false), TimeSpan.FromSeconds(10), "GetAsync")).ToList();
            membersAfter.Should().HaveCount(0);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP5a/failed-clears-presence-maps-1
        [UtsFact]
        [Trait("spec", "RTP5a")]
        public async Task RTP5a_FailedClearsPresenceMaps()
        {
            // Setup
            var channelName = "test-RTP5a-failed-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", new JArray { PresentEntry("alice", "c1", "c1:0:0", 100) }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();
            members.Should().HaveCount(1);

            var leaveEvents = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Leave, leaveEvent => leaveEvents.Add(leaveEvent));

            // Server sends channel ERROR to put channel in FAILED state
            mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 90001, "Channel failed"));

            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            // RTP5a: No LEAVE events emitted
            leaveEvents.Should().HaveCount(0);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP5b/attached-sends-queued-presence-0
        [UtsFact]
        [Trait("spec", "RTP5b")]
        public async Task RTP5b_AttachedSendsQueuedPresence()
        {
            // Setup
            var channelName = "test-RTP5b-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // Delay attach response
                    }
                    else if ((int)msg.Action == ProtocolMessages.Presence)
                    {
                        capturedPresence.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach — channel goes to ATTACHING
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Queue presence while channel is ATTACHING
            var enterFuture = channel.Presence.EnterAsync("queued");

            // No presence sent yet
            await ProcessPendingEvents();
            capturedPresence.Should().HaveCount(0);

            // Complete the attach
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            var enterResult = await UtsWait.WithTimeout(enterFuture, TimeSpan.FromSeconds(10), "enterFuture");
            enterResult.IsSuccess.Should().BeTrue("AWAIT enter_future succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            // Queued presence was sent after attach completed
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            capturedPresence[0].Presence[0].Data.Should().Be("queued");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP5f/suspended-maintains-presence-map-0
        [UtsFact(Skip = "UTS spec error RTP5f: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTP5f")]
        public Task RTP5f_SuspendedMaintainsPresenceMap()
        {
            // SPEC ERROR RTP5f: the test simulates a transport disconnect and then awaits the channel entering SUSPENDED, but a disconnect only
            // takes the connection to DISCONNECTED, and features RTL3e says DISCONNECTED has no effect on channel states (RTL3c: a channel goes
            // SUSPENDED when the connection enters SUSPENDED). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTP5f - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTP13/sync-complete-attribute-0
        [UtsFact]
        [Trait("spec", "RTP13")]
        public async Task RTP13_SyncCompleteAttribute()
        {
            // Setup
            var channelName = "test-RTP13-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));

                        // Start multi-message SYNC (cursor is non-empty)
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:cursor1", new JArray { PresentEntry("alice", "c1", "c1:0:0", 100) }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Sync is in progress — not yet complete
            // NOTE: the queued deliveries are drained first so the assertion does not race the ATTACHED and SYNC processing
            await ProcessPendingEvents();
            channel.Presence.SyncComplete.Should().BeFalse();

            // Complete the sync (empty cursor)
            mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", new JArray { PresentEntry("bob", "c2", "c2:0:0", 100) }));
            await ProcessPendingEvents();

            // Assertions
            channel.Presence.SyncComplete.Should().BeTrue();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL9/presence-attribute-0
        [UtsFact]
        [Trait("spec", "RTL9")]
        public async Task RTL9_PresenceAttribute()
        {
            // Setup
            var channelName = "test-RTL9a-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg => { });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var presence = channel.Presence;

            // Assertions
            // ASSERT presence IS RealtimePresence: Presence is a sealed type and the property is declared as Presence, a compile-time guarantee
            presence.Should().NotBeNull();

            // RTL9a - Same presence object returned for same channel
            // identity check — same instance
            channel.Presence.Should().BeSameAs(channel.Presence);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-detached-0
        [UtsFact]
        [Trait("spec", "RTL11")]
        public async Task RTL11_QueuedPresenceFailDetached()
        {
            // Setup
            var channelName = "test-RTL11-detached-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Presence)
                    {
                        capturedPresence.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach then detach to put channel in DETACHED state
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);

            // Attempting presence on a DETACHED channel should error immediately
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("queued-enter"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;

            // Assertions
            // No presence messages were sent
            capturedPresence.Should().HaveCount(0);

            // The enter completed with an error
            error.Should().BeAssignableTo<ErrorInfo>();

            // ASSERT error.code IS NOT null: Code is a value type, a compile-time guarantee

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-suspended-1
        [UtsFact(Skip = "UTS spec error RTL11: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTL11")]
        public Task RTL11_QueuedPresenceFailSuspended()
        {
            // SPEC ERROR RTL11: the test simulates a transport disconnect and then awaits the channel entering SUSPENDED, but a disconnect only
            // takes the connection to DISCONNECTED, and features RTL3e says DISCONNECTED has no effect on channel states (RTL3c: a channel goes
            // SUSPENDED when the connection enters SUSPENDED). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTL11 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTL11/queued-presence-fail-failed-2
        [UtsFact]
        [Trait("spec", "RTL11")]
        public async Task RTL11_QueuedPresenceFailFailed()
        {
            // Setup
            var channelName = "test-RTL11-failed-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // Do NOT respond — leave channel in ATTACHING
                    }
                    else if ((int)msg.Action == ProtocolMessages.Presence)
                    {
                        capturedPresence.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Queue presence
            var enterFuture = channel.Presence.EnterAsync("queued-enter");

            await ProcessPendingEvents();
            capturedPresence.Should().HaveCount(0);

            // Server sends ERROR for this channel — channel goes FAILED
            mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 90001, "Channel failed"));

            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            // No presence messages were sent
            capturedPresence.Should().HaveCount(0);

            // Queued future completed with an error
            var enterResult = await UtsWait.WithTimeout(enterFuture, TimeSpan.FromSeconds(10), "enterFuture");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;
            error.Should().BeAssignableTo<ErrorInfo>();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL11a/ack-nack-unaffected-by-state-0
        [UtsFact]
        [Trait("spec", "RTL11a")]
        public async Task RTL11a_AckNackUnaffectedByState()
        {
            // Setup
            var channelName = "test-RTL11a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Presence)
                    {
                        capturedPresence.Add(msg);

                        // Do NOT send ACK yet — hold it
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Send presence — it goes to the server, but no ACK yet
            var enterFuture = channel.Presence.EnterAsync("awaiting-ack");
            await ProcessPendingEvents();
            capturedPresence.Should().HaveCount(1);

            // Detach the channel
            var detachTask = channel.DetachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Detached);

            // Now the server sends the ACK for the presence message that was already sent
            mockWs.SendToClient(ProtocolMessages.AckMessage(capturedPresence[0].MsgSerial, 1));

            // Assertions
            // The enter future resolves successfully — ACK was processed despite channel being DETACHED
            var enterResult = await UtsWait.WithTimeout(enterFuture, TimeSpan.FromSeconds(10), "enterFuture");
            enterResult.IsSuccess.Should().BeTrue("AWAIT enter_future succeeds ({0})", enterResult.Error?.Message);

            await CloseClient(client);
        }
    }
}
