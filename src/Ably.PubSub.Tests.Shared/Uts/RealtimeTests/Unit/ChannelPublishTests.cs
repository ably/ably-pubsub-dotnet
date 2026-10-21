// RealtimeChannel Publish Tests (RTL6, RTL6a, RTL6c, RTL6c1, RTL6c2, RTL6c4, RTL6c5, RTL6i, RTL6i1, RTL6i2, RTL6i3, RTL6j, RTN7d, RTN7e, RTN19a, RTN19a2, RTN19b)
// Derived from uts/realtime/unit/channels/channel_publish.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key ValidKey (the spec's "appId.keyId:keySecret"); the mock answers CLOSE with CLOSED where a test closes the client (infra-driving); connectionStateTtl is server-sent in this SDK, so the spec's connectionStateTtl option becomes the CONNECTED message's value where the test connects first; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelPublishTests : UtsUnitTestBase
    {
        public ChannelPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // A mock that connects, answers every ATTACH with ATTACHED and records every MESSAGE.
        private MockWebSocket AttachingMock(CaptureList<ProtocolMessage> capturedMessages)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            return mockWs;
        }

        // An ACK carrying the PublishResult of the acknowledged ProtocolMessage (TR4s).
        private static JObject AckWithSerials(long msgSerial, params string[] serials)
        {
            var serialsArray = new JArray();
            foreach (var serial in serials)
            {
                serialsArray.Add(serial == null ? JValue.CreateNull() : new JValue(serial));
            }

            var ack = ProtocolMessages.AckMessage(msgSerial);
            ack["res"] = new JArray(new JObject { ["serials"] = serialsArray });
            return ack;
        }

        private static async Task AwaitReconnected(CaptureList<ConnectionState> states)
        {
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED after DISCONNECTED");
        }

        // UTS: realtime/unit/RTL6i1/publish-name-and-data-0
        [UtsFact]
        [Trait("spec", "RTL6i1")]
        public async Task RTL6i1_PublishNameAndData()
        {
            // Setup
            var channelName = "test-RTL6i1-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(capturedMessages);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var publishTask = channel.PublishAsync("greeting", "hello");

            // NOTE: the spec asserts at once; the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();

            // Assertions
            capturedMessages.Count.Should().Be(1);
            ((int)capturedMessages[0].Action).Should().Be(ProtocolMessages.Message);
            capturedMessages[0].Channel.Should().Be(channelName);
            capturedMessages[0].Messages.Should().HaveCount(1);
            capturedMessages[0].Messages[0].Name.Should().Be("greeting");
            capturedMessages[0].Messages[0].Data.Should().Be("hello");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6i2/publish-message-array-0
        [UtsFact]
        [Trait("spec", "RTL6i2")]
        public async Task RTL6i2_PublishMessageArray()
        {
            // Setup
            var channelName = "test-RTL6i2-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(capturedMessages);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var publishTask = channel.PublishAsync(new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            });

            // NOTE: the spec asserts at once; the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();

            // Assertions
            // Single ProtocolMessage
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages.Should().HaveCount(3);
            capturedMessages[0].Messages[0].Name.Should().Be("event1");
            capturedMessages[0].Messages[1].Name.Should().Be("event2");
            capturedMessages[0].Messages[2].Name.Should().Be("event3");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6i3/null-fields-json-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTL6i3", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL6i3")]
        public async Task RTL6i3_NullFieldsJson()
        {
            // Setup
            var channelName = "test-RTL6i3-json-" + Guid.NewGuid().ToString("N");
            var capturedFrames = new CaptureList<JObject>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                },
                onTextDataFrame: text =>
                {
                    var decoded = JObject.Parse(text);
                    if ((int)decoded["action"] == ProtocolMessages.Message)
                    {
                        capturedFrames.Add(decoded);
                    }
                });
            InstallMock(mockWs);

            // NOTE: useBinaryProtocol is a no-op in this SDK build (JSON only), so the spec's useBinaryProtocol: false is the default
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish with name only (null data)
            var publishTask1 = channel.PublishAsync("click", null);

            // Publish with data only (null name)
            var publishTask2 = channel.PublishAsync(null, "payload");

            // Publish with both null
            var publishTask3 = channel.PublishAsync(null, null);

            // NOTE: the spec asserts at once; the queued work is drained first so the frames have reached the mock
            await ProcessPendingEvents();

            // Assertions
            capturedFrames.Count.Should().Be(3);

            // First message: name present, data key absent
            var msg0 = (JObject)capturedFrames[0]["messages"][0];
            ((string)msg0["name"]).Should().Be("click");
            msg0.ContainsKey("data").Should().BeFalse();

            // Second message: data present, name key absent
            var msg1 = (JObject)capturedFrames[1]["messages"][0];
            msg1.ContainsKey("name").Should().BeFalse();
            ((string)msg1["data"]).Should().Be("payload");

            // Third message: both keys absent
            var msg2 = (JObject)capturedFrames[2]["messages"][0];
            msg2.ContainsKey("name").Should().BeFalse();
            msg2.ContainsKey("data").Should().BeFalse();
            await CloseClient(client);
        }

        // realtime/unit/channels/channel_publish.md (translate realtime/unit/RTL6i3/null-fields-json-0)
        // UTS-NA: realtime/unit/RTL6i3/null-fields-msgpack-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

        // UTS: realtime/unit/RTL6c1/publish-when-attached-0
        [UtsFact]
        [Trait("spec", "RTL6c1")]
        public async Task RTL6c1_PublishWhenAttached()
        {
            // Setup
            var channelName = "test-RTL6c1-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(capturedMessages);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);

            var publishTask = channel.PublishAsync("test", "immediate");

            // Assertions
            // Message should have been sent immediately (synchronously captured by mock)
            // NOTE: the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages[0].Name.Should().Be("test");
            capturedMessages[0].Messages[0].Data.Should().Be("immediate");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c1/publish-when-attaching-1
        [UtsFact]
        [Trait("spec", "RTL6c1")]
        public async Task RTL6c1_PublishWhenAttaching()
        {
            // Setup
            var channelName = "test-RTL6c1-attaching-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // Don't respond — leave channel in ATTACHING
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't complete it — channel stays ATTACHING
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            var publishTask = channel.PublishAsync("while-attaching", "data");

            // Assertions
            // Message should have been sent immediately (ATTACHING is neither SUSPENDED nor FAILED)
            // NOTE: the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages[0].Name.Should().Be("while-attaching");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c1/publish-when-initialized-2
        [UtsFact]
        [Trait("spec", "RTL6c1")]
        public async Task RTL6c1_PublishWhenInitialized()
        {
            // Setup
            var channelName = "test-RTL6c1-init-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(capturedMessages);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            var publishTask = channel.PublishAsync("before-attach", "data");

            // Assertions
            // Message should have been sent immediately
            // NOTE: the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages[0].Name.Should().Be("before-attach");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c2/queued-when-connecting-0
        [UtsFact]
        [Trait("spec", "RTL6c2")]
        public async Task RTL6c2_QueuedWhenConnecting()
        {
            // Setup
            var channelName = "test-RTL6c2-connecting-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    // Don't respond yet — leave connection in CONNECTING
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Publish while CONNECTING — should be queued
            var publishTask = channel.PublishAsync("queued", "waiting");

            // Message should NOT have been sent yet
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(0);

            // Complete the connection
            var pendingConn = await mockWs.AwaitConnectionAttempt();
            pendingConn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // Queued message should now have been sent
            // NOTE: the queued work is drained first so the queued MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages[0].Name.Should().Be("queued");
            capturedMessages[0].Messages[0].Data.Should().Be("waiting");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c2/queued-when-disconnected-1
        [UtsFact]
        [Trait("spec", "RTL6c2")]
        public async Task RTL6c2_QueuedWhenDisconnected()
        {
            // Setup
            var channelName = "test-RTL6c2-disconnected-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record state changes to verify DISCONNECTED was reached
            // NOTE: the states are recorded before the disconnect (the spec records after it), so DISCONNECTED cannot be missed
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Simulate disconnect
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: AWAIT_STATE disconnected could miss a transient state; the record is polled
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // Publish while DISCONNECTED — should be queued
            var publishTask = channel.PublishAsync("during-disconnect", "queued");

            // Message should NOT have been sent yet (no active connection)
            var messageCountBefore = capturedMessages.Count;

            // Assertions
            // After reconnection, the queued message should be sent
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection re-entered CONNECTED after DISCONNECTED");
            await ProcessPendingEvents();

            capturedMessages.Count.Should().BeGreaterThan(messageCountBefore);

            // Find the queued message in captured messages
            var queued = capturedMessages.Snapshot().Where(m => m.Messages[0].Name == "during-disconnect").ToList();
            queued.Should().HaveCount(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c2/queued-when-initialized-2
        [UtsFact]
        [Trait("spec", "RTL6c2")]
        public async Task RTL6c2_QueuedWhenInitialized()
        {
            // Setup
            var channelName = "test-RTL6c2-init-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // Publish before connecting — should be queued
            var publishTask = channel.PublishAsync("pre-connect", "early");

            // Message should NOT have been sent
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(0);

            // Now connect
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // Queued message should now have been sent
            // NOTE: the queued work is drained first so the queued MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages[0].Name.Should().Be("pre-connect");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-suspended-0
        [UtsFact]
        [Trait("spec", "RTL6c4")]
        public async Task RTL6c4_FailsConnSuspended()
        {
            // Setup
            var channelName = "test-RTL6c4-suspended-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithRefused());
            InstallMock(mockWs);

            // NOTE: connectionStateTtl is not a client option in this SDK (it is server-sent, default 120 s) and no CONNECTED is ever received here, so the loop below is long enough for the default TTL to elapse
            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();

            // Advance time until connection enters SUSPENDED
            for (var i = 0; i < 70; i++)
            {
                await AdvanceTime(2000);
                if (client.Connection.State == ConnectionState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);

            // Publish should fail
            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-closed-1
        [UtsFact]
        [Trait("spec", "RTL6c4")]
        public async Task RTL6c4_FailsConnClosed()
        {
            // Setup
            var channelName = "test-RTL6c4-closed-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            // The mock doesn't answer CLOSE
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Connection.State.Should().Be(ConnectionState.Closed);

            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c4/fails-conn-failed-2
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTL6c4")]
        public async Task RTL6c4_FailsConnFailed()
        {
            // Setup
            var channelName = "test-RTL6c4-failed-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithError(
                    new JObject
                    {
                        ["action"] = ProtocolMessages.Error,
                        ["error"] = ProtocolMessages.ErrorObject(80000, "Fatal error"),
                    },
                    thenClose: true);
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c4/fails-channel-suspended-3
        [UtsFact]
        [Trait("spec", "RTL6c4")]
        public async Task RTL6c4_FailsChannelSuspended()
        {
            // Setup
            var channelName = "test-RTL6c4-ch-suspended-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            EnableFakeTimers();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // Don't respond on second attach — will timeout to SUSPENDED
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100),
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach — will timeout and channel enters SUSPENDED
            var attachTask = channel.AttachAsync();
            await AdvanceTime(150);
            var attachResult = await UtsWait.WithTimeout(attachTask, TimeSpan.FromSeconds(10), "attachTask");
            attachResult.IsFailure.Should().BeTrue();
            var attachError = attachResult.Error;

            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Publish should fail because channel is SUSPENDED
            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();

            // No MESSAGE sent to server
            capturedMessages.Count.Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c4/fails-channel-failed-4
        [UtsFact]
        [Trait("spec", "RTL6c4")]
        public async Task RTL6c4_FailsChannelFailed()
        {
            // Setup
            var channelName = "test-RTL6c4-ch-failed-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 40160, "Not permitted", 401));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach fails → channel enters FAILED
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var attachError = attachResult.Error;
            channel.State.Should().Be(ChannelState.Failed);

            // Publish should fail because channel is FAILED
            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();

            // No MESSAGE sent to server
            capturedMessages.Count.Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c2/fails-no-queue-messages-3
        [UtsFact]
        [Trait("spec", "RTL6c2")]
        public async Task RTL6c2_FailsNoQueueMessages()
        {
            // Setup
            var channelName = "test-RTL6c2-noqueue-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Don't respond — leave connection in CONNECTING
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                QueueMessages = false,
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            Func<Task> publish = () => channel.PublishAsync("fail", "should-error");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c5/no-implicit-attach-0
        [UtsFact]
        [Trait("spec", "RTL6c5")]
        public async Task RTL6c5_NoImplicitAttach()
        {
            // Setup
            var channelName = "test-RTL6c5-" + Guid.NewGuid().ToString("N");
            var attachMessageCount = 0;
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        Interlocked.Increment(ref attachMessageCount);
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            var publishTask = channel.PublishAsync("no-attach", "test");

            // Assertions
            // Publish should have been sent (RTL6c1 — CONNECTED, channel not SUSPENDED/FAILED)
            // NOTE: the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(1);

            // Channel should remain INITIALIZED — no implicit attach
            channel.State.Should().Be(ChannelState.Initialized);
            Volatile.Read(ref attachMessageCount).Should().Be(0);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6c2/queued-messages-order-4
        [UtsFact]
        [Trait("spec", "RTL6c2")]
        public async Task RTL6c2_QueuedMessagesOrder()
        {
            // Setup
            var channelName = "test-RTL6c2-order-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    // Don't respond yet — leave in CONNECTING
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Queue multiple messages
            var publishTask1 = channel.PublishAsync("first", "1");
            var publishTask2 = channel.PublishAsync("second", "2");
            var publishTask3 = channel.PublishAsync("third", "3");

            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(0);

            // Complete the connection
            var pendingConn = await mockWs.AwaitConnectionAttempt();
            pendingConn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // All messages should have been sent in order
            // NOTE: the queued work is drained first so the queued MESSAGEs have reached the mock
            await ProcessPendingEvents();
            capturedMessages.Count.Should().Be(3);
            capturedMessages[0].Messages[0].Name.Should().Be("first");
            capturedMessages[1].Messages[0].Name.Should().Be("second");
            capturedMessages[2].Messages[0].Name.Should().Be("third");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6i1/publish-message-object-1
        [UtsFact]
        [Trait("spec", "RTL6i1")]
        public async Task RTL6i1_PublishMessageObject()
        {
            // Setup
            var channelName = "test-RTL6i1-obj-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            var mockWs = AttachingMock(capturedMessages);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var publishTask = channel.PublishAsync(new Message("custom", new JObject { ["key"] = "value" }));

            // NOTE: the spec asserts at once; the queued work is drained first so the MESSAGE has reached the mock
            await ProcessPendingEvents();

            // Assertions
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages.Should().HaveCount(1);
            capturedMessages[0].Messages[0].Name.Should().Be("custom");
            // NOTE: captured_messages holds the frame as sent (wire form): RSL4d3 sends a JSON object payload as a JSON string with encoding "json", so the data is parsed before the comparison
            JToken.DeepEquals(JToken.Parse(capturedMessages[0].Messages[0].Data.ToString()), new JObject { ["key"] = "value" }).Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6j/publish-result-serials-0
        [UtsFact]
        [Trait("spec", "RTL6j")]
        public async Task RTL6j_PublishResultSerials()
        {
            // Setup
            var channelName = "test-RTL6j-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);

                        // Respond with ACK containing PublishResult with serials
                        mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "abc123"));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var result = await UtsWait.WithTimeout(channel.PublishAsync("greeting", "hello"), TimeSpan.FromSeconds(10), "PublishAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result.Error?.Message);

            // Assertions
            // Publish should have been sent with msgSerial
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].MsgSerial.Should().Be(0);

            // Result should be a PublishResult with serials from the ACK
            result.Value.Should().BeAssignableTo<PublishResult>();
            result.Value.Serials.Should().HaveCount(1);
            result.Value.Serials[0].Should().Be("abc123");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6j/batch-publish-serials-1
        [UtsFact]
        [Trait("spec", "RTL6j")]
        public async Task RTL6j_BatchPublishSerials()
        {
            // Setup
            var channelName = "test-RTL6j-batch-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);

                        // Respond with ACK containing serials for each message
                        mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-1", null, "serial-3"));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var publishTask4 = channel.PublishAsync(new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            });
            var result = await UtsWait.WithTimeout(publishTask4, TimeSpan.FromSeconds(10), "PublishAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result.Error?.Message);

            // Assertions
            // Single ProtocolMessage with 3 messages
            capturedMessages.Count.Should().Be(1);
            capturedMessages[0].Messages.Should().HaveCount(3);

            // Result should contain serials 1:1 with published messages
            result.Value.Should().BeAssignableTo<PublishResult>();
            result.Value.Serials.Should().HaveCount(3);
            result.Value.Serials[0].Should().Be("serial-1");

            // Conflated message
            result.Value.Serials[1].Should().BeNull();
            result.Value.Serials[2].Should().Be("serial-3");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6j/incrementing-msg-serial-2
        [UtsFact]
        [Trait("spec", "RTL6j")]
        public async Task RTL6j_IncrementingMsgSerial()
        {
            // Setup
            var channelName = "test-RTL6j-serial-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);

                        // Respond with ACK, using msgSerial to generate distinct serials
                        mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-" + msg.MsgSerial));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var result1 = await UtsWait.WithTimeout(channel.PublishAsync("first", "1"), TimeSpan.FromSeconds(10), "PublishAsync");
            result1.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result1.Error?.Message);
            var result2 = await UtsWait.WithTimeout(channel.PublishAsync("second", "2"), TimeSpan.FromSeconds(10), "PublishAsync");
            result2.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result2.Error?.Message);
            var result3 = await UtsWait.WithTimeout(channel.PublishAsync("third", "3"), TimeSpan.FromSeconds(10), "PublishAsync");
            result3.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result3.Error?.Message);

            // Assertions
            // Each outgoing MESSAGE should have incrementing msgSerial
            capturedMessages.Count.Should().Be(3);
            capturedMessages[0].MsgSerial.Should().Be(0);
            capturedMessages[1].MsgSerial.Should().Be(1);
            capturedMessages[2].MsgSerial.Should().Be(2);

            // Each publish should resolve with the correct PublishResult
            result1.Value.Serials[0].Should().Be("serial-0");
            result2.Value.Serials[0].Should().Be("serial-1");
            result3.Value.Serials[0].Should().Be("serial-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL6j/nack-results-error-3
        [UtsFact]
        [Trait("spec", "RTL6j")]
        public async Task RTL6j_NackResultsError()
        {
            // Setup
            var channelName = "test-RTL6j-nack-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Respond with NACK
                        mockWs.SendToClient(ProtocolMessages.NackMessage(msg.MsgSerial, 40160, "Publish rejected", 401));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var result = await UtsWait.WithTimeout(channel.PublishAsync("rejected", "data"), TimeSpan.FromSeconds(10), "PublishAsync");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            error.Should().NotBeNull();
            error.Code.Should().Be(40160);
            error.Message.Should().Be("Publish rejected");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7e/pending-fail-suspended-0
        [UtsFact]
        [Trait("spec", "RTN7e")]
        public async Task RTN7e_PendingFailSuspended()
        {
            // Setup
            var channelName = "test-RTN7e-suspended-" + Guid.NewGuid().ToString("N");

            EnableFakeTimers();

            MockWebSocket mockWs = null;
            // NOTE: connectionStateTtl is server-sent in this SDK, so the spec's connectionStateTtl: 5000 option is carried by CONNECTED
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(connectionStateTtl: 5000)),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave message pending
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(1000),
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish but don't ACK — message stays pending
            var publishTask = channel.PublishAsync("pending", "data");

            // Disconnect and refuse all reconnection attempts so connection enters SUSPENDED
            // NOTE: the spec installs a second MockWebSocket mid-test; the infra injects a mock at client construction, so the same mock's connection handler is replaced and its active connection is disconnected
            mockWs.OnConnectionAttempt = conn => conn.RespondWithRefused();

            mockWs.ActiveConnection.SimulateDisconnect();

            // Advance time until connection enters SUSPENDED
            for (var i = 0; i < 15; i++)
            {
                await AdvanceTime(2000);
                if (client.Connection.State == ConnectionState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);

            // The pending publish should now fail
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7e/pending-fail-closed-1
        [UtsFact]
        [Trait("spec", "RTN7e")]
        public async Task RTN7e_PendingFailClosed()
        {
            // Setup
            var channelName = "test-RTN7e-closed-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave message pending
                    }
                });

            // The mock doesn't answer CLOSE
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish but don't ACK — message stays pending
            var publishTask = channel.PublishAsync("pending", "data");

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            client.Connection.State.Should().Be(ConnectionState.Closed);

            // The pending publish should now fail
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7e/pending-fail-failed-2
        [UtsFact]
        [Trait("spec", "RTN7e")]
        public async Task RTN7e_PendingFailFailed()
        {
            // Setup
            var channelName = "test-RTN7e-failed-" + Guid.NewGuid().ToString("N");
            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref connectionCount) == 1)
                    {
                        conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                    }
                    else
                    {
                        // Fatal error on reconnection attempt
                        conn.RespondWithSuccess();
                    }
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave message pending
                        // Send a fatal error to force FAILED state
                        mockWs.SendToClientAndClose(new JObject
                        {
                            ["action"] = ProtocolMessages.Error,
                            ["error"] = ProtocolMessages.ErrorObject(80000, "Fatal error"),
                        });
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish — server responds with fatal ERROR instead of ACK
            var publishTask = channel.PublishAsync("pending", "data");

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // The pending publish should now fail
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7e/multiple-pending-fail-3
        [UtsFact]
        [Trait("spec", "RTN7e")]
        public async Task RTN7e_MultiplePendingFail()
        {
            // Setup
            var channelName = "test-RTN7e-multi-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave all messages pending
                    }
                });

            // The mock doesn't answer CLOSE
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish multiple messages, none will be ACK'd
            var future1 = channel.PublishAsync("msg1", "data1");
            var future2 = channel.PublishAsync("msg2", "data2");
            var future3 = channel.PublishAsync("msg3", "data3");

            // Close the connection
            client.Close();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // All pending publishes should fail
            var result1 = await UtsWait.WithTimeout(future1, TimeSpan.FromSeconds(10), "future1");
            result1.IsFailure.Should().BeTrue();
            var error1 = result1.Error;
            var result2 = await UtsWait.WithTimeout(future2, TimeSpan.FromSeconds(10), "future2");
            result2.IsFailure.Should().BeTrue();
            var error2 = result2.Error;
            var result3 = await UtsWait.WithTimeout(future3, TimeSpan.FromSeconds(10), "future3");
            result3.IsFailure.Should().BeTrue();
            var error3 = result3.Error;

            // Assertions
            error1.Should().NotBeNull();
            error2.Should().NotBeNull();
            error3.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7e/error-represents-reason-4
        // DEVIATION: see deviations.md
        [DeviationFact("RTN27h", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN7e")]
        public async Task RTN7e_ErrorRepresentsReason()
        {
            // Setup
            var channelName = "test-RTN7e-error-reason-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave message pending
                        // Send a fatal error to force FAILED state with a specific reason
                        mockWs.SendToClientAndClose(ProtocolMessages.ErrorMessage(80019, "Connection closed due to admin action", 400));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish — server responds with fatal ERROR instead of ACK
            var publishTask = channel.PublishAsync("pending", "data");

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Failed);

            // The pending publish should fail with the same error that caused the state change
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            // The error should represent the reason for the state change
            error.Should().NotBeNull();
            error.Code.Should().Be(80019);
            ((int?)error.StatusCode).Should().Be(400);
            error.Message.Should().Be("Connection closed due to admin action");

            // Verify the connection's errorReason matches
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80019);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7d/fail-disconnected-no-queue-0
        [UtsFact]
        [Trait("spec", "RTN7d")]
        public async Task RTN7d_FailDisconnectedNoQueue()
        {
            // Setup
            var channelName = "test-RTN7d-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        // Do NOT send ACK — leave message pending
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                QueueMessages = false,
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish but don't ACK — message stays pending
            var publishTask = channel.PublishAsync("pending", "data");

            // Record state changes to verify DISCONNECTED was reached
            // NOTE: the states are recorded before the disconnect (the spec records after it)
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Disconnect — triggers DISCONNECTED state
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: AWAIT_STATE disconnected could miss a transient state; the record is polled
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().Any(s => s == ConnectionState.Disconnected),
                description: "connection entered DISCONNECTED");

            // The pending publish should fail immediately on DISCONNECTED
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsFailure.Should().BeTrue();
            var error = result.Error;

            // Assertions
            error.Should().NotBeNull();
            // ASSERT error.code IS NOT null: ErrorInfo.Code is an int, so the compile-time type guarantees it
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN7d/survive-disconnected-queue-1
        [UtsFact]
        [Trait("spec", "RTN7d")]
        public async Task RTN7d_SurviveDisconnectedQueue()
        {
            // Setup
            var channelName = "test-RTN7d-default-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(msg);
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            // ACK on reconnection
                            mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-ack"));
                        }

                        // First connection: do NOT ACK — leave pending
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish but don't ACK — message stays pending
            var publishTask = channel.PublishAsync("pending", "data");

            // Disconnect
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();

            // Reconnect
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            // The publish should eventually succeed (resent on new transport, then ACK'd)
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result.Error?.Message);

            // Assertions
            result.Value.Should().BeAssignableTo<PublishResult>();
            result.Value.Serials[0].Should().Be("serial-ack");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN19a/resent-on-new-transport-0
        [UtsFact]
        [Trait("spec", "RTN19a")]
        public async Task RTN19a_ResentOnNewTransport()
        {
            // Setup
            var channelName = "test-RTN19a-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<Tuple<ProtocolMessage, int>>();

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(Tuple.Create(msg, Volatile.Read(ref connectionCount)));
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            // ACK on second connection
                            mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-resent"));
                        }

                        // First connection: do NOT ACK
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish — will be sent on first transport, no ACK received
            var publishTask = channel.PublishAsync("resend-me", "data");

            // Verify message was sent on first transport
            await ProcessPendingEvents();
            var firstTransportMessages = capturedMessages.Snapshot().Where(m => m.Item2 == 1 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            firstTransportMessages.Should().HaveCount(1);

            // Disconnect
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();

            // Reconnect
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            // The publish should succeed (resent and ACK'd on new transport)
            var result = await UtsWait.WithTimeout(publishTask, TimeSpan.FromSeconds(10), "publishTask");
            result.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result.Error?.Message);

            // Assertions
            // Message should have been sent on both transports
            var secondTransportMessages = capturedMessages.Snapshot().Where(m => m.Item2 == 2 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            secondTransportMessages.Count.Should().BeGreaterThanOrEqualTo(1);

            // The resent message should have the same content
            secondTransportMessages[0].Item1.Messages[0].Name.Should().Be("resend-me");

            // Publish should have resolved successfully
            result.Value.Should().BeAssignableTo<PublishResult>();
            result.Value.Serials[0].Should().Be("serial-resent");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN19a2/same-serial-on-resume-0
        [UtsFact]
        [Trait("spec", "RTN19a2")]
        public async Task RTN19a2_SameSerialOnResume()
        {
            // Setup
            var channelName = "test-RTN19a2-resume-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<Tuple<ProtocolMessage, int>>();
            var originalConnectionId = "connection-abc";

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    var count = Interlocked.Increment(ref connectionCount);

                    // Both connections use the same connectionId = successful resume (RTN15c6)
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(originalConnectionId, "key-" + count));
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(Tuple.Create(msg, Volatile.Read(ref connectionCount)));
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-resumed"));
                        }
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish two messages — neither will be ACK'd
            var future1 = channel.PublishAsync("msg1", "data1");
            var future2 = channel.PublishAsync("msg2", "data2");

            // Capture original msgSerials
            await ProcessPendingEvents();
            var firstTransportMsgs = capturedMessages.Snapshot().Where(m => m.Item2 == 1 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            var originalSerial1 = firstTransportMsgs[0].Item1.MsgSerial;
            var originalSerial2 = firstTransportMsgs[1].Item1.MsgSerial;

            // Disconnect and reconnect (successful resume — same connectionId)
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            var result1 = await UtsWait.WithTimeout(future1, TimeSpan.FromSeconds(10), "future1");
            result1.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result1.Error?.Message);
            var result2 = await UtsWait.WithTimeout(future2, TimeSpan.FromSeconds(10), "future2");
            result2.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result2.Error?.Message);

            // Assertions
            // Messages resent on second transport should have the SAME msgSerials
            var secondTransportMsgs = capturedMessages.Snapshot().Where(m => m.Item2 == 2 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            secondTransportMsgs.Should().HaveCount(2);
            secondTransportMsgs[0].Item1.MsgSerial.Should().Be(originalSerial1);
            secondTransportMsgs[1].Item1.MsgSerial.Should().Be(originalSerial2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN19a2/new-serial-failed-resume-1
        [UtsFact]
        [Trait("spec", "RTN19a2")]
        public async Task RTN19a2_NewSerialFailedResume()
        {
            // Setup
            var channelName = "test-RTN19a2-failed-resume-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<Tuple<ProtocolMessage, int>>();

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref connectionCount) == 1)
                    {
                        conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-first", "key-first"));
                    }
                    else
                    {
                        // Failed resume — different connectionId + error (RTN15c7)
                        var connected = ProtocolMessages.ConnectedMessage("connection-new", "key-new");
                        connected["error"] = ProtocolMessages.ErrorObject(80018, "Connection not resumable");
                        conn.RespondWithSuccess(connected);
                    }
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Message)
                    {
                        capturedMessages.Add(Tuple.Create(msg, Volatile.Read(ref connectionCount)));
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            mockWs.SendToClient(AckWithSerials(msg.MsgSerial, "serial-new"));
                        }
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Publish two messages with msgSerials 0 and 1 — neither will be ACK'd
            var future1 = channel.PublishAsync("msg1", "data1");
            var future2 = channel.PublishAsync("msg2", "data2");

            // Verify original serials
            await ProcessPendingEvents();
            var firstTransportMsgs = capturedMessages.Snapshot().Where(m => m.Item2 == 1 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            firstTransportMsgs[0].Item1.MsgSerial.Should().Be(0);
            firstTransportMsgs[1].Item1.MsgSerial.Should().Be(1);

            // Disconnect and reconnect (failed resume — different connectionId + error)
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            var result1 = await UtsWait.WithTimeout(future1, TimeSpan.FromSeconds(10), "future1");
            result1.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result1.Error?.Message);
            var result2 = await UtsWait.WithTimeout(future2, TimeSpan.FromSeconds(10), "future2");
            result2.IsSuccess.Should().BeTrue("AWAIT channel.publish() succeeds ({0})", result2.Error?.Message);

            // Assertions
            // Messages resent on second transport should have NEW msgSerials starting from 0
            // (RTN15c7 resets the internal msgSerial counter)
            var secondTransportMsgs = capturedMessages.Snapshot().Where(m => m.Item2 == 2 && (int)m.Item1.Action == ProtocolMessages.Message).ToList();
            secondTransportMsgs.Should().HaveCount(2);
            secondTransportMsgs[0].Item1.MsgSerial.Should().Be(0);
            secondTransportMsgs[1].Item1.MsgSerial.Should().Be(1);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN19b/attach-resent-on-reconnect-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN19b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN19b")]
        public async Task RTN19b_AttachResentOnReconnect()
        {
            // Setup
            var channelName = "test-RTN19b-attach-" + Guid.NewGuid().ToString("N");
            var capturedAttachMessages = new CaptureList<Tuple<ProtocolMessage, int>>();

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        capturedAttachMessages.Add(Tuple.Create(msg, Volatile.Read(ref connectionCount)));
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            // Respond with ATTACHED on second connection
                            mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                        }

                        // First connection: don't respond — leave channel ATTACHING
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach but don't respond — channel stays ATTACHING
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Verify ATTACH was sent on first transport
            await ProcessPendingEvents();
            var firstTransportAttaches = capturedAttachMessages.Snapshot().Where(m => m.Item2 == 1).ToList();
            firstTransportAttaches.Should().HaveCount(1);
            firstTransportAttaches[0].Item1.Channel.Should().Be(channelName);

            // Disconnect and reconnect
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            // Attach should complete (ATTACH resent and responded to on new transport)
            var attachResult = await UtsWait.WithTimeout(attachTask, TimeSpan.FromSeconds(10), "attachTask");
            attachResult.IsSuccess.Should().BeTrue("AWAIT attach_future succeeds ({0})", attachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            // ATTACH should have been resent on second transport
            var secondTransportAttaches = capturedAttachMessages.Snapshot().Where(m => m.Item2 == 2).ToList();
            secondTransportAttaches.Count.Should().BeGreaterThanOrEqualTo(1);
            secondTransportAttaches[0].Item1.Channel.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN19b/detach-resent-on-reconnect-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTN19b", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN19b")]
        public async Task RTN19b_DetachResentOnReconnect()
        {
            // Setup
            var channelName = "test-RTN19b-detach-" + Guid.NewGuid().ToString("N");
            var capturedDetachMessages = new CaptureList<Tuple<ProtocolMessage, int>>();

            EnableFakeTimers();

            var connectionCount = 0;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    Interlocked.Increment(ref connectionCount);
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        capturedDetachMessages.Add(Tuple.Create(msg, Volatile.Read(ref connectionCount)));
                        if (Volatile.Read(ref connectionCount) >= 2)
                        {
                            // Respond with DETACHED on second connection
                            mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                        }

                        // First connection: don't respond — leave channel DETACHING
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Start detach but don't respond — channel stays DETACHING
            var detachTask = channel.DetachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Detaching);

            // Verify DETACH was sent on first transport
            await ProcessPendingEvents();
            var firstTransportDetaches = capturedDetachMessages.Snapshot().Where(m => m.Item2 == 1).ToList();
            firstTransportDetaches.Should().HaveCount(1);

            // Disconnect and reconnect
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the states are recorded before the disconnect and the re-entry is polled
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.SimulateDisconnect();
            await AdvanceTime(2000);
            await AwaitReconnected(states);

            // Detach should complete (DETACH resent and responded to on new transport)
            var detachResult = await UtsWait.WithTimeout(detachTask, TimeSpan.FromSeconds(10), "detachTask");
            detachResult.IsSuccess.Should().BeTrue("AWAIT detach_future succeeds ({0})", detachResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Detached);

            // DETACH should have been resent on second transport
            var secondTransportDetaches = capturedDetachMessages.Snapshot().Where(m => m.Item2 == 2).ToList();
            secondTransportDetaches.Count.Should().BeGreaterThanOrEqualTo(1);
            secondTransportDetaches[0].Item1.Channel.Should().Be(channelName);
            await CloseClient(client);
        }
    }
}
