// Network Change Tests (RTN20)
// Derived from uts/realtime/unit/connection/network_change_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: MockNetworkListener stands in for the platform connectivity listener (infra stand-in)

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
    public class NetworkChangeTests : UtsUnitTestBase
    {
        public NetworkChangeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN20a/network-loss-connected-disconnects-0
        [UtsFact]
        [Trait("spec", "RTN20a")]
        public async Task RTN20a_NetworkLossConnectedDisconnects()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockNetwork = new MockNetworkListener();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id-" + attempt, "connection-key-" + attempt));
            });
            InstallMock(mockWs);
            InstallMock(mockNetwork);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Simulate OS reporting network loss
            mockNetwork.SimulateNetworkLost();

            // The client should transition to DISCONNECTED and then automatically
            // attempt to reconnect (per RTN15). Wait for the full cycle to complete.
            // The reconnection may succeed immediately since the mock WebSocket
            // always accepts connections.
            // NOTE: AWAIT_STATE connected would return at once (already CONNECTED); the record is polled for the re-entry into CONNECTED after DISCONNECTED
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().SkipWhile(c => c.Current != ConnectionState.Disconnected).Any(c => c.Current == ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(10),
                description: "connection re-entered CONNECTED after DISCONNECTED");

            // Assertions
            // Verify the state change sequence includes DISCONNECTED transition
            UtsAssert.ContainsInOrder(
                stateChanges.Snapshot().Select(c => c.Current).ToList(),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                });

            // Verify the DISCONNECTED state change has an appropriate reason
            var disconnectedChange = stateChanges.Snapshot().First(s => s.Current == ConnectionState.Disconnected);
            disconnectedChange.Reason.Should().NotBeNull();

            // Verify reconnection happened
            Volatile.Read(ref connectionAttemptCount).Should().Be(2);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN20a/network-loss-connecting-disconnects-1
        [UtsFact]
        [Trait("spec", "RTN20a")]
        public async Task RTN20a_NetworkLossConnectingDisconnects()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockNetwork = new MockNetworkListener();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First attempt: don't respond yet - leave in CONNECTING state.
                    // The network loss event will fire while we're still connecting.
                    // Do NOT call respond_with_success() here.
                }
                else
                {
                    // Subsequent attempts succeed
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);
            InstallMock(mockNetwork);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Start connecting - the mock won't respond, so we stay in CONNECTING
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            // Simulate OS reporting network loss while still CONNECTING
            mockNetwork.SimulateNetworkLost();

            // Client should transition to DISCONNECTED, then eventually reconnect
            // NOTE: AWAIT_STATE connected would return at once only if already CONNECTED; the record is polled so the DISCONNECTED -> CONNECTING -> CONNECTED sequence is verified below
            await UtsWait.PollUntil(
                () => stateChanges.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(10),
                description: "connection reached CONNECTED after the network loss");

            // Assertions
            // Verify the state change sequence
            UtsAssert.ContainsInOrder(
                stateChanges.Snapshot(),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                });

            // The first connection attempt was abandoned, second succeeded
            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterThanOrEqualTo(2);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN20b/network-available-disconnected-connects-0
        [UtsFact(Skip = "UTS spec error RTN20b: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN20b")]
        public Task RTN20b_NetworkAvailableDisconnectedConnects()
        {
            // SPEC ERROR RTN20b: the refused second attempt is a host-unreachable failure (RSC15l1) on the default endpoint, whose fallback
            // set is not empty (REC2c1), so RTN17j has the client retry a fallback domain at once and it is CONNECTED again before the network
            // event; the spec assumes it waits out disconnectedRetryTimeout in DISCONNECTED. Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN20b - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/unit/RTN20c/network-available-connecting-restarts-0
        [UtsFact]
        [Trait("spec", "RTN20c")]
        public async Task RTN20c_NetworkAvailableConnectingRestarts()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockNetwork = new MockNetworkListener();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // First attempt: don't respond - leave pending (simulates slow connection)
                    // The network-available event will fire while this attempt is pending.
                }
                else
                {
                    // Second attempt (restarted after network event) succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);
            InstallMock(mockNetwork);

            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                AutoConnect = false,
                UseBinaryProtocol = false,
            });

            // Record all state changes
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Start connecting - the mock won't respond to the first attempt,
            // leaving the client in CONNECTING state
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Simulate OS reporting network is now available while still CONNECTING.
            // The client should abandon the pending connection and start a new attempt.
            mockNetwork.SimulateNetworkAvailable();

            // The restarted connection attempt should succeed
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // Assertions
            // The first connection attempt was abandoned and a new one was made
            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterThanOrEqualTo(2);

            // Client is now connected
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // NOTE: the spec's Setup records stateChanges but asserts nothing on them; recorded for diagnostics only
            Output.WriteLine("state changes: " + string.Join(",", stateChanges.Snapshot()));

            await CloseClient(client);
        }
    }
}
