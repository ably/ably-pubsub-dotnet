using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using RealtimeConnection = Ably.PubSub.Realtime.Connection;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/network_change_test.md in ably/specification.
    ///
    /// Spec points: RTN20, RTN20a, RTN20b, RTN20c
    ///
    /// <para>
    /// <b>The SDK's network hook is static, so these tests cannot use it directly.</b> The spec asks
    /// for an injectable <c>MockNetworkListener</c>. The nearest thing here is
    /// <c>Connection.NotifyOperatingSystemNetworkState</c>, which a host application calls to report
    /// an OS network change — but it is static and every <c>Connection</c> subscribes to it from its
    /// own constructor, so calling it does not simulate a change for the client under test. It
    /// simulates one for every live client in the process. Measured: it destabilised three of the
    /// repo's own unit tests, each handed RTN20a's 80017 DISCONNECTED with <c>retryInstantly</c>.
    /// </para>
    /// <para>
    /// These tests therefore go through <c>UtsClients.NotifyNetworkState(connection, state)</c>,
    /// which runs the same two lines against one client's internal surface. That is the spec's
    /// injectable listener, scoped the way the spec intends — a white-box adaptation of an internal
    /// API in a unit test, which <c>writing-derived-tests.md</c> permits, with the observable
    /// unchanged.
    /// </para>
    /// <para>
    /// <c>AutomaticNetworkStateMonitoring</c> is turned off for every client here, so a real OS
    /// network event cannot inject a state change into a unit test. That is best effort, not a
    /// guarantee: the registration it drives is process-wide and never removed, so another test
    /// leaving it on hooks it for the rest of the run.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class NetworkChangeTests : UtsTestBase
    {
        /// <summary>The spec's <c>WITH timeout: 10 seconds</c> on the post-network-event waits.</summary>
        private static readonly TimeSpan SpecTimeout = TimeSpan.FromSeconds(10);

        public NetworkChangeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN20a/network-loss-connected-disconnects-0
        [Fact]
        public async Task RTN20a_NetworkLossConnectedDisconnects()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id-" + attempt,
                    connectionKey: "connection-key-" + attempt));
            });

            var client = RealtimeClient(mockWs, configure: NoAutomaticMonitoring);

            // Recorded before connecting: the DISCONNECTED this test is about is transient, because the
            // retry after a network loss is immediate, so it cannot be waited for.
            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // The target of this wait is CONNECTED and the client is still CONNECTED while it is being
            // set up, so AwaitConnectionState would return at once and everything after it would run
            // against the state before the network event. NextConnectionState waits for the fresh entry,
            // and is registered before the event is raised.
            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, SpecTimeout);

            // The spec's mock_network.simulate_network_lost().
            UtsClients.NotifyNetworkState(client.Connection, NetworkState.Offline);

            await reconnected;

            var observed = UtsClients.Snapshot(stateChanges);

            UtsClients.ContainsInOrder(
                observed.Select(change => change.Current),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            var disconnectedChange = observed.FirstOrDefault(
                change => change.Current == ConnectionState.Disconnected);
            disconnectedChange.Should().NotBeNull();
            disconnectedChange.Reason.Should().NotBeNull();

            Volatile.Read(ref connectionAttemptCount).Should().Be(2);
        }

        // UTS: realtime/unit/RTN20a/network-loss-connecting-disconnects-1
        [Fact]
        public async Task RTN20a_NetworkLossConnectingDisconnects()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                if (attempt == 1)
                {
                    // Deliberately unanswered, as the spec asks: the client stays CONNECTING while the
                    // network-loss event fires. The mock parks an unanswered attempt rather than failing
                    // it, so nothing here times out on its own.
                    return;
                }

                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id",
                    connectionKey: "connection-key"));
            });

            var client = RealtimeClient(mockWs, configure: NoAutomaticMonitoring);

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();

            // The spec's AWAIT_STATE connecting. CONNECTING is emitted before the transport is built, so
            // waiting for the state alone can return before the mock has seen anything — poll for the
            // attempt, which is the premise the next step depends on.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count == 1,
                "the first connection attempt to reach the mock");
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            var connected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, SpecTimeout);

            // The spec's mock_network.simulate_network_lost(), while still CONNECTING.
            UtsClients.NotifyNetworkState(client.Connection, NetworkState.Offline);

            await connected;

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            // The first connection attempt was abandoned and a second one made.
            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterOrEqualTo(2);
        }

        // UTS: realtime/unit/RTN20b/network-available-disconnected-connects-0
        [Fact]
        public async Task RTN20b_NetworkAvailableDisconnectedConnects()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                if (attempt == 1)
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-1",
                        connectionKey: "connection-key-1"));
                }
                else if (attempt == 2)
                {
                    // The reconnection after the forced drop fails, which is what leaves the client in
                    // DISCONNECTED with the long retry timer armed.
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id-2",
                        connectionKey: "connection-key-2"));
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                NoAutomaticMonitoring(options);

                // The spec's enable_fake_timers() plus disconnectedRetryTimeout: 30000. There is no
                // timer seam in this SDK, so the 30s stays a real interval that is simply never waited
                // out: every wait below is bounded by the helpers' 5s deadline, and reaching CONNECTED
                // inside it is exactly the spec's proof that the network event bypassed the retry timer.
                options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(30);
            });

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec reads the connection off the CONNECTION_SUCCESS event; there is exactly one
            // established connection at this point, which is what ActiveConnection is.
            mockWs.SimulateDisconnect();

            // NOTE: taken from the spec's prose ("wait for the client to reach DISCONNECTED after the
            // failed reconnection") rather than from its one-line AWAIT_STATE. A drop out of CONNECTED
            // is retried instantly per RTN15a, so the first DISCONNECTED is transient and the client is
            // CONNECTING again before any await returns. The state the spec means is the one after the
            // refused second attempt — the one holding the 30s timer.
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2
                      && client.Connection.State == ConnectionState.Disconnected,
                "the client to settle in DISCONNECTED after the refused reconnection");

            var attemptsBefore = Volatile.Read(ref connectionAttemptCount);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected);

            // The spec's mock_network.simulate_network_available().
            UtsClients.NotifyNetworkState(client.Connection, NetworkState.Online);

            await reconnected;

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected).Should().BeTrue();

            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterThan(attemptsBefore);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTN20c/network-available-connecting-restarts-0
        [Fact]
        public async Task RTN20c_NetworkAvailableConnectingRestarts()
        {
            var connectionAttemptCount = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                var attempt = Interlocked.Increment(ref connectionAttemptCount);
                if (attempt == 1)
                {
                    // Left pending, as the spec asks: a slow connection the network event arrives during.
                    return;
                }

                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                    connectionId: "connection-id",
                    connectionKey: "connection-key"));
            });

            var client = RealtimeClient(mockWs, configure: NoAutomaticMonitoring);

            // NOTE: the spec's setup records state changes but its assertion block never reads them, and
            // its implementation note says either a straight CONNECTING restart or a trip through
            // DISCONNECTED satisfies RTN20c. No recorder here, so the test does not pin a shape the spec
            // deliberately left open. This SDK takes the first route: SetState early-returns on a
            // CONNECTING-to-CONNECTING transition, so no state change is emitted at all and the restart
            // is observable only as the new transport.
            client.Connect();

            // Polled on the handler's own counter, not on mockWs.ConnectionAttempts.Count: the mock
            // records an attempt on its timeline *before* invoking the handler, so the published
            // count reaching 1 does not yet imply the handler has run. Waiting on the count and then
            // asserting the counter is a race that reads 0.
            await UtsClients.PollUntil(
                () => Volatile.Read(ref connectionAttemptCount) == 1,
                "the first connection attempt to reach the handler");
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            var connected = UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, SpecTimeout);

            // The spec's mock_network.simulate_network_available(), while still CONNECTING.
            UtsClients.NotifyNetworkState(client.Connection, NetworkState.Online);

            await connected;

            // The first connection attempt was abandoned and a new one made.
            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterOrEqualTo(2);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        /// <summary>
        /// The spec's install_mock(mock_network): the library's own OS subscription is off, so the only
        /// network events these tests see are the ones they raise.
        /// </summary>
        /// <param name="options">The options being built for this test's client.</param>
        private static void NoAutomaticMonitoring(ClientOptions options)
        {
            options.AutomaticNetworkStateMonitoring = false;
        }
    }
}
