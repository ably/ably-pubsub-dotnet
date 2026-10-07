using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// Tests for the WebSocket mock itself, not for the SDK.
    ///
    /// The realtime unit tier is entirely built on this, and the failure mode of a wrong transport mock is
    /// a test that hangs, or passes without testing anything, rather than one that fails with a
    /// useful message — so the contract is pinned here.
    /// </summary>
    public class MockWebSocketTests : UtsTestBase
    {
        public MockWebSocketTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task RespondWithSuccess_ConnectsTheClient()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.Id.Should().Be(ProtocolMessages.TestConnectionId);
            client.Connection.Key.Should().Be(ProtocolMessages.TestConnectionKey);
            mockWs.ConnectionAttempts.Should().HaveCount(1);
            mockWs.ActiveConnection.Should().NotBeNull();
        }

        [Fact]
        public async Task TheConnectionUrlCarriesTheQueryParametersTheSpecsRead()
        {
            PendingWebSocketConnection attempt = null;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempt = conn;
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
            });

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            attempt.Should().NotBeNull();
            attempt.Url.Host.Should().Be("realtime.ably.io");
            attempt.QueryParams.Should().ContainKey("key");
            attempt.Protocol.Should().Be("application/json", "msgpack is compiled out of this SDK build");
        }

        [Fact]
        public async Task HandlerWithState_FailsTheFirstAttemptAndSucceedsOnTheSecond()
        {
            var attempts = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                attempts++;
                if (attempts == 1)
                {
                    conn.RespondWithDnsError();
                }
                else
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            attempts.Should().Be(2);
            mockWs.EventsOfType(MockEventType.ConnectionFailure).Should().HaveCount(1);
            mockWs.EventsOfType(MockEventType.ConnectionSuccess).Should().HaveCount(1);
        }

        [Fact]
        public async Task MessagesTheClientSendsAreCapturedAndDecoded()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Channels.Get("uts-mock-ws").Attach();

            var attaches = await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            attaches.Should().HaveCount(1);
            attaches[0].Channel.Should().Be("uts-mock-ws");
        }

        [Fact]
        public async Task RawTextFrameHookSeesTheWireEncoding()
        {
            // The hook the encoding specs need: it sees the JSON before it is decoded, so a spec can
            // assert that a null field was omitted rather than sent.
            string lastFrame = null;
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onTextDataFrame: text => lastFrame = text);

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Channels.Get("raw-frame").Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            lastFrame.Should().NotBeNull();
            lastFrame.Should().Contain("\"channel\":\"raw-frame\"");
        }

        [Fact]
        public async Task SendToClient_DeliversAnInjectedMessage()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get("inject");
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            mockWs.SendToClient(ProtocolMessages.AttachedMessage("inject"));

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);
            channel.State.Should().Be(ChannelState.Attached);
        }

        [Fact]
        public async Task UnknownFieldsAndActionsSurviveToTheWire()
        {
            // What makes the forwards-compatibility specs (RTF1/RSF1) translatable without a separate
            // send_to_client_raw: the injected message is a JObject, so an unknown field on a known
            // action — and an unknown action entirely — reach the SDK untouched.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var attached = ProtocolMessages.AttachedMessage("fwd");
            attached["someFutureField"] = "ignored";

            var channel = client.Channels.Get("fwd");
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);
            mockWs.SendToClient(attached);
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            // An action the SDK's enum does not define must not take the connection down.
            mockWs.SendToClient(new Newtonsoft.Json.Linq.JObject { ["action"] = 99 });
            await Task.Delay(50);

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Fact]
        public async Task SimulateDisconnect_DropsTheConnectionAndTheSequenceIsRecorded()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10));

            // Register the recorder before connecting: DISCONNECTED is transient on the way back up and
            // cannot be caught by waiting for it.
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.SimulateDisconnect();
            await UtsClients.NextConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue(
                "the recorded sequence is: " + string.Join(", ", UtsClients.Snapshot(states)));

            mockWs.ConnectionAttempts.Should().HaveCount(2);
            mockWs.EventsOfType(MockEventType.ServerDisconnect).Should().HaveCount(1);
        }

        [Fact]
        public async Task RespondWithError_ConnectsThenFailsWithTheServerError()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
                conn.RespondWithError(ProtocolMessages.ErrorMessage(40400, "Invalid credentials", 404)));

            var client = RealtimeClient(mockWs);

            // Assert on the recorded transition rather than on Connection.ErrorReason afterwards. The
            // server closes the socket straight after a connection-level ERROR — which is what the real
            // server does and what RespondWithError models — so a second transition follows close behind
            // and the property holds whichever error came last. Sampling it is a race; this is not.
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);
            client.Connect();

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(10));

            var failed = UtsClients.Snapshot(changes).Find(c => c.Current == ConnectionState.Failed);
            failed.Should().NotBeNull();
            failed.Reason.Should().NotBeNull();
            failed.Reason.Code.Should().Be(40400);
        }

        [Fact]
        public async Task AwaitConnectionAttempt_HandsTheAttemptToTheTest()
        {
            // A handler that deliberately answers nothing is how the specs withhold a response.
            var mockWs = new MockWebSocket(onConnectionAttempt: _ => { });

            var client = RealtimeClient(mockWs);
            client.Connect();

            var attempt = await mockWs.AwaitConnectionAttempt();
            attempt.Answered.Should().BeFalse();

            attempt.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
        }

        [Fact]
        public async Task AwaitClientClose_SeesTheLibraryCloseTheSocket()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var pendingClose = mockWs.AwaitClientClose();

            // client.Close() sends a CLOSE protocol message and waits for the server's CLOSED before it
            // tears the socket down, so the close event only appears once the server has answered.
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());

            var closeEvent = await pendingClose;
            closeEvent.Should().NotBeNull();
            mockWs.EventsOfType(MockEventType.ClientClose).Should().NotBeEmpty();
        }

        [Fact]
        public async Task ActiveConnectionIsNullBeforeConnectingAndAfterTheServerCloses()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            mockWs.ActiveConnection.Should().BeNull();

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            mockWs.ActiveConnection.Should().NotBeNull();

            var connection = mockWs.ActiveConnection;
            connection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ActiveConnection == null,
                "the dropped connection to stop being active");
        }

        [Fact]
        public async Task InjectingIntoAClosedConnectionThrowsRatherThanBeingSwallowed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var connection = mockWs.ActiveConnection;
            connection.SimulateDisconnect();

            Action act = () => connection.SendToClient(ProtocolMessages.HeartbeatMessage());

            act.Should().Throw<InvalidOperationException>().WithMessage("*closed*");
        }

        [Fact]
        public void SendToClientWithNoConnectionExplainsItself()
        {
            var mockWs = new MockWebSocket();

            Action act = () => mockWs.SendToClient(ProtocolMessages.HeartbeatMessage());

            act.Should().Throw<InvalidOperationException>().WithMessage("*no active connection*");
        }

        [Fact]
        public async Task TheTimelineOrdersEveryEvent()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var types = mockWs.Events.Select(e => e.Type).ToList();
            types.Should().StartWith(new[]
            {
                MockEventType.ConnectionAttempt,
                MockEventType.ConnectionSuccess,
                MockEventType.MessageToClient,
            });
        }

        [Fact]
        public async Task AckUnblocksAPublish()
        {
            // Most specs write the publish un-awaited and never ACK it. RTL6b makes a realtime publish
            // await the ACK, so a derived test either ACKs or drives the publish as a task.
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));

            var client = RealtimeClient(mockWs);
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get("ack-test");
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);
            mockWs.SendToClient(ProtocolMessages.AttachedMessage("ack-test"));
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            var publish = channel.PublishAsync("event", "data");
            var published = await mockWs.AwaitPublished();

            mockWs.SendToClient(ProtocolMessages.AckMessage(published[0].MsgSerial));

            var result = await publish;
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void ResetClearsTheTimelineButKeepsTheHandlers()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess());

            mockWs.Reset();

            mockWs.Events.Should().BeEmpty();
            mockWs.ConnectionAttempts.Should().BeEmpty();
            mockWs.OnConnectionAttempt.Should().NotBeNull();
        }
    }
}
