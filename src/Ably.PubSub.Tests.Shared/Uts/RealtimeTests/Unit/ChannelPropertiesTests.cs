// Channel Properties Tests (RTL15, RTL15b, RTL15b1, RTL15b2, RTL15c)
// Derived from uts/realtime/unit/channels/channel_properties.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: infra stand-in: fake timers are enabled before the client is built where the spec enables them after (RTL15b2/serial-retained-suspended-1); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelPropertiesTests : UtsUnitTestBase
    {
        public ChannelPropertiesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static IDictionary<string, JToken> Serial(string channelSerial)
            => new Dictionary<string, JToken> { ["channelSerial"] = channelSerial };

        // UTS: realtime/unit/RTL15c/attach-serial-from-attached-0
        [UtsFact]
        [Trait("spec", "RTL15c")]
        public async Task RTL15c_AttachSerialFromAttached()
        {
            // Setup
            var channelName = "test-RTL15c-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        var count = Interlocked.Increment(ref attachCount);

                        // No RESUMED flag, so resumed is false
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("attach-serial-" + count)));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Before connecting, attachSerial should be unset
            channel.Properties.AttachSerial.Should().BeNull();

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            // attachSerial set from ATTACHED response
            channel.Properties.AttachSerial.Should().Be("attach-serial-1");

            // Detach and reattach to get a new attachSerial
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            var reattachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            reattachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", reattachResult.Error?.Message);

            // attachSerial updated from second ATTACHED response
            channel.Properties.AttachSerial.Should().Be("attach-serial-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15c/attach-serial-server-reattach-1
        [UtsFact]
        [Trait("spec", "RTL15c")]
        public async Task RTL15c_AttachSerialServerReattach()
        {
            // Setup
            var channelName = "test-RTL15c-update-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("initial-serial")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.AttachSerial.Should().Be("initial-serial");

            // Server sends unsolicited ATTACHED without the RESUMED flag (e.g. RTL12 update)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(channelName, Serial("updated-serial")));
            await UtsWait.PollUntil(
                () => channel.Properties.AttachSerial == "updated-serial",
                description: "channel.properties.attachSerial == updated-serial");

            // Assertions
            channel.Properties.AttachSerial.Should().Be("updated-serial");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15c/attach-serial-not-updated-resumed-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTL15c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL15c")]
        public async Task RTL15c_AttachSerialNotUpdatedResumed()
        {
            // Setup
            var channelName = "test-RTL15c-resumed-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("initial-serial")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.AttachSerial.Should().Be("initial-serial");

            // Server sends unsolicited ATTACHED WITH the RESUMED flag
            // (successful resume, no loss of continuity)
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(
                channelName,
                new Dictionary<string, JToken>
                {
                    ["channelSerial"] = "resumed-serial",
                    ["flags"] = (int)ProtocolMessage.Flag.Resumed,
                }));

            // channelSerial is updated per RTL15b regardless of the RESUMED flag,
            // confirming the message has been processed
            await UtsWait.PollUntil(
                () => channel.Properties.ChannelSerial == "resumed-serial",
                description: "channel.properties.channelSerial == resumed-serial");

            // Assertions
            // attachSerial must be unchanged, since the ATTACHED message had resumed=true
            channel.Properties.AttachSerial.Should().Be("initial-serial");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b/channel-serial-from-attached-0
        [UtsFact]
        [Trait("spec", "RTL15b")]
        public async Task RTL15b_ChannelSerialFromAttached()
        {
            // Setup
            var channelName = "test-RTL15b-attached-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Before attach, channelSerial should be unset
            channel.Properties.ChannelSerial.Should().BeNull();

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            channel.Properties.ChannelSerial.Should().Be("serial-001");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b/channel-serial-from-messages-1
        [UtsFact]
        [Trait("spec", "RTL15b")]
        public async Task RTL15b_ChannelSerialFromMessages()
        {
            // Setup
            var channelName = "test-RTL15b-messages-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // Server sends MESSAGE with channelSerial
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray(new JObject { ["name"] = "event", ["data"] = "data" }),
                Serial("serial-002")));
            await UtsWait.PollUntil(
                () => channel.Properties.ChannelSerial == "serial-002",
                description: "channel.properties.channelSerial == serial-002");

            // Server sends PRESENCE with channelSerial
            // NOTE: the spec's PRESENCE message carries no presence entries; an empty presence array is sent
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                channelName,
                new JArray(),
                Serial("serial-003")));
            await UtsWait.PollUntil(
                () => channel.Properties.ChannelSerial == "serial-003",
                description: "channel.properties.channelSerial == serial-003");

            // Assertions
            channel.Properties.ChannelSerial.Should().Be("serial-003");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b/serial-not-updated-empty-2
        [UtsFact]
        [Trait("spec", "RTL15b")]
        public async Task RTL15b_SerialNotUpdatedEmpty()
        {
            // Setup
            var channelName = "test-RTL15b-noupdate-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // Server sends MESSAGE without channelSerial
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.MessageProtocolMessage(
                channelName,
                new JArray(new JObject { ["name"] = "event", ["data"] = "data" })));

            // NOTE: the spec has no wait for this negative check; the queued work is drained so the MESSAGE has been processed before the assertion
            await ProcessPendingEvents();

            // Assertions
            // channelSerial should remain unchanged
            channel.Properties.ChannelSerial.Should().Be("serial-001");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b/serial-not-updated-irrelevant-3
        [UtsFact]
        [Trait("spec", "RTL15b")]
        public async Task RTL15b_SerialNotUpdatedIrrelevant()
        {
            // Setup
            var channelName = "test-RTL15b-irrelevant-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        Interlocked.Increment(ref attachCount);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // Server sends DETACHED with a channelSerial field
            // (RTL13a will trigger reattach, but the DETACHED itself should not update channelSerial)
            // Record channelSerial before the DETACHED
            var serialBefore = channel.Properties.ChannelSerial;
            serialBefore.Should().Be("serial-001");

            // NOTE: AWAIT_STATE attached would return at once (already ATTACHED); the states are recorded before the stimulus and the re-entry of ATTACHED is polled
            var states = UtsRecord.ChannelStates(channel);

            var detached = ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Detached", 500);
            detached["channelSerial"] = "serial-should-not-apply";
            mockWs.ActiveConnection.SendToClient(detached);

            // Wait for the reattach to complete (RTL13a)
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ChannelState.Attaching).Any(s => s == ChannelState.Attached),
                description: "channel re-entered ATTACHED after the DETACHED");

            // Assertions
            // channelSerial should be from the new ATTACHED, not from DETACHED
            // The DETACHED action should not have updated channelSerial
            // (RTL15b2 clears it on DETACHED/FAILED, then ATTACHED sets it fresh)
            Volatile.Read(ref attachCount).Should().Be(2);
            channel.Properties.ChannelSerial.Should().Be("serial-001");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b2/serial-cleared-detached-0
        [UtsFact]
        [Trait("spec", "RTL15b2")]
        public async Task RTL15b2_SerialClearedDetached()
        {
            // Setup
            var channelName = "test-RTL15b1-detached-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            channel.Properties.ChannelSerial.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b2/serial-retained-suspended-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL15b2", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL15b2")]
        public async Task RTL15b2_SerialRetainedSuspended()
        {
            // Setup
            var channelName = "test-RTL15b2-suspended-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        if (Interlocked.Increment(ref attachCount) == 1)
                        {
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                        }

                        // Don't respond to second attach (causes timeout -> SUSPENDED)
                    }
                });
            InstallMock(mockWs);

            // NOTE: the spec calls enable_fake_timers() after building the client; fake timers are enabled before the client (a timer already running stays on the real clock)
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // Trigger server-initiated DETACHED -> reattach attempt that will timeout
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ServerDetachedMessage(channelName, 90198, "Detached", 500));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Let attach timeout -> SUSPENDED
            await AdvanceTime(150);
            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Assertions
            channel.State.Should().Be(ChannelState.Suspended);

            // RTL15b2: channelSerial is retained through SUSPENDED, not cleared
            channel.Properties.ChannelSerial.Should().Be("serial-001");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL15b2/serial-cleared-failed-2
        [UtsFact]
        [Trait("spec", "RTL15b2")]
        public async Task RTL15b2_SerialClearedFailed()
        {
            // Setup
            var channelName = "test-RTL15b2-failed-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel, Serial("serial-001")));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.Properties.ChannelSerial.Should().Be("serial-001");

            // Server sends channel ERROR -> FAILED
            mockWs.ActiveConnection.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted", 401));
            await UtsWait.AwaitChannelState(channel, ChannelState.Failed);

            // Assertions
            channel.State.Should().Be(ChannelState.Failed);
            channel.Properties.ChannelSerial.Should().BeNull();
            await CloseClient(client);
        }
    }
}
