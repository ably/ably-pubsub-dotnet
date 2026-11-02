// RealtimeChannel Attach Tests (RTL4, RTL4a, RTL4b, RTL4c, RTL4c1, RTL4f, RTL4g, RTL4h, RTL4i, RTL4j, RTL4k, RTL4l, RTL4m, RTL4m1)
// Derived from uts/realtime/unit/channels/channel_attach.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelAttachTests : UtsUnitTestBase
    {
        public ChannelAttachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL4a/already-attached-noop-0
        [UtsFact]
        [Trait("spec", "RTL4a")]
        public async Task RTL4a_AlreadyAttachedNoop()
        {
            // Setup
            var channelName = "test-RTL4a-" + Guid.NewGuid().ToString("N");
            var attachMessageCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Increment(ref attachMessageCount);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // First attach
            var firstAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            firstAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", firstAttach.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1);

            // Second attach - should be no-op
            var secondAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            secondAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", secondAttach.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1); // No additional ATTACH message sent
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4h/attach-while-attaching-0
        [UtsFact]
        [Trait("spec", "RTL4h")]
        public async Task RTL4h_AttachWhileAttaching()
        {
            // Setup
            var channelName = "test-RTL4h-" + Guid.NewGuid().ToString("N");
            var attachMessageCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Increment(ref attachMessageCount);

                        // Delay response to allow second attach call
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start first attach (don't await)
            var attachFuture1 = channel.AttachAsync();

            // Wait for channel to enter attaching state
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Start second attach while first is pending
            var attachFuture2 = channel.AttachAsync();

            // Now send the ATTACHED response
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            // Both should complete
            var attachResult1 = await UtsWait.WithTimeout(attachFuture1, TimeSpan.FromSeconds(10), "attachFuture1");
            attachResult1.IsSuccess.Should().BeTrue("AWAIT attach_future_1 succeeds ({0})", attachResult1.Error?.Message);
            var attachResult2 = await UtsWait.WithTimeout(attachFuture2, TimeSpan.FromSeconds(10), "attachFuture2");
            attachResult2.IsSuccess.Should().BeTrue("AWAIT attach_future_2 succeeds ({0})", attachResult2.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageCount).Should().Be(1); // Only one ATTACH message sent
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4h/attach-while-detaching-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL4h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL4h")]
        public async Task RTL4h_AttachWhileDetaching()
        {
            // Setup
            var channelName = "test-RTL4h-detaching-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                    {
                        // Delay DETACHED response
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach first
            var firstAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            firstAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", firstAttach.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Start detach (don't await)
            var detachFuture = channel.DetachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Detaching);

            // Start attach while detaching
            var attachFuture = channel.AttachAsync();

            // Send DETACHED response
            mockWs.SendToClient(ProtocolMessages.DetachedMessage(channelName));

            // Wait for detach to complete
            var detachResult = await UtsWait.WithTimeout(detachFuture, TimeSpan.FromSeconds(10), "detachFuture");
            detachResult.IsSuccess.Should().BeTrue("AWAIT detach_future succeeds ({0})", detachResult.Error?.Message);

            // Now ATTACH should be sent and we wait for it
            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsSuccess.Should().BeTrue("AWAIT attach_future succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // Should have: ATTACH, DETACH, ATTACH
            var attachMessages = mockWs.MessagesFromClientOfAction(ProtocolMessages.Attach);
            attachMessages.Count.Should().Be(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4g/attach-from-failed-0
        [UtsFact]
        [Trait("spec", "RTL4g")]
        public async Task RTL4g_AttachFromFailed()
        {
            // Setup
            var channelName = "test-RTL4g-" + Guid.NewGuid().ToString("N");
            var attachCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        if (Interlocked.Increment(ref attachCount) == 1)
                        {
                            // First attach fails
                            // NOTE: hand-built: ChannelErrorMessage defaults statusCode to code / 100 and the spec gives none
                            mockWs.SendToClient(new JObject
                            {
                                ["action"] = ProtocolMessages.Error,
                                ["channel"] = channelName,
                                ["error"] = ProtocolMessages.ErrorObject(40160, "Denied"),
                            });
                        }
                        else
                        {
                            // Second attach succeeds
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                        }
                    }
                });
            InstallMock(mockWs);

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

            // Second attach from failed state
            var secondAttach = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            secondAttach.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", secondAttach.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // RTL4c: successful attach clears errorReason
            channel.ErrorReason.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c/clears-error-reason-0
        [UtsFact]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_ClearsErrorReason()
        {
            // Setup
            var channelName = "test-RTL4c-error-clear-" + Guid.NewGuid().ToString("N");

            // NOTE: enable_fake_timers() is called before the client is built, as the infra requires
            EnableFakeTimers();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
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
                FallbackHosts = new string[0],
                SuspendedRetryTimeout = TimeSpan.FromMilliseconds(2000),
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Simulate disconnect — push connection through to SUSPENDED
            mockWs.OnConnectionAttempt = conn => conn.RespondWithRefused();
            mockWs.ActiveConnection.SimulateDisconnect();

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

            // Channel should have errorReason set from the connection failure
            channel.ErrorReason.Should().NotBeNull();

            // Allow reconnection to succeed
            mockWs.OnConnectionAttempt = conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());

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
            // errorReason cleared by the successful attach (RTL4c)
            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4b/fails-connection-closed-0
        [UtsFact]
        [Trait("spec", "RTL4b")]
        public async Task RTL4b_FailsConnectionClosed()
        {
            // Setup
            var channelName = "test-RTL4b-closed-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            // Disclosures: the mock answers CLOSE with CLOSED (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Connection.State.Should().Be(ConnectionState.Closed);

            // Try to attach
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            error.Should().NotBeNull();

            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so it is never null (compile-time guarantee)
            channel.State.Should().NotBe(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4b/fails-connection-failed-1
        [UtsFact]
        [Trait("spec", "RTL4b")]
        public async Task RTL4b_FailsConnectionFailed()
        {
            // Setup
            var channelName = "test-RTL4b-failed-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());

                // Server sends fatal error
                // NOTE: hand-built: ErrorMessage defaults statusCode to code / 100 and the spec gives none
                conn.SendToClientAndClose(new JObject
                {
                    ["action"] = ProtocolMessages.Error,
                    ["error"] = ProtocolMessages.ErrorObject(80000, "Fatal error"),
                });
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // Try to attach
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            error.Should().NotBeNull();
            channel.State.Should().NotBe(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4b/fails-connection-suspended-2
        [UtsFact(Skip = "UTS spec error RTL4b: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTL4b")]
        public Task RTL4b_FailsConnectionSuspended()
        {
            // SPEC ERROR RTL4b: the test reaches connection SUSPENDED by refusing every connection with real timers and only channelRetryTimeout: 100 ("short suspend timeout"), but channelRetryTimeout governs channel retries (RTB1); the connection only enters SUSPENDED after connectionStateTtl (RTN14e, DF1a default 120 s), so the AWAIT_STATE can't be met in a unit test.
            // Fix the UTS spec first (fake timers and ADVANCE_TIME past connectionStateTtl, or a short server-sent connectionStateTtl) - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTL4b - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTL4i/queued-while-connecting-0
        [UtsFact]
        [Trait("spec", "RTL4i")]
        public async Task RTL4i_QueuedWhileConnecting()
        {
            // Setup
            var channelName = "test-RTL4i-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Delay connection response
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Start connecting but don't complete
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Start attach while connection is still connecting
            var attachFuture = channel.AttachAsync();

            // Channel should immediately enter attaching
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Assertions
            channel.State.Should().Be(ChannelState.Attaching);

            // Attach message not yet sent (connection not ready)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4i/completes-on-connected-1
        [UtsFact]
        [Trait("spec", "RTL4i")]
        public async Task RTL4i_CompletesOnConnected()
        {
            // Setup
            var channelName = "test-RTL4i-connected-" + Guid.NewGuid().ToString("N");
            var attachMessageReceived = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    // Delay connection response
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        Interlocked.Exchange(ref attachMessageReceived, 1);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Start connecting
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Start attach while connecting
            var attachFuture = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);
            Volatile.Read(ref attachMessageReceived).Should().Be(0);

            // Complete connection
            var pendingConn = await mockWs.AwaitConnectionAttempt();
            pendingConn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());

            // Wait for attach to complete
            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsSuccess.Should().BeTrue("AWAIT attach_future succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            Volatile.Read(ref attachMessageReceived).Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c/sends-attach-message-1
        [UtsFact]
        [Trait("spec", "RTL4c")]
        public async Task RTL4c_SendsAttachMessage()
        {
            // Setup
            var channelName = "test-RTL4c-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        capturedAttachMessages.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var statesDuringAttach = new CaptureList<ChannelState>();
            channel.On(ChannelEvent.Attaching, change => statesDuringAttach.Add(channel.State));

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            statesDuringAttach.Count.Should().BeGreaterThanOrEqualTo(1);
            statesDuringAttach[0].Should().Be(ChannelState.Attaching);
            channel.State.Should().Be(ChannelState.Attached);
            capturedAttachMessages.Count.Should().BeGreaterThanOrEqualTo(1);
            capturedAttachMessages[0].Action.Should().Be(ProtocolMessage.MessageAction.Attach);
            capturedAttachMessages[0].Channel.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4c1/includes-channel-serial-0
        [UtsFact]
        [Trait("spec", "RTL4c1")]
        public async Task RTL4c1_IncludesChannelSerial()
        {
            // Setup
            var channelName = "test-RTL4c1-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        capturedAttachMessages.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                            channelName,
                            new Dictionary<string, JToken> { ["channelSerial"] = "serial-from-server-1" }));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // First attach - no channelSerial yet
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Trigger reattach via setOptions (RTL16a) — does NOT go through DETACHED,
            // so channelSerial is preserved (RTL15b2 only clears on DETACHED/FAILED)
            var setOptionsResult = await UtsWait.WithTimeout(channel.SetOptionsAsync(new ChannelOptions(modes: new ChannelModes(ChannelMode.Subscribe))), TimeSpan.FromSeconds(10), "SetOptionsAsync");
            setOptionsResult.IsSuccess.Should().BeTrue("AWAIT channel.setOptions() succeeds ({0})", setOptionsResult.Error?.Message);

            // Assertions
            capturedAttachMessages.Count.Should().Be(2);

            // First attach has no channelSerial (or null)
            capturedAttachMessages[0].ChannelSerial.Should().BeNull();

            // Second attach (reattach via setOptions) includes channelSerial
            capturedAttachMessages[1].ChannelSerial.Should().Be("serial-from-server-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4f/timeout-to-suspended-0
        [UtsFact]
        [Trait("spec", "RTL4f")]
        public async Task RTL4f_TimeoutToSuspended()
        {
            // Setup
            var channelName = "test-RTL4f-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Don't respond - simulate timeout
                    }
                });
            InstallMock(mockWs);

            // NOTE: enable_fake_timers() is called before the client is built, as the infra requires (the spec calls it in Test Steps)
            EnableFakeTimers();

            // Use short timeout for testing
            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100), // 100ms timeout for testing
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachFuture = channel.AttachAsync();

            // Advance time past timeout
            await AdvanceTime(150);

            var attachResult = await UtsWait.WithTimeout(attachFuture, TimeSpan.FromSeconds(10), "attachFuture");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            channel.State.Should().Be(ChannelState.Suspended);
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4k/includes-channel-params-0
        [UtsFact]
        [Trait("spec", "RTL4k")]
        public async Task RTL4k_IncludesChannelParams()
        {
            // Setup
            var channelName = "test-RTL4k-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        capturedAttachMessages.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channelOptions = new ChannelOptions(channelParams: new ChannelParams { ["rewind"] = "1", ["delta"] = "vcdiff" });
            var channel = client.Channels.Get(channelName, channelOptions);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            capturedAttachMessages.Count.Should().BeGreaterThanOrEqualTo(1);
            capturedAttachMessages[0].Params.Should().NotBeNull();
            capturedAttachMessages[0].Params["rewind"].Should().Be("1");
            capturedAttachMessages[0].Params["delta"].Should().Be("vcdiff");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4l/modes-encoded-as-flags-0
        [UtsFact]
        [Trait("spec", "RTL4l")]
        public async Task RTL4l_ModesEncodedAsFlags()
        {
            // Setup
            var channelName = "test-RTL4l-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        capturedAttachMessages.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            var channelOptions = new ChannelOptions(modes: new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe));
            var channel = client.Channels.Get(channelName, channelOptions);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            capturedAttachMessages.Count.Should().BeGreaterThanOrEqualTo(1);
            capturedAttachMessages[0].Flags.Should().HaveValue();

            // Flags should include PUBLISH (131072, TR3r bit 17) and SUBSCRIBE (262144, TR3s bit 18) bits
            (capturedAttachMessages[0].Flags.Value & 131072).Should().NotBe(0); // PUBLISH bit set
            (capturedAttachMessages[0].Flags.Value & 262144).Should().NotBe(0); // SUBSCRIBE bit set
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4m/modes-from-attached-0
        [UtsFact]
        [Trait("spec", "RTL4m")]
        public async Task RTL4m_ModesFromAttached()
        {
            // Setup
            var channelName = "test-RTL4m-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // PUBLISH (131072, TR3r) + MESSAGE_SUBSCRIBE (262144, TR3u)
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 393216 }));
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

            // Assertions
            channel.Modes.Should().NotBeNull();
            channel.Modes.Should().Contain(ChannelMode.Publish);

            // NOTE: the SDK's ChannelMode has no message_subscribe member: ChannelMode.Subscribe is the TR3u/TR3s flag (262144), so it is the idiomatic rendering
            channel.Modes.Should().Contain(ChannelMode.Subscribe);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL4j/attach-resume-flag-not-set-0
        [UtsFact]
        [Trait("spec", "RTL4j")]
        public async Task RTL4j_AttachResumeFlagNotSet()
        {
            // Setup
            var channelName = "test-RTL4j-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        capturedAttachMessages.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // First attach - clean attach
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Reattach while still attached (via setOptions) — not a clean attach
            var setOptionsResult = await UtsWait.WithTimeout(channel.SetOptionsAsync(new ChannelOptions(channelParams: new ChannelParams { ["rewind"] = "1" })), TimeSpan.FromSeconds(10), "SetOptionsAsync");
            setOptionsResult.IsSuccess.Should().BeTrue("AWAIT channel.setOptions() succeeds ({0})", setOptionsResult.Error?.Message);

            // Assertions
            capturedAttachMessages.Count.Should().Be(2);

            // Neither ATTACH should have the ATTACH_RESUME flag set
            (capturedAttachMessages[0].Flags.GetValueOrDefault() & 32).Should().Be(0); // ATTACH_RESUME = 32
            (capturedAttachMessages[1].Flags.GetValueOrDefault() & 32).Should().Be(0); // ATTACH_RESUME = 32
            await CloseClient(client);
        }
    }
}
