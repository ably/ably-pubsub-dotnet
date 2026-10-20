// Connection whenState Tests (RTN26)
// Derived from uts/realtime/unit/connection/when_state_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none (every test is a missing-API stub: Connection#whenState is not in this SDK)

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
    public class WhenStateTests : UtsUnitTestBase
    {
        public WhenStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN26a/immediate-callback-current-state-0
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26a")]
        public Task RTN26a_ImmediateCallbackCurrentState()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
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
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            // # Start connection
            // client.connect()
            // # Wait for CONNECTED state
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // # Now call whenState for the current state
            // callback_invoked = false
            // callback_arg = undefined
            // client.connection.whenState(ConnectionState.connected, (change) => {
            //   callback_invoked = true
            //   callback_arg = change
            // })
            // # Callback should be invoked synchronously or very quickly
            // WAIT(50)
            // # Callback was invoked immediately
            // ASSERT callback_invoked == true
            // # Callback was invoked with null argument (not a StateChange object)
            // ASSERT callback_arg IS null
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN26b/deferred-callback-future-state-0
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26b")]
        public Task RTN26b_DeferredCallbackFutureState()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
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
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            // # Connection is in INITIALIZED state
            // ASSERT client.connection.state == ConnectionState.initialized
            // # Set up whenState before connecting
            // callback_invoked = false
            // callback_arg = undefined
            // client.connection.whenState(ConnectionState.connected, (change) => {
            //   callback_invoked = true
            //   callback_arg = change
            // })
            // # Callback should not be invoked yet
            // ASSERT callback_invoked == false
            // # Start connection
            // client.connect()
            // # Wait for CONNECTED state
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // # Give callback a moment to execute
            // WAIT(50)
            // # Callback was invoked after state transition
            // ASSERT callback_invoked == true
            // # Callback was invoked with a ConnectionStateChange object (not null)
            // ASSERT callback_arg IS NOT null
            // ASSERT callback_arg.previous IN [ConnectionState.initialized, ConnectionState.connecting]
            // ASSERT callback_arg.current == ConnectionState.connected
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN26b/fires-only-once-1
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26b")]
        public Task RTN26b_FiresOnlyOnce()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
            // connection_attempt_count = 0
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     connection_attempt_count++
            //     IF connection_attempt_count == 1:
            //       # First attempt: connect then disconnect
            //       conn.respond_with_success()
            //       conn.send_to_client(ProtocolMessage(
            //         action: CONNECTED,
            //         connectionId: "connection-id-1",
            //         connectionKey: "connection-key-1",
            //         connectionDetails: ConnectionDetails(
            //           connectionKey: "connection-key-1",
            //           maxIdleInterval: 15000,
            //           connectionStateTtl: 120000
            //         )
            //       ))
            //     ELSE:
            //       # Second attempt: connect again
            //       conn.respond_with_success()
            //       conn.send_to_client(ProtocolMessage(
            //         action: CONNECTED,
            //         connectionId: "connection-id-2",
            //         connectionKey: "connection-key-2",
            //         connectionDetails: ConnectionDetails(
            //           connectionKey: "connection-key-2",
            //           maxIdleInterval: 15000,
            //           connectionStateTtl: 120000
            //         )
            //       ))
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   disconnectedRetryTimeout: 100,
            //   autoConnect: false
            // ))
            // enable_fake_timers()
            // # Set up whenState listener
            // callback_count = 0
            // client.connection.whenState(ConnectionState.connected, (change) => {
            //   callback_count++
            // })
            // # Start connection
            // client.connect()
            // # Wait for first CONNECTED
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // WAIT(50)
            // # Verify callback was invoked once
            // ASSERT callback_count == 1
            // # Force a disconnection
            // mock_ws.active_connection.close()
            // # Wait for DISCONNECTED
            // AWAIT_STATE client.connection.state == ConnectionState.disconnected
            //   WITH timeout: 2 seconds
            // # Advance time to trigger reconnection
            // ADVANCE_TIME(150)
            // # Wait for second CONNECTED
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // WAIT(50)
            // # Callback was still only invoked once (not again on reconnection)
            // ASSERT callback_count == 1
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN26a/multiple-whenstate-calls-1
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26a")]
        public Task RTN26a_MultipleWhenstateCalls()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
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
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            // # Set up multiple whenState listeners before connecting
            // callback1_invoked = false
            // callback2_invoked = false
            // callback3_invoked = false
            // client.connection.whenState(ConnectionState.connected, (change) => {
            //   callback1_invoked = true
            // })
            // client.connection.whenState(ConnectionState.connected, (change) => {
            //   callback2_invoked = true
            // })
            // client.connection.whenState(ConnectionState.connecting, (change) => {
            //   callback3_invoked = true
            // })
            // # Start connection
            // client.connect()
            // # Wait for CONNECTED state
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // WAIT(50)
            // # All whenState callbacks were invoked
            // ASSERT callback1_invoked == true
            // ASSERT callback2_invoked == true
            // ASSERT callback3_invoked == true
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN26a/no-fire-for-past-state-2
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26a")]
        public Task RTN26a_NoFireForPastState()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
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
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            // # Start connection
            // client.connect()
            // # Wait for CONNECTED state
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            //   WITH timeout: 5 seconds
            // # Now call whenState for a past state (CONNECTING)
            // callback_invoked = false
            // client.connection.whenState(ConnectionState.connecting, (change) => {
            //   callback_invoked = true
            // })
            // # Wait to see if callback is invoked
            // WAIT(200)
            // # Callback should NOT be invoked (we're not in CONNECTING state anymore)
            // ASSERT callback_invoked == false
            // # This demonstrates whenState checks current state, not historical states
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN26/whenstate-different-states-0
        [UtsFact(Skip = "Missing SDK public API: Connection#whenState (RTN26); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTN26")]
        public Task RTN26_WhenstateDifferentStates()
        {
            // NOTE: Connection#whenState is not in this SDK (realtime-mapping.md section 12, Missing APIs); the whole test depends on it
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     # Connection attempt fails
            //     conn.respond_with_refused()
            //   }
            // )
            // install_mock(mock_ws)
            // client = Realtime(options: ClientOptions(
            //   key: "appId.keyId:keySecret",
            //   autoConnect: false
            // ))
            // # Set up whenState listeners for various states
            // initialized_fired = false
            // connecting_fired = false
            // disconnected_fired = false
            // client.connection.whenState(ConnectionState.initialized, (change) => {
            //   initialized_fired = true
            // })
            // client.connection.whenState(ConnectionState.connecting, (change) => {
            //   connecting_fired = true
            // })
            // client.connection.whenState(ConnectionState.disconnected, (change) => {
            //   disconnected_fired = true
            // })
            // # Initially in INITIALIZED
            // WAIT(50)
            // # Should fire immediately for current state
            // ASSERT initialized_fired == true
            // ASSERT connecting_fired == false
            // ASSERT disconnected_fired == false
            // # Start connection
            // client.connect()
            // # Wait for DISCONNECTED (connection will fail)
            // AWAIT_STATE client.connection.state == ConnectionState.disconnected
            //   WITH timeout: 5 seconds
            // WAIT(50)
            // # All states were reached and callbacks invoked
            // ASSERT initialized_fired == true
            // ASSERT connecting_fired == true
            // ASSERT disconnected_fired == true
            // CLOSE_CLIENT(client)
            return Task.CompletedTask;
        }
    }
}
