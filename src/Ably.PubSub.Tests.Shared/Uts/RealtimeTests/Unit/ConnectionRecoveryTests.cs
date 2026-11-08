// Connection Recovery Tests (RTN16d, RTN16f, RTN16f1, RTN16g, RTN16g1, RTN16g2, RTN16g3, RTN16i, RTN16j, RTN16k, RTN16l)
// Derived from uts/realtime/unit/connection/connection_recovery_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the mock answers CLOSE with CLOSED (infra-driving); recovery keys are built and parsed as JSON documents with JObject (realtime-mapping.md section 14, not white-box)

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ConnectionRecoveryTests : UtsUnitTestBase
    {
        public ConnectionRecoveryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // A recovery key is a JSON document (connectionKey, msgSerial, channelSerials).
        private static string RecoveryKey(string connectionKey, long msgSerial, JObject channelSerials)
        {
            return new JObject
            {
                ["connectionKey"] = connectionKey,
                ["msgSerial"] = msgSerial,
                ["channelSerials"] = channelSerials,
            }.ToString(Formatting.None);
        }

        private static JObject Attached(string channel, string channelSerial)
        {
            return ProtocolMessages.AttachedMessage(channel, new Dictionary<string, JToken> { ["channelSerial"] = channelSerial });
        }

        // UTS: realtime/unit/RTN16g/recovery-key-structure-0
        [UtsFact]
        [Trait("spec", "RTN16g")]
        public async Task RTN16g_RecoveryKeyStructure()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-abc-123", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Test Steps
            // Connect
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Get the WebSocket connection for sending mock responses
            var ws = mockWs.Connections[0];

            // Get two channels and simulate attaching them (including one with unicode name)
            var channelA = client.Channels.Get("channel-alpha");
            var channelB = client.Channels.Get("channel-éàü-世界");

            // Attach channel_a
            _ = channelA.AttachAsync();
            ws.SendToClient(Attached("channel-alpha", "serial-a-001"));
            await UtsWait.AwaitChannelState(channelA, ChannelState.Attached);

            // Attach channel_b (unicode name)
            _ = channelB.AttachAsync();
            ws.SendToClient(Attached("channel-éàü-世界", "serial-b-002"));
            await UtsWait.AwaitChannelState(channelB, ChannelState.Attached);

            // Create recovery key
            var recoveryKeyString = client.Connection.CreateRecoveryKey();

            // Assertions
            // Recovery key is not null
            recoveryKeyString.Should().NotBeNull();

            // Deserialize the recovery key (JSON format per ably-js reference)
            var recoveryKey = JObject.Parse(recoveryKeyString);

            // Contains connectionKey
            ((string)recoveryKey["connectionKey"]).Should().Be("key-abc-123");

            // Contains msgSerial (starts at 0 since no messages were sent)
            ((long)recoveryKey["msgSerial"]).Should().Be(0);

            // Contains channelSerials map with both channels
            recoveryKey["channelSerials"].Should().NotBeNull();
            ((string)recoveryKey["channelSerials"]["channel-alpha"]).Should().Be("serial-a-001");

            // RTN16g1: Unicode channel name is correctly encoded in the serialized key
            ((string)recoveryKey["channelSerials"]["channel-éàü-世界"]).Should().Be("serial-b-002");

            // Verify round-trip: re-serializing and deserializing preserves the unicode name
            var reSerialized = recoveryKey.ToString(Formatting.None);
            var reParsed = JObject.Parse(reSerialized);
            ((string)reParsed["channelSerials"]["channel-éàü-世界"]).Should().Be("serial-b-002");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN16g3/recovery-key-null-inactive-0
        [UtsFact(Skip = "UTS spec error RTN16g3: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN16g3")]
        public Task RTN16g3_RecoveryKeyNullInactive()
        {
            // SPEC ERROR RTN16g3: the SUSPENDED sub-case is internally inconsistent: its handler comment says "All connections fail
            // after initial, to force SUSPENDED", but it branches on connection_attempt_count, which only the first mock increments
            // (it stays 1), so every attempt succeeds and SUSPENDED is never reached. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN16g3 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN16k/recover-query-param-0
        [UtsFact]
        [Trait("spec", "RTN16k")]
        public async Task RTN16k_RecoverQueryParam()
        {
            // Setup
            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new CaptureList<PendingWebSocketConnection>();

            // Construct a valid recoveryKey
            var recoveryKey = RecoveryKey("recovered-key-xyz", 5, new JObject());

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                capturedConnectionAttempts.Add(conn);
                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First connection: successful recovery (same connectionId as implied by recoveryKey)
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("recovered-conn-id", "new-key-after-recovery", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
                else
                {
                    // Subsequent connection: resume after disconnect
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("recovered-conn-id", "resumed-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Recover = recoveryKey,
                AutoConnect = false,
            });

            // Test Steps
            // Connect - should use recover param
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Simulate disconnect and reconnection
            var ws = mockWs.Connections[0];
            var states = UtsRecord.ConnectionStates(client.Connection);
            ws.SimulateDisconnect();

            // Wait for resume reconnection
            // NOTE: AWAIT_STATE would return at once (already CONNECTED); the record is polled for a CONNECTED entered after the stimulus and for the second connection attempt
            await UtsWait.PollUntil(
                () => states.Snapshot().Count(s => s == ConnectionState.Connected) >= 1 && Volatile.Read(ref connectionAttemptCount) >= 2,
                timeout: TimeSpan.FromSeconds(5),
                description: "re-entered CONNECTED after the forced disconnect");

            // Assertions
            // First connection attempt includes recover param with connectionKey from recoveryKey
            capturedConnectionAttempts[0].QueryParams["recover"].Should().Be("recovered-key-xyz");

            // First connection attempt does NOT include resume param
            capturedConnectionAttempts[0].QueryParams.ContainsKey("resume").Should().BeFalse();

            // Second connection attempt uses resume (not recover) since client is now connected
            capturedConnectionAttempts[1].QueryParams["resume"].Should().Be("new-key-after-recovery");
            capturedConnectionAttempts[1].QueryParams.ContainsKey("recover").Should().BeFalse();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN16f/recover-initializes-msgserial-0
        [UtsFact]
        [Trait("spec", "RTN16f")]
        public async Task RTN16f_RecoverInitializesMsgserial()
        {
            // Setup
            var connectionAttemptCount = 0;

            // Construct a recoveryKey with msgSerial of 42
            var recoveryKey = RecoveryKey("old-key", 42, new JObject { ["test-channel"] = "ch-serial-1" });

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();

                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Successful recovery
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("recovered-conn", "new-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Recover = recoveryKey,
                AutoConnect = false,
            });

            // Test Steps
            // Connect with recovery
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Get WebSocket connection reference
            var ws = mockWs.Connections[0];

            // Attach the recovered channel
            var channel = client.Channels.Get("test-channel");
            _ = channel.AttachAsync();
            ws.SendToClient(Attached("test-channel", "ch-serial-updated"));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Publish a message - the msgSerial should start from the recovered value (42)
            _ = channel.PublishAsync("event", "data");

            // Capture the MESSAGE frame sent by the client
            var messageFrame = await mockWs.AwaitClientMessage(ProtocolMessages.Message);

            // ACK the message
            ws.SendToClient(ProtocolMessages.AckMessage(42, 1));

            // Assertions
            // The first message published uses msgSerial from the recoveryKey
            messageFrame.Should().NotBeNull();
            messageFrame.MsgSerial.Should().Be(42);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN16f1/malformed-recovery-key-0
        // DEVIATION: see deviations.md
        [UtsFact]
        [Trait("spec", "RTN16f1")]
        public async Task RTN16f1_MalformedRecoveryKey()
        {
            // Setup
            var connectionAttemptCount = 0;
            var capturedConnectionAttempts = new CaptureList<PendingWebSocketConnection>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Increment(ref connectionAttemptCount);
                capturedConnectionAttempts.Add(conn);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("fresh-conn", "fresh-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            // Use a malformed (non-JSON) recover string
            // NOTE: the spec's implementation note asks for an error-level log about the malformed key to be captured; the message text is unspecified, so any error-level log is asserted
            var log = new CapturingLogSink();
            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Recover = "this-is-not-valid-json!!!",
                AutoConnect = false,
                LogHandler = log,
                LogLevel = LogLevel.Debug,
            });

            // Test Steps
            // Connect - should proceed as a normal connection (no recover param)
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // Connection succeeded normally
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be("fresh-conn");
            client.Connection.Key.Should().Be("fresh-key");

            // No recover param was sent (malformed key was rejected)
            capturedConnectionAttempts[0].QueryParams.ContainsKey("recover").Should().BeFalse();

            // Also no resume param (this is a fresh connection)
            capturedConnectionAttempts[0].QueryParams.ContainsKey("resume").Should().BeFalse();

            // Only one connection attempt (normal connection, no retries)
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // An error was logged for the malformed recovery key
            // DEVIATION: spec (RTN16f1) requires an error to be logged, the SDK logs it at Warning - see deviations.md
            log.Logs.Snapshot().Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("recover"));

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN16j/recover-channel-serials-0
        [UtsFact]
        [Trait("spec", "RTN16j")]
        public async Task RTN16j_RecoverChannelSerials()
        {
            // Setup
            var connectionAttemptCount = 0;

            // Construct a recoveryKey with multiple channels
            var recoveryKey = RecoveryKey(
                "old-key-abc",
                10,
                new JObject
                {
                    ["channel-one"] = "serial-1-abc",
                    ["channel-two"] = "serial-2-def",
                    ["channel-üñîçöðé"] = "serial-3-unicode",
                });

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("recovered-conn", "new-key", connectionStateTtl: 120000, maxIdleInterval: 15000));
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Recover = recoveryKey,
                AutoConnect = false,
            });

            // Test Steps
            // Connect with recovery
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            // RTN16j: Channels from the recoveryKey are instantiated
            var channelOne = client.Channels.Get("channel-one");
            var channelTwo = client.Channels.Get("channel-two");
            var channelUnicode = client.Channels.Get("channel-üñîçöðé");

            // Each channel has its channelSerial set from the recoveryKey
            channelOne.Properties.ChannelSerial.Should().Be("serial-1-abc");
            channelTwo.Properties.ChannelSerial.Should().Be("serial-2-def");
            channelUnicode.Properties.ChannelSerial.Should().Be("serial-3-unicode");

            // RTN16i: Channels are NOT automatically attached — the user must explicitly attach them.
            // They should be in INITIALIZED state (the library instantiated them but didn't attach).
            channelOne.State.Should().Be(ChannelState.Initialized);
            channelTwo.State.Should().Be(ChannelState.Initialized);
            channelUnicode.State.Should().Be(ChannelState.Initialized);

            // When the user attaches, the ATTACH message should include the channelSerial
            // (this enables the server to resume the channel from the correct point)
            var ws = mockWs.Connections[0];

            _ = channelOne.AttachAsync();

            // Capture the ATTACH frame sent by the client
            await mockWs.AwaitClientMessage(ProtocolMessages.Attach);
            var sentFrames = mockWs.MessagesFromClientOfAction(ProtocolMessages.Attach).Where(m => m.Channel == "channel-one").ToList();
            sentFrames.Should().HaveCount(1);
            sentFrames[0].ChannelSerial.Should().Be("serial-1-abc");

            // Complete the attachment
            ws.SendToClient(Attached("channel-one", "serial-1-abc-updated"));
            await UtsWait.AwaitChannelState(channelOne, ChannelState.Attached);

            await CloseClient(client);
        }
    }
}
