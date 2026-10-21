// RealtimeChannel Detach Tests (RTL5, RTL5a, RTL5b, RTL5d, RTL5e, RTL5f, RTL5i, RTL5j, RTL5k, RTL5l)
// Derived from uts/realtime/unit/channels/channel_detach.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: fake timers enabled in RTL5l/detach-attached-when-disconnected-1 (infra-driving: keeps the connection DISCONNECTED until time is advanced); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelDetachTests : UtsUnitTestBase
    {
        public ChannelDetachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL5a/detach-initialized-noop-0
        [UtsFact]
        [Trait("spec", "RTL5a")]
        public async Task RTL5a_DetachInitializedNoop()
        {
            // Setup
            var channelName = "test-RTL5a-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            channel.State.Should().Be(ChannelState.Initialized);

            // Detach from initialized state - should be no-op
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().BeOneOf(ChannelState.Initialized, ChannelState.Detached);

            // No state change events should have been emitted (or only to detached)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5a/detach-already-detached-noop-1
        [UtsFact]
        [Trait("spec", "RTL5a")]
        public async Task RTL5a_DetachAlreadyDetachedNoop()
        {
            // Setup
            var channelName = "test-RTL5a-detached-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    Interlocked.Increment(ref detachMessageCount);
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach then detach
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(1);

            // Second detach - should be no-op
            var secondDetachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            secondDetachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", secondDetachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(1, "no additional DETACH message sent");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5i/detach-while-detaching-0
        [UtsFact]
        [Trait("spec", "RTL5i")]
        public async Task RTL5i_DetachWhileDetaching()
        {
            // Setup
            var channelName = "test-RTL5i-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    Interlocked.Increment(ref detachMessageCount);

                    // Delay response to allow second detach call
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

            // Start first detach (don't await)
            var detachFuture1 = channel.DetachAsync();

            // Wait for channel to enter detaching state
            await UtsWait.AwaitChannelState(channel, ChannelState.Detaching);

            // Start second detach while first is pending
            var detachFuture2 = channel.DetachAsync();

            // Now send the DETACHED response
            mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));

            // Both should complete
            var detachResult1 = await UtsWait.WithTimeout(detachFuture1, TimeSpan.FromSeconds(10), "detachFuture1");
            detachResult1.IsSuccess.Should().BeTrue("AWAIT detach_future_1 succeeds ({0})", detachResult1.Error?.Message);
            var detachResult2 = await UtsWait.WithTimeout(detachFuture2, TimeSpan.FromSeconds(10), "detachFuture2");
            detachResult2.IsSuccess.Should().BeTrue("AWAIT detach_future_2 succeeds ({0})", detachResult2.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(1, "only one DETACH message sent");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5i/detach-while-attaching-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL5i", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5i")]
        public async Task RTL5i_DetachWhileAttaching()
        {
            // Setup
            var channelName = "test-RTL5i-attaching-" + Guid.NewGuid().ToString("N");
            var messagesFromClient = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                messagesFromClient.Add(msg);
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Delay response
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach (don't await)
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Start detach while attaching
            var detachFuture = channel.DetachAsync();

            // Send ATTACHED response - attach completes
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            // Wait for both operations
            // NOTE: the spec's implementation note allows the superseded attach to resolve or to be rejected; the attach outcome is not asserted
            await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            var detachResult = await UtsWait.WithTimeout(detachFuture, TimeSpan.FromSeconds(10), "detachFuture");
            detachResult.IsSuccess.Should().BeTrue("AWAIT detach_future succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);

            // Should have: ATTACH, DETACH
            var sent = messagesFromClient.Snapshot();
            sent.Should().HaveCount(2);
            ((int)sent[0].Action).Should().Be(ProtocolMessages.Attach);
            ((int)sent[1].Action).Should().Be(ProtocolMessages.Detach);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5b/detach-failed-errors-0
        [UtsFact]
        [Trait("spec", "RTL5b")]
        public async Task RTL5b_DetachFailedErrors()
        {
            // Setup
            var channelName = "test-RTL5b-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Fail the attachment
                    mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted"));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach fails - channel enters failed state
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var attachError = attachResult.Error;
            attachError.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Failed);

            // Try to detach from failed state
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsFailure.Should().BeTrue();
            var error = detachResult.Error;

            // Assertions
            error.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Failed, "state unchanged");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5j/detach-suspended-to-detached-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL5j", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5j")]
        public async Task RTL5j_DetachSuspendedToDetached()
        {
            // Setup
            var channelName = "test-RTL5j-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            // NOTE: enable_fake_timers() is hoisted above the client construction, as the fake clock must be on before Realtime(...)
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    // Don't respond - let it timeout to suspended
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    Interlocked.Increment(ref detachMessageCount);
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach
            var attachFuture = channel.AttachAsync();

            // Let it timeout to suspended
            await AdvanceTime(150);
            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;
            error.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Suspended);

            // Detach from suspended
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(5), "channel.detach()");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(0, "no DETACH message sent - immediate transition");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5l/detach-not-connected-immediate-0
        [UtsFact]
        [Trait("spec", "RTL5l")]
        public async Task RTL5l_DetachNotConnectedImmediate()
        {
            // Setup
            var channelName = "test-RTL5l-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Delay connection
            });
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    Interlocked.Increment(ref detachMessageCount);
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Start connecting but don't complete
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Put channel into attaching state
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Now detach while connection is still connecting
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(0, "no DETACH message sent");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5l/detach-attached-when-disconnected-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN14d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5l")]
        public async Task RTL5l_DetachAttachedWhenDisconnected()
        {
            // Setup
            // NOTE: fake timers keep the connection DISCONNECTED (no automatic retry) until time is advanced; see the header disclosure
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithConnected());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channel = client.Channels.Get("test-channel");

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach the channel
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                }
            };
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Disconnect the transport
            mockWs.ActiveConnection.SimulateDisconnect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            // Now detach while disconnected
            var messagesSent = new CaptureList<ProtocolMessage>();
            mockWs.OnMessageFromClient = msg => messagesSent.Add(msg);

            var detachFuture = channel.DetachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Detached);

            // Assertions
            // Channel transitions directly to DETACHED
            channel.State.Should().Be(ChannelState.Detached);

            // No DETACH message was sent (transport is unavailable)
            var detachMessages = messagesSent.Snapshot().Where(m => (int)m.Action == ProtocolMessages.Detach).ToList();
            detachMessages.Should().HaveCount(0);
            await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            await UtsWait.WithTimeout(detachFuture, TimeSpan.FromSeconds(10), "detachFuture");
        }

        // UTS: realtime/unit/RTL5d/normal-detach-flow-0
        [UtsFact]
        [Trait("spec", "RTL5d")]
        public async Task RTL5d_NormalDetachFlow()
        {
            // Setup
            var channelName = "test-RTL5d-" + Guid.NewGuid().ToString("N");
            var capturedDetachMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    capturedDetachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
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

            var statesDuringDetach = new CaptureList<ChannelState>();
            channel.On(ChannelEvent.Detaching, change => statesDuringDetach.Add(channel.State));

            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            statesDuringDetach.Should().NotBeEmpty();
            statesDuringDetach[0].Should().Be(ChannelState.Detaching);
            channel.State.Should().Be(ChannelState.Detached);
            capturedDetachMessages.Should().NotBeEmpty();
            var capturedDetachMessage = capturedDetachMessages[0];
            ((int)capturedDetachMessage.Action).Should().Be(ProtocolMessages.Detach);
            capturedDetachMessage.Channel.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5f/timeout-returns-previous-state-0
        [UtsFact]
        [Trait("spec", "RTL5f")]
        public async Task RTL5f_TimeoutReturnsPreviousState()
        {
            // Setup
            var channelName = "test-RTL5f-" + Guid.NewGuid().ToString("N");

            // NOTE: enable_fake_timers() is hoisted above the client construction, as the fake clock must be on before Realtime(...)
            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    // Don't respond - simulate timeout
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            var detachFuture = channel.DetachAsync();

            // Advance time past timeout
            await AdvanceTime(150);

            var detachResult = await UtsWait.WithTimeout(detachFuture, TimeSpan.FromSeconds(10), "detachFuture");
            detachResult.IsFailure.Should().BeTrue();
            var error = detachResult.Error;

            // Assertions
            channel.State.Should().Be(ChannelState.Attached, "returns to previous state");
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5k/attached-while-detaching-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL5k", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5k")]
        public async Task RTL5k_AttachedWhileDetaching()
        {
            // Setup
            var channelName = "test-RTL5k-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    if (Interlocked.Increment(ref detachMessageCount) == 1)
                    {
                        // First DETACH: server sends ATTACHED instead of DETACHED
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else
                    {
                        // Second DETACH: respond correctly
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                    }
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

            // Start detach
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(2, "two DETACH messages sent");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5k/attached-while-detached-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL5k", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5k")]
        public async Task RTL5k_AttachedWhileDetached()
        {
            // Setup
            var channelName = "test-RTL5k-detached-" + Guid.NewGuid().ToString("N");
            var detachMessageCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    Interlocked.Increment(ref detachMessageCount);
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
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
            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Detached);
            Volatile.Read(ref detachMessageCount).Should().Be(1);

            // Server unexpectedly sends ATTACHED while detached
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            // Let the client process the inbound message (see process_pending_events in uts/README.md)
            await ProcessPendingEvents();

            // Assertions
            Volatile.Read(ref detachMessageCount).Should().Be(2, "client sent another DETACH");
            channel.State.Should().Be(ChannelState.Detached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL5/detach-state-change-events-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL5", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL5")]
        public async Task RTL5_DetachStateChangeEvents()
        {
            // Setup
            var channelName = "test-RTL5-events-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            mockWs.OnMessageFromClient = msg =>
            {
                if ((int)msg.Action == ProtocolMessages.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                }
                else if ((int)msg.Action == ProtocolMessages.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));
                }
            };
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            var stateChanges = UtsRecord.ChannelStateChanges(channel);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Clear attach state changes
            stateChanges.Clear();

            var detachResult = await UtsWait.WithTimeout(channel.DetachAsync(), TimeSpan.FromSeconds(10), "DetachAsync");
            detachResult.IsSuccess.Should().BeTrue("AWAIT channel.detach() succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            stateChanges.Count.Should().BeGreaterThanOrEqualTo(2);

            // First event: detaching
            stateChanges[0].Current.Should().Be(ChannelState.Detaching);
            stateChanges[0].Previous.Should().Be(ChannelState.Attached);
            stateChanges[0].Event.Should().Be(ChannelEvent.Detaching);

            // Second event: detached
            stateChanges[1].Current.Should().Be(ChannelState.Detached);
            stateChanges[1].Previous.Should().Be(ChannelState.Detaching);
            stateChanges[1].Event.Should().Be(ChannelEvent.Detached);
            await CloseClient(client);
        }

        // NOTE: the spec's "[REMOVED] RTL5 - Detach clears errorReason" section has no Test ID and no test case; nothing to translate.
    }
}
