using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/connection_recovery_test.md in ably/specification.
    ///
    /// Spec points: RTN16d, RTN16f, RTN16f1, RTN16g, RTN16g1, RTN16g2, RTN16g3, RTN16i, RTN16j,
    /// RTN16k, RTN16l
    ///
    /// Three translation notes that apply across the file:
    ///
    /// The spec's "createRecoveryKey() IS null" is asserted here as <c>BeNullOrEmpty()</c>.
    /// <c>Connection.CreateRecoveryKey()</c> returns <c>string.Empty</c> where RTN16g3 calls for null,
    /// a choice its own doc comment states and justifies: this SDK returns empty strings rather than
    /// nulls for absent string values throughout, and callers hand the result straight back as
    /// <c>ClientOptions.Recover</c>, which treats the two alike. The assertion still pins the
    /// behaviour RTN16g3 is about — no key is handed out in those states — because it fails if a real
    /// key comes back.
    ///
    /// RTN16g3 is one Test ID covering three independent clients (the terminal states, FAILED, and
    /// SUSPENDED), each with its own mock and its own setup. It is translated as three
    /// <c>[Fact]</c>s carrying that one Test ID, so a failure names which state regressed.
    ///
    /// RTL6b makes a realtime publish await its ACK, so RTN16f drives its publish as a task and
    /// completes it with the ACK the spec already sends.
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ConnectionRecoveryTests : UtsTestBase
    {
        private const string UnicodeChannelName = "channel-éàü-世界";

        private const string UnicodeRecoveredChannelName = "channel-üñîçöðé";

        public ConnectionRecoveryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN16g/recovery-key-structure-0
        [Fact]
        public async Task RTN16g_RecoveryKeyStructure()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-1",
                        connectionKey: "key-abc-123")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channelA = client.Channels.Get("channel-alpha");
            var channelB = client.Channels.Get(UnicodeChannelName);

            // The ATTACH is waited for before the ATTACHED is injected, so the channel has actually
            // asked to attach rather than being handed a reply it never requested.
            channelA.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                "channel-alpha",
                new Dictionary<string, JToken> { ["channelSerial"] = "serial-a-001" }));
            await UtsClients.AwaitChannelState(channelA, ChannelState.Attached);

            channelB.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach, 2);
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                UnicodeChannelName,
                new Dictionary<string, JToken> { ["channelSerial"] = "serial-b-002" }));
            await UtsClients.AwaitChannelState(channelB, ChannelState.Attached);

            var recoveryKeyString = client.Connection.CreateRecoveryKey();

            recoveryKeyString.Should().NotBeNullOrEmpty();

            var recoveryKey = JObject.Parse(recoveryKeyString);

            recoveryKey["connectionKey"].Value<string>().Should().Be("key-abc-123");

            // No messages were sent, so the serial is still at zero.
            recoveryKey["msgSerial"].Value<long>().Should().Be(0L);

            recoveryKey.ContainsKey("channelSerials").Should().BeTrue();
            var channelSerials = (JObject)recoveryKey["channelSerials"];
            channelSerials["channel-alpha"].Value<string>().Should().Be("serial-a-001");

            // RTN16g1: the unicode channel name is correctly encoded in the serialized key.
            channelSerials[UnicodeChannelName].Value<string>().Should().Be("serial-b-002");

            // Round trip: re-serializing and deserializing preserves the unicode name.
            var reParsed = JObject.Parse(recoveryKey.ToString(Formatting.None));
            ((JObject)reParsed["channelSerials"])[UnicodeChannelName]
                .Value<string>().Should().Be("serial-b-002");
        }

        // UTS: realtime/unit/RTN16g3/recovery-key-null-inactive-0
        [Fact]
        public async Task RTN16g3_RecoveryKeyNullBeforeConnectAndWhenClosed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-1",
                        connectionKey: "key-1")));

            var client = RealtimeClient(mockWs);

            // Before connecting (INITIALIZED state, no connectionKey).
            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty();

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.CreateRecoveryKey().Should().NotBeNullOrEmpty();

            // Both waiters are registered before Close(): the workflow drains its command queue on
            // its own thread, so CLOSING would otherwise be gone before it could be observed.
            var closing = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closing);
            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);

            client.Connection.Close();

            await closing;
            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty();

            // Close() only sends a CLOSE and waits for the server's CLOSED, so the test answers it
            // rather than spending the realtimeRequestTimeout in CLOSING.
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());

            await closed;
            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN16g3/recovery-key-null-inactive-0
        [Fact]
        public async Task RTN16g3_RecoveryKeyNullWhenFailed()
        {
            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "conn-f",
                        connectionKey: "key-f")));

            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var failed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Failed);

            // A connection-level ERROR arriving while CONNECTED fails the connection whatever its
            // status code, which is why the spec's 50000/500 reaches FAILED rather than DISCONNECTED.
            mockWs.SendToClientAndClose(ProtocolMessages.ErrorMessage(50000, "Fatal error", 500));

            await failed;

            client.Connection.CreateRecoveryKey().Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN16g3/recovery-key-null-inactive-0
        [Fact]
        public async Task RTN16g3_RecoveryKeyRetainedWhenSuspended()
        {
            // All connections after the first are refused, to force SUSPENDED.
            var attempt = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (attempt == 0)
                {
                    // maxIdleInterval 0 disables RTN23a idle detection, which measures against the
                    // same clock this test advances and would otherwise drop the connection itself.
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "conn-s",
                            connectionKey: "key-s",
                            connectionStateTtl: 2000,
                            maxIdleInterval: 0));
                }
                else
                {
                    conn.RespondWithRefused();
                }

                attempt = attempt + 1;
            });

            // The spec's enable_fake_timers() / ADVANCE_TIME, split the two ways the skill's Timers
            // section prescribes. RTN14e's connectionStateTtl is time the SDK *measures* —
            // AttemptsHelpers.ShouldSuspend compares ClientOptions.NowFunc against the first recorded
            // attempt — so TestClock reaches it. The wait in DISCONNECTED before retrying is time the
            // SDK *schedules* on a real timer, so it is shortened through DisconnectedRetryTimeout.
            // fallbackHosts is already empty by UtsClients default.
            var clock = new TestClock();
            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var suspended = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(15));

            mockWs.SimulateDisconnect();

            // The RTN14e deadline is measured from the attempt the drop records, so the advance has to
            // follow that record — advancing first would carry the recorded time along with the clock
            // and nothing would ever elapse. A second connection attempt proves the record was taken,
            // because the retry producing it is queued after it.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "a second connection attempt after the transport dropped");

            // The spec's LOOP of ADVANCE_TIME(1500) until suspended; one advance past the 2000ms
            // connectionStateTtl this connection was given does the same work.
            clock.Advance(3000);

            await suspended;

            // RTN8d/RTN9d: the connectionKey is retained, so the connection is still recoverable while
            // suspended and createRecoveryKey() returns a key.
            client.Connection.CreateRecoveryKey().Should().NotBeNullOrEmpty();
        }

        // UTS: realtime/unit/RTN16k/recover-query-param-0
        [Fact]
        public async Task RTN16k_RecoverQueryParam()
        {
            var recoveryKey = new JObject
            {
                ["connectionKey"] = "recovered-key-xyz",
                ["msgSerial"] = 5,
                ["channelSerials"] = new JObject(),
            }.ToString(Formatting.None);

            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new List<PendingWebSocketConnection>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;
                capturedConnectionAttempts.Add(conn);

                if (connectionAttemptCount == 1)
                {
                    // First connection: successful recovery.
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "recovered-conn-id",
                            connectionKey: "new-key-after-recovery"));
                }
                else
                {
                    // Subsequent connection: resume after disconnect.
                    conn.RespondWithSuccess(
                        ProtocolMessages.ConnectedMessage(
                            connectionId: "recovered-conn-id",
                            connectionKey: "resumed-key"));
                }
            });

            var client = RealtimeClient(mockWs, configure: options => options.Recover = recoveryKey);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Trap 1: the client is still CONNECTED here, so waiting for CONNECTED would return at
            // once and the assertions would run before the second attempt had been made.
            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected);

            mockWs.SimulateDisconnect();

            await reconnected;

            capturedConnectionAttempts.Should().HaveCount(2);

            // First attempt carries recover, set from the connectionKey component of the recoveryKey.
            capturedConnectionAttempts[0].QueryParams.Should().ContainKey("recover");
            capturedConnectionAttempts[0].QueryParams["recover"].Should().Be("recovered-key-xyz");
            capturedConnectionAttempts[0].QueryParams.Should().NotContainKey("resume");

            // Second attempt uses resume, not recover, because the client is now connected.
            capturedConnectionAttempts[1].QueryParams.Should().ContainKey("resume");
            capturedConnectionAttempts[1].QueryParams["resume"].Should().Be("new-key-after-recovery");
            capturedConnectionAttempts[1].QueryParams.Should().NotContainKey("recover");
        }

        // UTS: realtime/unit/RTN16f/recover-initializes-msgserial-0
        [Fact]
        public async Task RTN16f_RecoverInitializesMsgSerial()
        {
            var recoveryKey = new JObject
            {
                ["connectionKey"] = "old-key",
                ["msgSerial"] = 42,
                ["channelSerials"] = new JObject { ["test-channel"] = "ch-serial-1" },
            }.ToString(Formatting.None);

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "recovered-conn",
                        connectionKey: "new-key")));

            var client = RealtimeClient(mockWs, configure: options => options.Recover = recoveryKey);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get("test-channel");
            channel.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                "test-channel",
                new Dictionary<string, JToken> { ["channelSerial"] = "ch-serial-updated" }));
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached);

            // Publish a message - the msgSerial should start from the recovered value (42).
            var publish = channel.PublishAsync("event", "data");
            var published = await mockWs.AwaitPublished();

            published.Should().HaveCount(1);

            // The first message published uses msgSerial from the recoveryKey.
            published[0].MsgSerial.Should().Be(42L);

            mockWs.SendToClient(ProtocolMessages.AckMessage(42));

            var result = await publish;
            result.IsSuccess.Should().BeTrue();
        }

        // UTS: realtime/unit/RTN16f1/malformed-recovery-key-0
        [Fact]
        public async Task RTN16f1_MalformedRecoveryKey()
        {
            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new List<PendingWebSocketConnection>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;
                capturedConnectionAttempts.Add(conn);
                conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "fresh-conn",
                        connectionKey: "fresh-key"));
            });

            // Use a malformed (non-JSON) recover string.
            var client = RealtimeClient(
                mockWs,
                configure: options => options.Recover = "this-is-not-valid-json!!!");

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Connection succeeded normally.
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be("fresh-conn");
            client.Connection.Key.Should().Be("fresh-key");

            capturedConnectionAttempts.Should().HaveCount(1);

            // No recover param was sent (the malformed key was rejected), and no resume param either
            // because this is a fresh connection.
            //
            // NOTE: the spec's trailing implementation note asks for the error log to be captured as
            // well. Its Assertions block does not assert on it, and the SDK logs the rejection at
            // Warning rather than Error (RecoveryKeyContext.Decode), so that half is left to the
            // deviation record rather than asserted here.
            capturedConnectionAttempts[0].QueryParams.Should().NotContainKey("recover");
            capturedConnectionAttempts[0].QueryParams.Should().NotContainKey("resume");

            // Only one connection attempt (normal connection, no retries).
            connectionAttemptCount.Should().Be(1);
        }

        // UTS: realtime/unit/RTN16j/recover-channel-serials-0
        [Fact]
        public async Task RTN16j_RecoverChannelSerials()
        {
            var recoveryKey = new JObject
            {
                ["connectionKey"] = "old-key-abc",
                ["msgSerial"] = 10,
                ["channelSerials"] = new JObject
                {
                    ["channel-one"] = "serial-1-abc",
                    ["channel-two"] = "serial-2-def",
                    [UnicodeRecoveredChannelName] = "serial-3-unicode",
                },
            }.ToString(Formatting.None);

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(
                    ProtocolMessages.ConnectedMessage(
                        connectionId: "recovered-conn",
                        connectionKey: "new-key")));

            var client = RealtimeClient(mockWs, configure: options => options.Recover = recoveryKey);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // RTN16j: channels from the recoveryKey are instantiated, so these Get calls return the
            // instances the library already made rather than creating fresh ones.
            var channelOne = client.Channels.Get("channel-one");
            var channelTwo = client.Channels.Get("channel-two");
            var channelUnicode = client.Channels.Get(UnicodeRecoveredChannelName);

            // Each channel has its channelSerial set from the recoveryKey.
            channelOne.Properties.ChannelSerial.Should().Be("serial-1-abc");
            channelTwo.Properties.ChannelSerial.Should().Be("serial-2-def");
            channelUnicode.Properties.ChannelSerial.Should().Be("serial-3-unicode");

            // RTN16i: channels are NOT automatically attached - the user must explicitly attach them.
            channelOne.State.Should().Be(ChannelState.Initialized);
            channelTwo.State.Should().Be(ChannelState.Initialized);
            channelUnicode.State.Should().Be(ChannelState.Initialized);

            // When the user attaches, the ATTACH message should include the channelSerial, which is
            // what lets the server resume the channel from the correct point.
            channelOne.Attach();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Attach);

            var attachFrames = mockWs.MessagesFromClient
                .Where(m => m.Action == ProtocolMessage.MessageAction.Attach
                            && m.Channel == "channel-one")
                .ToList();

            attachFrames.Should().HaveCount(1);
            attachFrames[0].ChannelSerial.Should().Be("serial-1-abc");

            // Complete the attachment.
            mockWs.SendToClient(ProtocolMessages.AttachedMessage(
                "channel-one",
                new Dictionary<string, JToken> { ["channelSerial"] = "serial-1-abc-updated" }));
            await UtsClients.AwaitChannelState(channelOne, ChannelState.Attached);
        }
    }
}
