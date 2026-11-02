// RealtimePresence Enter/Update/Leave Tests (RTP4, RTP8, RTP9, RTP10, RTP14, RTP15, RTP16)
// Derived from uts/realtime/unit/presence/realtime_presence_enter.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key stand-in "fake.key:secret" as the spec sets it; ClientOptions.ClientId rejects the wildcard "*" (RSA7c; spec top note), so tests that use clientId "*" only for enterClient use key auth with no clientId (the spec's alternative auth pattern); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class RealtimePresenceEnterTests : UtsUnitTestBase
    {
        public RealtimePresenceEnterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's common handler: ATTACH is answered with ATTACHED, every PRESENCE is captured and ACKed.
        private static MockWebSocket PresenceAckingMock(
            string channelName,
            CaptureList<ProtocolMessage> capturedPresence,
            string connectionId = null,
            bool hasPresence = false)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess(connectionId == null ? ProtocolMessages.ConnectedMessage() : ProtocolMessages.ConnectedMessage(connectionId));
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(hasPresence ? ProtocolMessages.AttachedWithPresenceMessage(channelName) : ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        capturedPresence?.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            return mockWs;
        }

        // The spec's NOW() at unit: the fake clock when one is enabled, else the real clock.
        private long NowMillis() => Clock != null ? Clock.NowMillis : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // UTS: realtime/unit/RTP8a/enter-sends-presence-enter-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP8c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP8a")]
        public async Task RTP8a_EnterSendsPresenceEnter()
        {
            // Setup
            var channelName = "test-RTP8a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence, "conn-1");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Action.Should().Be(ProtocolMessage.MessageAction.Presence);
            capturedPresence[0].Channel.Should().Be(channelName);
            capturedPresence[0].Presence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);

            // RTP8c: clientId must NOT be present in the PresenceMessage
            capturedPresence[0].Presence[0].ClientId.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP8e/enter-with-data-0
        [UtsFact]
        [Trait("spec", "RTP8e")]
        public async Task RTP8e_EnterWithData()
        {
            // Setup
            var channelName = "test-RTP8e-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence, "conn-1");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync("hello world"), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            ((string)capturedPresence[0].Presence[0].Data).Should().Be("hello world");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP8d/enter-implicitly-attaches-0
        [UtsFact]
        [Trait("spec", "RTP8d")]
        public async Task RTP8d_EnterImplicitlyAttaches()
        {
            // Setup
            var channelName = "test-RTP8d-" + Guid.NewGuid().ToString("N");

            var mockWs = PresenceAckingMock(channelName, null, "conn-1");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            // enter() on INITIALIZED channel triggers implicit attach
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP8g/enter-detached-failed-errors-0
        [UtsFact]
        [Trait("spec", "RTP8g")]
        public async Task RTP8g_EnterDetachedFailedErrors()
        {
            // Setup
            var channelName = "test-RTP8g-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Respond with error to put channel in FAILED state
                        mockWs.SendToClient(ProtocolMessages.ChannelErrorMessage(channelName, 90001, "Channel failed"));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Put channel into FAILED state
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();
            var attachError = attachResult.Error;
            channel.State.Should().Be(ChannelState.Failed);

            // enter() on FAILED channel should error immediately
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;

            // Assertions
            error.Should().NotBeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP8j/enter-null-clientid-errors-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP8j", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP8j")]
        public async Task RTP8j_EnterNullClientidErrors()
        {
            // Setup
            var channelName = "test-RTP8j-" + Guid.NewGuid().ToString("N");

            var mockWs = PresenceAckingMock(channelName, null);
            InstallMock(mockWs);

            // No clientId — anonymous client
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // enter() without clientId should error
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;

            // Assertions
            error.Should().NotBeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP8j/enter-wildcard-clientid-errors-1
        [Fact]
        [Trait("spec", "RTP8j")]
        public void RTP8j_EnterWildcardClientidErrors()
        {
            // Setup
            // Note: Some SDKs may reject wildcard clientId "*" at the ClientOptions
            // construction level rather than at enter() time. In that case, this test
            // validates that the error occurs at ClientOptions creation instead.
            var channelName = "test-RTP8j-wild-" + Guid.NewGuid().ToString("N");

            var mockWs = PresenceAckingMock(channelName, null);
            InstallMock(mockWs);

            // Wildcard clientId
            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" at ClientOptions creation (InvalidOperationException), so the error is asserted there, as the spec's setup note allows; the steps that follow cannot run.
            Action create = () => Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "*", AutoConnect = false });

            // Assertions
            create.Should().Throw<InvalidOperationException>();

            // Test Steps (not reachable: the client cannot be created with a wildcard clientId)
            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            // AWAIT channel.presence.enter() FAILS WITH error
            // ASSERT error IS NOT null
        }

        // UTS: realtime/unit/RTP8h/nack-presence-permission-denied-0
        [UtsFact]
        [Trait("spec", "RTP8h")]
        public async Task RTP8h_NackPresencePermissionDenied()
        {
            // Setup
            var channelName = "test-RTP8h-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        mockWs.SendToClient(ProtocolMessages.NackMessage(msg.MsgSerial, 40160, "Presence permission denied", 401, 1));
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

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;

            // Assertions
            error.Should().NotBeNull();
            error.Code.Should().Be(40160);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP9a/update-sends-presence-update-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP9d", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP9a")]
        public async Task RTP9a_UpdateSendsPresenceUpdate()
        {
            // Setup
            var channelName = "test-RTP9a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence, "conn-1");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var updateResult = await UtsWait.WithTimeout(channel.Presence.UpdateAsync("new-status"), TimeSpan.FromSeconds(10), "UpdateAsync");
            updateResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.update() succeeds ({0})", updateResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Update);
            ((string)capturedPresence[0].Presence[0].Data).Should().Be("new-status");

            // RTP9d
            capturedPresence[0].Presence[0].ClientId.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP10a/leave-sends-presence-leave-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTP10c", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTP10a")]
        public async Task RTP10a_LeaveSendsPresenceLeave()
        {
            // Setup
            var channelName = "test-RTP10a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence, "conn-1");
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var leaveResult = await UtsWait.WithTimeout(channel.Presence.LeaveAsync(), TimeSpan.FromSeconds(10), "LeaveAsync");
            leaveResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.leave() succeeds ({0})", leaveResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Leave);

            // RTP10c
            capturedPresence[0].Presence[0].ClientId.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP10a/leave-with-data-1
        [UtsFact]
        [Trait("spec", "RTP10a")]
        public async Task RTP10a_LeaveWithData()
        {
            // Setup
            var channelName = "test-RTP10a-data-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var leaveResult = await UtsWait.WithTimeout(channel.Presence.LeaveAsync("goodbye"), TimeSpan.FromSeconds(10), "LeaveAsync");
            leaveResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.leave() succeeds ({0})", leaveResult.Error?.Message);

            // Assertions
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Leave);
            ((string)capturedPresence[0].Presence[0].Data).Should().Be("goodbye");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP14a/enterclient-on-behalf-0
        [UtsFact]
        [Trait("spec", "RTP14a")]
        public async Task RTP14a_EnterclientOnBehalf()
        {
            // Setup
            var channelName = "test-RTP14a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence);
            InstallMock(mockWs);

            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" in ClientOptions (RSA7c), so the client uses key auth with no clientId, the alternative the spec's notes give: an unidentified client may enterClient any clientId (RTP15a, RTP15f)
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var aliceResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync("user-alice", "alice-data"), TimeSpan.FromSeconds(10), "EnterClientAsync");
            aliceResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enterClient() succeeds ({0})", aliceResult.Error?.Message);
            var bobResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync("user-bob", "bob-data"), TimeSpan.FromSeconds(10), "EnterClientAsync");
            bobResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enterClient() succeeds ({0})", bobResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(2);

            // First enter: user-alice
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            capturedPresence[0].Presence[0].ClientId.Should().Be("user-alice");
            ((string)capturedPresence[0].Presence[0].Data).Should().Be("alice-data");

            // Second enter: user-bob
            capturedPresence[1].Presence[0].Action.Should().Be(PresenceAction.Enter);
            capturedPresence[1].Presence[0].ClientId.Should().Be("user-bob");
            ((string)capturedPresence[1].Presence[0].Data).Should().Be("bob-data");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP15a/updateclient-leaveclient-0
        [UtsFact]
        [Trait("spec", "RTP15a")]
        public async Task RTP15a_UpdateclientLeaveclient()
        {
            // Setup
            var channelName = "test-RTP15a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence);
            InstallMock(mockWs);

            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" in ClientOptions (RSA7c), so the client uses key auth with no clientId, the alternative the spec's notes give: an unidentified client may enterClient any clientId (RTP15a, RTP15f)
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync("user-1", "entered"), TimeSpan.FromSeconds(10), "EnterClientAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enterClient() succeeds ({0})", enterResult.Error?.Message);
            var updateResult = await UtsWait.WithTimeout(channel.Presence.UpdateClientAsync("user-1", "updated"), TimeSpan.FromSeconds(10), "UpdateClientAsync");
            updateResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.updateClient() succeeds ({0})", updateResult.Error?.Message);
            var leaveResult = await UtsWait.WithTimeout(channel.Presence.LeaveClientAsync("user-1", "leaving"), TimeSpan.FromSeconds(10), "LeaveClientAsync");
            leaveResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.leaveClient() succeeds ({0})", leaveResult.Error?.Message);

            // Assertions
            capturedPresence.Should().HaveCount(3);

            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);
            capturedPresence[0].Presence[0].ClientId.Should().Be("user-1");
            ((string)capturedPresence[0].Presence[0].Data).Should().Be("entered");

            capturedPresence[1].Presence[0].Action.Should().Be(PresenceAction.Update);
            capturedPresence[1].Presence[0].ClientId.Should().Be("user-1");
            ((string)capturedPresence[1].Presence[0].Data).Should().Be("updated");

            capturedPresence[2].Presence[0].Action.Should().Be(PresenceAction.Leave);
            capturedPresence[2].Presence[0].ClientId.Should().Be("user-1");
            ((string)capturedPresence[2].Presence[0].Data).Should().Be("leaving");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP15e/enterclient-implicitly-attaches-0
        [UtsFact]
        [Trait("spec", "RTP15e")]
        public async Task RTP15e_EnterclientImplicitlyAttaches()
        {
            // Setup
            var channelName = "test-RTP15e-" + Guid.NewGuid().ToString("N");

            var mockWs = PresenceAckingMock(channelName, null);
            InstallMock(mockWs);

            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" in ClientOptions (RSA7c), so the client uses key auth with no clientId, the alternative the spec's notes give: an unidentified client may enterClient any clientId (RTP15a, RTP15f)
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync("user-1", null), TimeSpan.FromSeconds(10), "EnterClientAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enterClient() succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP15f/enterclient-mismatched-clientid-0
        [UtsFact]
        [Trait("spec", "RTP15f")]
        public async Task RTP15f_EnterclientMismatchedClientid()
        {
            // Setup
            var channelName = "test-RTP15f-" + Guid.NewGuid().ToString("N");

            var mockWs = PresenceAckingMock(channelName, null);
            InstallMock(mockWs);

            // Client has a specific (non-wildcard) clientId
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // enterClient with a different clientId than the connection's clientId
            // NOTE: the spec's implementation note allows either a client-side error or a server NACK; the mock here ACKs every PRESENCE, so only a client-side check produces an error
            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync("other-client", null), TimeSpan.FromSeconds(10), "EnterClientAsync");
            enterResult.IsFailure.Should().BeTrue();
            var error = enterResult.Error;

            // Assertions
            error.Should().NotBeNull();

            // Connection and channel remain available
            client.Connection.State.Should().Be(ConnectionState.Connected);
            channel.State.Should().Be(ChannelState.Attached);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP16a/presence-sent-when-attached-0
        [UtsFact]
        [Trait("spec", "RTP16a")]
        public async Task RTP16a_PresenceSentWhenAttached()
        {
            // Setup
            var channelName = "test-RTP16a-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            var mockWs = PresenceAckingMock(channelName, capturedPresence);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", ClientId = "my-client", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterAsync(), TimeSpan.FromSeconds(10), "EnterAsync");
            enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enter() succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            // Message was sent immediately
            capturedPresence.Should().HaveCount(1);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP16b/presence-queued-when-attaching-0
        [UtsFact]
        [Trait("spec", "RTP16b")]
        public async Task RTP16b_PresenceQueuedWhenAttaching()
        {
            // Setup
            var channelName = "test-RTP16b-" + Guid.NewGuid().ToString("N");

            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Delay the ATTACHED response
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
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

            // Start attach but don't complete it
            var attachTask = channel.AttachAsync();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Queue presence while ATTACHING
            var enterTask = channel.Presence.EnterAsync();

            // No messages sent yet
            await ProcessPendingEvents();
            capturedPresence.Should().HaveCount(0);

            // Now complete the attach
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName));

            var enterResult = await UtsWait.WithTimeout(enterTask, TimeSpan.FromSeconds(10), "enterTask");
            enterResult.IsSuccess.Should().BeTrue("AWAIT enter_future succeeds ({0})", enterResult.Error?.Message);

            // Assertions
            // Queued presence message was sent after attach completed
            capturedPresence.Should().HaveCount(1);
            capturedPresence[0].Presence[0].Action.Should().Be(PresenceAction.Enter);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP16c/presence-errors-other-states-0
        [UtsFact(Skip = "UTS spec error RTP16c: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTP16c")]
        public Task RTP16c_PresenceErrorsOtherStates()
        {
            // SPEC ERROR RTP16c: the setup answers ATTACH with a server DETACHED and asserts the channel is DETACHED, but features RTL13b
            // says a DETACHED received while ATTACHING sends the channel to SUSPENDED. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTP16c - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTP15c/enterclient-no-side-effects-0
        [UtsFact(Skip = "UTS spec error RTP15c: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTP15c")]
        public Task RTP15c_EnterclientNoSideEffects()
        {
            // SPEC ERROR RTP15c: the test expects enter() to succeed on a client whose clientId is the wildcard "*", but features RTP8j says
            // enter() with a "*" (or null) clientId results in an error immediately, and RSA7c forbids "*" in ClientOptions.
            // Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTP15c - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTP4/bulk-enterclient-same-connection-0
        [UtsFact]
        [Trait("spec", "RTP4")]
        public async Task RTP4_BulkEnterclientSameConnection()
        {
            // Setup
            var channelName = "test-RTP4-same-" + Guid.NewGuid().ToString("N");
            var memberCount = 50;

            var capturedPresence = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        capturedPresence.Add(msg);
                        mockWs.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));

                        // Server echoes back the ENTER as a PRESENCE event
                        for (var idx = 0; idx < msg.Presence.Length; idx++)
                        {
                            var p = msg.Presence[idx];
                            mockWs.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                                channelName,
                                new JArray(ProtocolMessages.PresenceEntry(
                                    (int)PresenceAction.Enter,
                                    p.ClientId,
                                    "conn-1",
                                    $"conn-1:{msg.MsgSerial}:{idx}",
                                    NowMillis(),
                                    p.Data))));
                        }
                    }
                });
            InstallMock(mockWs);

            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" in ClientOptions (RSA7c), so the client uses key auth with no clientId, the alternative the spec's notes give: an unidentified client may enterClient any clientId (RTP15a, RTP15f)
            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Track ENTER events received by subscriber
            var receivedEnters = new CaptureList<PresenceMessage>();
            channel.Presence.Subscribe(PresenceAction.Enter, message => receivedEnters.Add(message));

            // Enter 50 members
            for (var i = 0; i <= memberCount - 1; i++)
            {
                var enterResult = await UtsWait.WithTimeout(channel.Presence.EnterClientAsync($"user-{i}", $"data-{i}"), TimeSpan.FromSeconds(10), "EnterClientAsync");
                enterResult.IsSuccess.Should().BeTrue("AWAIT channel.presence.enterClient() succeeds ({0})", enterResult.Error?.Message);
            }

            // Send a complete SYNC with all 50 members as PRESENT
            var syncMembers = new JArray();
            for (var i = 0; i <= memberCount - 1; i++)
            {
                syncMembers.Add(ProtocolMessages.PresenceEntry(
                    (int)PresenceAction.Present,
                    $"user-{i}",
                    "conn-1",
                    $"conn-1:{i}:0",
                    NowMillis(),
                    $"data-{i}"));
            }

            mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", syncMembers));

            // Get all members after sync
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // All 50 members entered
            capturedPresence.Should().HaveCount(memberCount);

            // All 50 ENTER events received by subscriber
            await UtsWait.PollUntil(() => receivedEnters.Count == memberCount, description: "all ENTER events delivered");
            receivedEnters.Should().HaveCount(memberCount);

            // All 50 members present after sync
            members.Should().HaveCount(memberCount);

            // Verify each member exists with correct data
            for (var i = 0; i <= memberCount - 1; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == $"user-{i}");
                member.Should().NotBeNull();
                ((string)member.Data).Should().Be($"data-{i}");
            }

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP4/bulk-enterclient-diff-connections-1
        [UtsFact]
        [Trait("spec", "RTP4")]
        public async Task RTP4_BulkEnterclientDiffConnections()
        {
            // Setup
            var channelName = "test-RTP4-diff-" + Guid.NewGuid().ToString("N");
            var memberCount = 50;

            // --- Connection A: the entering client ---
            var capturedPresenceA = new CaptureList<ProtocolMessage>();
            MockWebSocket mockWsA = null;
            mockWsA = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-A")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWsA.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Presence)
                    {
                        capturedPresenceA.Add(msg);
                        mockWsA.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });

            // --- Connection B: the observing client ---
            MockWebSocket mockWsB = null;
            mockWsB = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-B")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWsB.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                    }
                });

            // Note: clientId "*" may not be accepted by all SDKs at construction time.
            // See top-level note for alternative auth patterns (e.g., key auth without clientId).
            // NOTE: this SDK rejects "*" in ClientOptions (RSA7c), so the client uses key auth with no clientId, the alternative the spec's notes give: an unidentified client may enterClient any clientId (RTP15a, RTP15f); each mock is installed just before its client is built
            InstallMock(mockWsA);
            var clientA = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            InstallMock(mockWsB);
            var clientB = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channelA = clientA.Channels.Get(channelName);
            var channelB = clientB.Channels.Get(channelName);

            // Test Steps
            // Connect and attach both clients
            clientA.Connect();
            await UtsWait.AwaitConnectionState(clientA.Connection, ConnectionState.Connected);
            var attachResultA = await UtsWait.WithTimeout(channelA.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResultA.IsSuccess.Should().BeTrue("AWAIT channel_a.attach() succeeds ({0})", attachResultA.Error?.Message);

            clientB.Connect();
            await UtsWait.AwaitConnectionState(clientB.Connection, ConnectionState.Connected);
            var attachResultB = await UtsWait.WithTimeout(channelB.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResultB.IsSuccess.Should().BeTrue("AWAIT channel_b.attach() succeeds ({0})", attachResultB.Error?.Message);

            // Subscribe on client B to observe remote presence events
            var receivedEntersB = new CaptureList<PresenceMessage>();
            channelB.Presence.Subscribe(PresenceAction.Enter, message => receivedEntersB.Add(message));

            // Client A enters 50 members
            for (var i = 0; i <= memberCount - 1; i++)
            {
                var enterResult = await UtsWait.WithTimeout(channelA.Presence.EnterClientAsync($"user-{i}", $"data-{i}"), TimeSpan.FromSeconds(10), "EnterClientAsync");
                enterResult.IsSuccess.Should().BeTrue("AWAIT channel_a.presence.enterClient() succeeds ({0})", enterResult.Error?.Message);
            }

            // Server delivers those ENTER events to client B as PRESENCE messages
            // (In real Ably, the server broadcasts to all connections on the channel)
            for (var i = 0; i <= memberCount - 1; i++)
            {
                mockWsB.SendToClient(ProtocolMessages.PresenceProtocolMessage(
                    channelName,
                    new JArray(ProtocolMessages.PresenceEntry(
                        (int)PresenceAction.Enter,
                        $"user-{i}",
                        "conn-A",
                        $"conn-A:{i}:0",
                        NowMillis(),
                        $"data-{i}"))));
            }

            // Server sends a SYNC to client B with all 50 members
            var syncMembers = new JArray();
            for (var i = 0; i <= memberCount - 1; i++)
            {
                syncMembers.Add(ProtocolMessages.PresenceEntry(
                    (int)PresenceAction.Present,
                    $"user-{i}",
                    "conn-A",
                    $"conn-A:{i}:0",
                    NowMillis(),
                    $"data-{i}"));
            }

            mockWsB.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", syncMembers));

            // Client B gets all members
            var members = (await UtsWait.WithTimeout(channelB.Presence.GetAsync(), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // Client A sent all 50 presence messages
            capturedPresenceA.Should().HaveCount(memberCount);

            // Client B received all 50 ENTER events
            await UtsWait.PollUntil(() => receivedEntersB.Count == memberCount, description: "all ENTER events delivered to client B");
            receivedEntersB.Should().HaveCount(memberCount);

            // All 50 members present via get() on client B
            members.Should().HaveCount(memberCount);

            // Verify each member has correct data and connectionId from conn-A
            for (var i = 0; i <= memberCount - 1; i++)
            {
                var member = members.FirstOrDefault(m => m.ClientId == $"user-{i}");
                member.Should().NotBeNull();
                ((string)member.Data).Should().Be($"data-{i}");
                member.ConnectionId.Should().Be("conn-A");
            }

            await CloseClient(clientA);
            await CloseClient(clientB);
        }
    }
}
