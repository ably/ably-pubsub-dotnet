// RealtimeChannel Connection State Side Effects Tests (RTL3, RTL3a, RTL3b, RTL3c, RTL3d, RTL3e, RTL4c1)
// Derived from uts/realtime/unit/channels/channel_connection_state.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving) in the RTL3b tests; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelConnectionStateTests : UtsUnitTestBase
    {
        public ChannelConnectionStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static string RandomId() => Guid.NewGuid().ToString("N");

        // CONNECTED with explicit connectionDetails, as the spec's RTL3c / RTL3d suspended tests send it
        private static JObject DetailedConnected() => ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 120000, maxIdleInterval: 15000);

        // UTS: realtime/unit/RTL3e/disconnected-attached-noop-0
        [UtsFact(Skip = "UTS spec error RTL3e: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTL3e")]
        public async Task RTL3e_DisconnectedAttachedNoop()
        {
            // Setup
            var channelName = "test-RTL3e-attached-" + RandomId();

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

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // NOTE: RTN15a reconnects at once after an unexpected transport drop, so the channel can re-attach (RTL3d) before the
            // assertions run; connection and channel events are recorded on one timeline and the check covers only the DISCONNECTED period
            var timeline = new CaptureList<string>();
            client.Connection.On(change => timeline.Add("connection:" + change.Current));
            channel.On(change => timeline.Add("channel:" + change.Current));

            // Simulate transport failure - connection goes to DISCONNECTED
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            // Assertions
            // Channel state must remain ATTACHED
            channel.State.Should().Be(ChannelState.Attached);

            // No channel state change events should have been emitted
            var duringDisconnected = timeline.Snapshot()
                .SkipWhile(e => e != "connection:" + ConnectionState.Disconnected)
                .Skip(1)
                .TakeWhile(e => !e.StartsWith("connection:"))
                .ToList();
            duringDisconnected.Should().BeEmpty("no channel state change is emitted while the connection is DISCONNECTED");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3e/disconnected-attaching-noop-1
        [UtsFact]
        [Trait("spec", "RTL3e")]
        public async Task RTL3e_DisconnectedAttachingNoop()
        {
            // Setup
            var channelName = "test-RTL3e-attaching-" + RandomId();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Do NOT respond - leave channel in ATTACHING state
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't await - server won't respond
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Simulate transport failure - connection goes to DISCONNECTED
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            // Assertions
            // Channel state must remain ATTACHING
            channel.State.Should().Be(ChannelState.Attaching);

            // No channel state change events should have been emitted
            channelStateChanges.Should().HaveCount(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3a/failed-attached-to-failed-0
        [UtsFact]
        [Trait("spec", "RTL3a")]
        public async Task RTL3a_FailedAttachedToFailed()
        {
            // Setup
            var channelName = "test-RTL3a-attached-" + RandomId();

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

            // Server sends a fatal connection-level ERROR
            mockWs.ActiveConnection.SendToClientAndClose(ProtocolMessages.ErrorMessage(40198, "Account disabled", 403));
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Assertions
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40198);

            // Channel state change event was emitted
            channelStateChanges.Count.Should().BeGreaterThanOrEqualTo(1);
            var failedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Failed);
            failedChange.Should().NotBeNull();
            failedChange.Previous.Should().Be(ChannelState.Attached);
            failedChange.Error.Should().NotBeNull();
            failedChange.Error.Code.Should().Be(40198);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3a/failed-attaching-to-failed-1
        [UtsFact]
        [Trait("spec", "RTL3a")]
        public async Task RTL3a_FailedAttachingToFailed()
        {
            // Setup
            var channelName = "test-RTL3a-attaching-" + RandomId();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Do NOT respond - leave channel in ATTACHING state
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't await - server won't respond
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Server sends a fatal connection-level ERROR
            mockWs.ActiveConnection.SendToClientAndClose(ProtocolMessages.ErrorMessage(40198, "Account disabled", 403));
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // The pending attach should fail
            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;
            error.Should().NotBeNull();

            // Assertions
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();

            var failedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Failed);
            failedChange.Should().NotBeNull();
            failedChange.Previous.Should().Be(ChannelState.Attaching);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3a/other-states-unaffected-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTL3a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL3a")]
        public async Task RTL3a_OtherStatesUnaffected()
        {
            // Setup
            var initializedChannelName = "test-RTL3a-init-" + RandomId();
            var detachedChannelName = "test-RTL3a-detached-" + RandomId();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var initializedChannel = client.Channels.Get(initializedChannelName);
            var detachedChannel = client.Channels.Get(detachedChannelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Leave initialized_channel in INITIALIZED state (never attach)
            initializedChannel.State.Should().Be(ChannelState.Initialized);

            // Attach then detach to get to DETACHED state
            var attachResult = await UtsWait.WithTimeout(detachedChannel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT detached_channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(detachedChannel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT detached_channel.detach() succeeds ({0})", detachResult.Error?.Message);
            detachedChannel.State.Should().Be(ChannelState.Detached);

            // Record state changes on both channels
            var initChanges = UtsRecord.ChannelStateChanges(initializedChannel);
            var detachedChanges = UtsRecord.ChannelStateChanges(detachedChannel);

            // Server sends a fatal connection-level ERROR
            mockWs.ActiveConnection.SendToClientAndClose(ProtocolMessages.ErrorMessage(40198, "Account disabled", 403));
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Assertions
            // Channels not in ATTACHING/ATTACHED should be unaffected
            initializedChannel.State.Should().Be(ChannelState.Initialized);
            detachedChannel.State.Should().Be(ChannelState.Detached);
            initChanges.Should().HaveCount(0);
            detachedChanges.Should().HaveCount(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3b/closed-attached-to-detached-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL3b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL3b")]
        public async Task RTL3b_ClosedAttachedToDetached()
        {
            // Setup
            var channelName = "test-RTL3b-" + RandomId();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };

            // The mock doesn't answer CLOSE (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
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

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);

            var detachedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Detached);
            detachedChange.Should().NotBeNull();
            detachedChange.Previous.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3b/closed-attaching-to-detached-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL3b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL3b")]
        public async Task RTL3b_ClosedAttachingToDetached()
        {
            // Setup
            var channelName = "test-RTL3b-attaching-" + RandomId();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Do NOT respond - leave channel in ATTACHING state
                }
            };

            // The mock doesn't answer CLOSE (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't await - server won't respond
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // The pending attach should fail
            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;
            error.Should().NotBeNull();

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);

            var detachedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Detached);
            detachedChange.Should().NotBeNull();
            detachedChange.Previous.Should().Be(ChannelState.Attaching);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3c/suspended-attached-to-suspended-0
        [UtsFact]
        [Trait("spec", "RTL3c")]
        public async Task RTL3c_SuspendedAttachedToSuspended()
        {
            // Setup
            var channelName = "test-RTL3c-" + RandomId();

            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(DetailedConnected()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                FallbackHosts = new string[0],
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Simulate disconnect - all reconnection attempts will fail
            mockWs.OnConnectionAttempt = conn => conn.RespondWithRefused();
            mockWs.ActiveConnection.SimulateDisconnect();

            // Connection must exhaust disconnectedRetryTimeout retries within connectionStateTtl
            // to transition from DISCONNECTED to SUSPENDED. The total time advance must exceed
            // connectionStateTtl (from connectionDetails, per RTN21).
            for (var i = 0; i < 30; i++)
            {
                await AdvanceTime(5000);
                if (client.Connection.State == ConnectionState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);

            // Assertions
            channel.State.Should().Be(ChannelState.Suspended);

            var suspendedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Suspended);
            suspendedChange.Should().NotBeNull();
            suspendedChange.Previous.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3c/suspended-attaching-to-suspended-1
        [UtsFact]
        [Trait("spec", "RTL3c")]
        public async Task RTL3c_SuspendedAttachingToSuspended()
        {
            // Setup
            var channelName = "test-RTL3c-attaching-" + RandomId();

            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(DetailedConnected()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Do NOT respond - leave channel in ATTACHING state
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                FallbackHosts = new string[0],
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't await - server won't respond
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Simulate disconnect - all reconnection attempts will fail
            mockWs.OnConnectionAttempt = conn => conn.RespondWithRefused();
            mockWs.ActiveConnection.SimulateDisconnect();

            // Connection must exhaust disconnectedRetryTimeout retries within connectionStateTtl
            // to transition from DISCONNECTED to SUSPENDED. The total time advance must exceed
            // connectionStateTtl (from connectionDetails, per RTN21).
            for (var i = 0; i < 30; i++)
            {
                await AdvanceTime(5000);
                if (client.Connection.State == ConnectionState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);

            // Assertions
            channel.State.Should().Be(ChannelState.Suspended);

            var suspendedChange = channelStateChanges.Snapshot().FirstOrDefault(c => c.Current == ChannelState.Suspended);
            suspendedChange.Should().NotBeNull();
            suspendedChange.Previous.Should().Be(ChannelState.Attaching);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3d/reattach-attached-with-serial-0
        [UtsFact]
        [Trait("spec", "RTL3d")]
        public async Task RTL3d_ReattachAttachedWithSerial()
        {
            // Setup
            var channelName = "test-RTL3d-attached-" + RandomId();
            var attachMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    attachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, new Dictionary<string, JToken> { ["channelSerial"] = "serial-001" }));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);
            attachMessages.Should().HaveCount(1);

            // Record channel state changes
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);
            var connectionStates = UtsRecord.ConnectionStates(client.Connection);

            // Simulate disconnect
            mockWs.ActiveConnection.SimulateDisconnect();

            // Wait for reconnection and re-attach
            // NOTE: AWAIT_STATE would return at once (already connected) and the channel may not have left ATTACHED yet; the recorded states are polled instead
            await UtsWait.PollUntil(
                () => connectionStates.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED after DISCONNECTED");
            await UtsWait.PollUntil(
                () => attachMessages.Count == 2 && channel.State == ChannelState.Attached,
                description: "channel re-attached");

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // A second ATTACH message was sent for the re-attach
            attachMessages.Should().HaveCount(2);

            // RTL4c1: The re-attach ATTACH message must include the channelSerial
            // from the previous ATTACHED response
            attachMessages[1].ChannelSerial.Should().Be("serial-001");

            // Channel transitioned through ATTACHING during re-attach
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot().Select(c => c.Current).ToList(), new[] { ChannelState.Attaching, ChannelState.Attached });
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3d/reattach-suspended-channels-1
        [UtsFact]
        [Trait("spec", "RTL3d")]
        public async Task RTL3d_ReattachSuspendedChannels()
        {
            // Setup
            var channelName = "test-RTL3d-suspended-" + RandomId();
            var attachMessageCount = 0;

            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(DetailedConnected()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    Interlocked.Increment(ref attachMessageCount);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                FallbackHosts = new string[0],
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(2000),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Simulate disconnect - all reconnection attempts will fail
            mockWs.OnConnectionAttempt = conn => conn.RespondWithRefused();
            mockWs.ActiveConnection.SimulateDisconnect();

            // Connection must exhaust disconnectedRetryTimeout retries within connectionStateTtl
            // to transition from DISCONNECTED to SUSPENDED. The total time advance must exceed
            // connectionStateTtl (from connectionDetails, per RTN21).
            for (var i = 0; i < 30; i++)
            {
                await AdvanceTime(5000);
                if (client.Connection.State == ConnectionState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);
            channel.State.Should().Be(ChannelState.Suspended);

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStateChanges(channel);

            // Allow reconnection to succeed
            mockWs.OnConnectionAttempt = conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());

            // Advance time past suspendedRetryTimeout to trigger retry
            for (var i = 0; i < 10; i++)
            {
                await AdvanceTime(2500);
                if (client.Connection.State == ConnectionState.Connected)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // An ATTACH message was sent for the re-attach
            Volatile.Read(ref attachMessageCount).Should().BeGreaterThanOrEqualTo(2);

            // Channel transitioned from SUSPENDED through ATTACHING to ATTACHED
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot().Select(c => c.Current).ToList(), new[] { ChannelState.Attaching, ChannelState.Attached });
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3d/init-detached-not-reattached-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTL3d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL3d")]
        public async Task RTL3d_InitDetachedNotReattached()
        {
            // Setup
            var initializedChannelName = "test-RTL3d-init-" + RandomId();
            var detachedChannelName = "test-RTL3d-detached-" + RandomId();
            var attachMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    attachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100),
            });
            var initializedChannel = client.Channels.Get(initializedChannelName);
            var detachedChannel = client.Channels.Get(detachedChannelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Leave initialized_channel in INITIALIZED state
            initializedChannel.State.Should().Be(ChannelState.Initialized);

            // Attach then detach to get to DETACHED state
            var attachResult = await UtsWait.WithTimeout(detachedChannel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT detached_channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(detachedChannel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT detached_channel.detach() succeeds ({0})", detachResult.Error?.Message);
            detachedChannel.State.Should().Be(ChannelState.Detached);

            var attachCountBefore = attachMessages.Count;

            // Record state changes
            var initChanges = UtsRecord.ChannelStateChanges(initializedChannel);
            var detachedChanges = UtsRecord.ChannelStateChanges(detachedChannel);
            var connectionStates = UtsRecord.ConnectionStates(client.Connection);

            // Simulate disconnect and reconnect
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: AWAIT_STATE would return at once (already connected); the recorded states are polled for the re-entry
            await UtsWait.PollUntil(
                () => connectionStates.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED after DISCONNECTED");

            // Assertions
            // Neither channel should have been re-attached
            initializedChannel.State.Should().Be(ChannelState.Initialized);
            detachedChannel.State.Should().Be(ChannelState.Detached);
            initChanges.Should().HaveCount(0);
            detachedChanges.Should().HaveCount(0);

            // No new ATTACH messages for these channels
            var newAttachChannels = attachMessages.Snapshot().Skip(attachCountBefore).Select(m => m.Channel).ToList();
            newAttachChannels.Should().NotContain(initializedChannelName);
            newAttachChannels.Should().NotContain(detachedChannelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL3d/multiple-channels-reattached-3
        [UtsFact]
        [Trait("spec", "RTL3d")]
        public async Task RTL3d_MultipleChannelsReattached()
        {
            // Setup
            var channel1Name = "test-RTL3d-multi1-" + RandomId();
            var channel2Name = "test-RTL3d-multi2-" + RandomId();
            var attachMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    attachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100),
            });
            var channel1 = client.Channels.Get(channel1Name);
            var channel2 = client.Channels.Get(channel2Name);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult1 = await UtsWait.WithTimeout(channel1.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult1.IsSuccess.Should().BeTrue("AWAIT channel1.attach() succeeds ({0})", attachResult1.Error?.Message);
            var attachResult2 = await UtsWait.WithTimeout(channel2.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult2.IsSuccess.Should().BeTrue("AWAIT channel2.attach() succeeds ({0})", attachResult2.Error?.Message);
            channel1.State.Should().Be(ChannelState.Attached);
            channel2.State.Should().Be(ChannelState.Attached);

            var attachCountBefore = attachMessages.Count;
            var connectionStates = UtsRecord.ConnectionStates(client.Connection);

            // Simulate disconnect and reconnect
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: AWAIT_STATE would return at once (already connected / attached); the recorded states and the ATTACH count are polled for the re-entry
            await UtsWait.PollUntil(
                () => connectionStates.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED after DISCONNECTED");
            await UtsWait.PollUntil(
                () => attachMessages.Count >= attachCountBefore + 2 && channel1.State == ChannelState.Attached && channel2.State == ChannelState.Attached,
                description: "both channels re-attached");

            // Assertions
            channel1.State.Should().Be(ChannelState.Attached);
            channel2.State.Should().Be(ChannelState.Attached);

            // Both channels should have received new ATTACH messages
            var newAttachChannels = attachMessages.Snapshot().Skip(attachCountBefore).Select(m => m.Channel).ToList();
            newAttachChannels.Should().Contain(channel1Name);
            newAttachChannels.Should().Contain(channel2Name);
            await CloseClient(client);
        }
    }
}
