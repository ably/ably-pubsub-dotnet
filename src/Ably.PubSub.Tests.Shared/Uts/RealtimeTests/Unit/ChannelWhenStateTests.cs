// RealtimeChannel whenState Tests (RTL25)
// Derived from uts/realtime/unit/channels/channel_when_state_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: every test is a skipped MIL stub: RealtimeChannel#whenState (RTL25) is missing from this SDK

using Ably.PubSub.Tests.Uts.Infra;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelWhenStateTests : UtsUnitTestBase
    {
        public ChannelWhenStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL25a/resolves-immediately-current-0
        [UtsFact(Skip = "Missing SDK API: RealtimeChannel#whenState (RTL25) is missing from this SDK; see deviations.md (Failing Tests)")]
        [Trait("spec", "RTL25a")]
        public async Task RTL25a_ResolvesImmediatelyCurrent()
        {
            // NOTE: RealtimeChannel#whenState (RTL25) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL25a-immediate-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     conn.respond_with_success()
            //     conn.send_to_client(ProtocolMessage(
            //       action: CONNECTED,
            //       connectionId: "connection-id",
            //       connectionKey: "connection-key",
            //       connectionDetails: ConnectionDetails(
            //         connectionKey: "connection-key",
            //         maxIdleInterval: 15000,
            //         connectionStateTtl: 120000
            //       )
            //     ))
            //   },
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name,
            //         flags: 0
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            //
            // channel = client.channels.get(channel_name)

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // AWAIT channel.attach()
            //
            // # Channel is now ATTACHED — call whenState for current state
            // result = AWAIT channel.whenState(ChannelState.attached)

            // # whenState resolved immediately with null (already in target state)
            // ASSERT result IS null
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL25b/waits-for-state-change-0
        [UtsFact(Skip = "Missing SDK API: RealtimeChannel#whenState (RTL25) is missing from this SDK; see deviations.md (Failing Tests)")]
        [Trait("spec", "RTL25b")]
        public async Task RTL25b_WaitsForStateChange()
        {
            // NOTE: RealtimeChannel#whenState (RTL25) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL25b-deferred-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     conn.respond_with_success()
            //     conn.send_to_client(ProtocolMessage(
            //       action: CONNECTED,
            //       connectionId: "connection-id",
            //       connectionKey: "connection-key",
            //       connectionDetails: ConnectionDetails(
            //         connectionKey: "connection-key",
            //         maxIdleInterval: 15000,
            //         connectionStateTtl: 120000
            //       )
            //     ))
            //   },
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name,
            //         flags: 0
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            //
            // channel = client.channels.get(channel_name)

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //
            // # Channel is in INITIALIZED state — start waiting for ATTACHED
            // when_state_promise = channel.whenState(ChannelState.attached)
            //
            // # Attach the channel (this triggers the state transition)
            // AWAIT channel.attach()
            //
            // # Now await the whenState result
            // result = AWAIT when_state_promise

            // # whenState resolved with a ChannelStateChange object (not null)
            // ASSERT result IS NOT null
            // ASSERT result.current == ChannelState.attached
            // ASSERT result.previous IN [ChannelState.initialized, ChannelState.attaching]
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL25b/fires-once-only-1
        [UtsFact(Skip = "Missing SDK API: RealtimeChannel#whenState (RTL25) is missing from this SDK; see deviations.md (Failing Tests)")]
        [Trait("spec", "RTL25b")]
        public async Task RTL25b_FiresOnceOnly()
        {
            // NOTE: RealtimeChannel#whenState (RTL25) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL25b-once-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     conn.respond_with_success()
            //     conn.send_to_client(ProtocolMessage(
            //       action: CONNECTED,
            //       connectionId: "connection-id",
            //       connectionKey: "connection-key",
            //       connectionDetails: ConnectionDetails(
            //         connectionKey: "connection-key",
            //         maxIdleInterval: 15000,
            //         connectionStateTtl: 120000
            //       )
            //     ))
            //   },
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name,
            //         flags: 0
            //       ))
            //     IF msg.action == DETACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: DETACHED,
            //         channel: channel_name
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            //
            // channel = client.channels.get(channel_name)

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //
            // # Register a side-effect counter via a listener wrapping whenState
            // attach_count = 0
            // channel.once(ChannelState.attached, () => { attach_count++ })
            //
            // # Also start a whenState that we'll use to verify one-shot behavior
            // when_state_promise = channel.whenState(ChannelState.attached)
            //
            // # First attach
            // AWAIT channel.attach()
            // result = AWAIT when_state_promise
            // ASSERT result IS NOT null
            // ASSERT attach_count == 1
            //
            // # Detach
            // AWAIT channel.detach()
            //
            // # Second attach — a new whenState should be needed; the old one is consumed
            // AWAIT channel.attach()
            // WAIT(50)

            // # The once listener only fired once (confirming one-shot semantics)
            // ASSERT attach_count == 1
            // CLOSE_CLIENT(client)
        }

        // UTS: realtime/unit/RTL25a/past-state-does-not-resolve-1
        [UtsFact(Skip = "Missing SDK API: RealtimeChannel#whenState (RTL25) is missing from this SDK; see deviations.md (Failing Tests)")]
        [Trait("spec", "RTL25a")]
        public async Task RTL25a_PastStateDoesNotResolve()
        {
            // NOTE: RealtimeChannel#whenState (RTL25) is missing from this SDK
            await Task.CompletedTask;
            // channel_name = "test-RTL25a-past-${random_id()}"
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     conn.respond_with_success()
            //     conn.send_to_client(ProtocolMessage(
            //       action: CONNECTED,
            //       connectionId: "connection-id",
            //       connectionKey: "connection-key",
            //       connectionDetails: ConnectionDetails(
            //         connectionKey: "connection-key",
            //         maxIdleInterval: 15000,
            //         connectionStateTtl: 120000
            //       )
            //     ))
            //   },
            //   onMessageFromClient: (msg) => {
            //     IF msg.action == ATTACH:
            //       mock_ws.send_to_client(ProtocolMessage(
            //         action: ATTACHED,
            //         channel: channel_name,
            //         flags: 0
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            //
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            //
            // channel = client.channels.get(channel_name)

            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //
            // # Attach — channel passes through ATTACHING to reach ATTACHED
            // AWAIT channel.attach()
            // ASSERT channel.state == ChannelState.attached
            //
            // # Now call whenState for ATTACHING — a past state, not the current one
            // resolved = false
            //
            // # Start whenState but do NOT await — check that it does not resolve
            // when_state_promise = channel.whenState(ChannelState.attaching)
            // when_state_promise.then(() => { resolved = true })
            //
            // # Wait to see if it resolves
            // WAIT(200)

            // # whenState should NOT have resolved (we're not in ATTACHING state anymore)
            // ASSERT resolved == false
            // CLOSE_CLIENT(client)
        }
    }
}
