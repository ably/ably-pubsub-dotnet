using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration
{
    /// <summary>
    /// Derived from uts/realtime/integration/connection_lifecycle_test.md in ably/specification.
    ///
    /// Spec points: RTN4b, RTN4c, RTN11, RTN12, RTN12a, RTN21
    ///
    /// Integration tier: every client here opens a real socket to the sandbox, so the waits are
    /// wall-clock. The spec's shorter per-transition budgets - one second for CONNECTED to CLOSING and
    /// five for CLOSING to CLOSED - are the budget a mock-backed test needs and are widened to this
    /// tier's ten seconds; a real CLOSE round trip on a shared CI runner does not reliably fit five.
    ///
    /// CONNECTING and CLOSING are both transient against a real server: the next transition can land
    /// in the same millisecond, so AWAIT_STATE on either is a coin toss that silently passes when it
    /// loses. Both are translated as a UtsClients.RecordConnectionStates recorder registered before
    /// the transition and read with CONTAINS_IN_ORDER, which is the shape mock_websocket.md prescribes
    /// for a transient state and also what a spec reading state_changes wants.
    ///
    /// The spec's CLOSE_CLIENT(client) and its "AFTER EACH TEST" cleanup are automatic:
    /// SandboxRealtimeClient registers the client with UtsIntegrationTestBase, which disposes it after
    /// every test including when the test throws.
    ///
    /// RTN21 is declared by the spec file but none of its test bodies assert it, so it is covered only
    /// implicitly - the connection is established over the library's default transport, which is a
    /// WebSocket. The spec file's gloss on RTN21 ("connections are initiated via WebSocket transport")
    /// also does not match the features spec, where RTN21 is about ConnectionDetails overriding the
    /// client library defaults; translated as the spec file has it and reported rather than rewritten.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ConnectionLifecycleTests : UtsRealtimeIntegrationTestBase
    {
        /// <summary>The spec's "CONNECTING to CONNECTED: 10 seconds", which is also this tier's default.</summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The spec allows five seconds for CLOSING to CLOSED. Widened to this tier's ten for the
        /// reason given in the class summary.
        /// </summary>
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

        public ConnectionLifecycleTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTN4b/successful-connection-0
        [Fact]
        public async Task RTN4b_SuccessfulConnection()
        {
            // NOTE: the spec's setup omits autoConnect while its first step asserts INITIALIZED and
            // only then calls connect(). TO3e defaults autoConnect to true, so read literally that
            // assertion races the library's own connect; autoConnect: false is the only reading under
            // which every assertion in the section holds, and the RTN11 section of the same spec file
            // states it explicitly. Reported as a spec error rather than silently dropped.
            var client = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            // Registered before connecting, because CONNECTING is transient and the recorder is the
            // only reliable witness to it.
            var recordedStates = UtsClients.RecordConnectionStates(client.Connection);

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            client.Connect();

            await AwaitConnectionState(client.Connection, ConnectionState.Connected, ConnectTimeout);

            var states = UtsClients.Snapshot(recordedStates);
            UtsClients.ContainsInOrder(states, ConnectionState.Connecting, ConnectionState.Connected)
                .Should().BeTrue(
                    "RTN4b requires INITIALIZED then CONNECTING then CONNECTED; observed " +
                    string.Join(", ", states));

            client.Connection.State.Should().Be(ConnectionState.Connected);

            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.Key.Should().NotBeNullOrEmpty();

            // The spec's regexes are unanchored and are translated as written: MatchRegex is
            // Regex.IsMatch, which is the same partial match the spec expresses. Anchoring them would
            // assert more than the spec does about the server's id and key formats.
            client.Connection.Id.Should().MatchRegex("[a-zA-Z0-9_-]+");
            client.Connection.Key.Should().MatchRegex("[a-zA-Z0-9_!-]+");

            client.Connection.ErrorReason.Should().BeNull();
        }

        // UTS: realtime/integration/RTN4c/graceful-close-0
        [Fact]
        public async Task RTN4c_GracefulClose()
        {
            // The spec's setup for this section does not pass autoConnect and does not assert
            // INITIALIZED, so the library default stands and the connect() below is the spec's step.
            var client = await SandboxRealtimeClient();

            // Registered before the close, because CLOSING is transient once the server's CLOSED
            // lands.
            var recordedStates = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, ConnectTimeout);

            client.Connection.Close();

            await AwaitConnectionState(client.Connection, ConnectionState.Closed, CloseTimeout);

            var states = UtsClients.Snapshot(recordedStates);
            UtsClients.ContainsInOrder(
                    states,
                    ConnectionState.Connected,
                    ConnectionState.Closing,
                    ConnectionState.Closed)
                .Should().BeTrue(
                    "RTN4c requires CONNECTED then CLOSING then CLOSED, and RTN12a's send-CLOSE-and-" +
                    "wait-for-confirmation is what puts CLOSING between them; observed " +
                    string.Join(", ", states));

            client.Connection.State.Should().Be(ConnectionState.Closed);

            // ADAPTED. The spec asserts `errorReason IS null` after a graceful close. This SDK
            // represents "no error, closed by the client" as a non-null ErrorInfo instead:
            // ConnectionClosedState sets `Error = error ?? ErrorInfo.ReasonClosed`, and ReasonClosed
            // carries ErrorCodes.NoError — a constant literally named for the absence of an error.
            // So the same fact is spelled as a value rather than as null.
            //
            // Adapted rather than env-gated because the representation is stable and deliberate, and
            // the governing doc prefers an assertion that runs: this one still catches a regression
            // that put a *real* error on a clean close, which is what the spec point is protecting.
            // Recorded under Adapted Tests in Uts/deviations.md.
            client.Connection.ErrorReason?.Code.Should().Be(
                ErrorCodes.NoError,
                "a graceful close must not report a real error; this SDK spells 'no error' as "
                + "ErrorInfo.ReasonClosed rather than as null");

            // ADAPTED, same reason as the ErrorReason assertion above. RTN8d and RTN9d say the
            // connection id and key are set to null; this connection had both while CONNECTED, and
            // RealtimeState.ConnectionData.ClearKeyAndId does clear them — to string.Empty rather
            // than to null. The behaviour the spec points require (no id, no key, so nothing to
            // resume with) holds; only the spelling of "absent" differs. Recorded once in
            // Uts/deviations.md under Adapted Tests, covering both sites.
            client.Connection.Id.Should().BeNullOrEmpty();
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/integration/RTN11/connect-reconnect-cycle-0
        [Fact]
        public async Task RTN11_ConnectReconnectCycle()
        {
            var client = await SandboxRealtimeClient(configure: options => options.AutoConnect = false);

            var recordedStates = UtsClients.RecordConnectionStates(client.Connection);

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, ConnectTimeout);

            var firstConnectionId = client.Connection.Id;

            client.Connection.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed, CloseTimeout);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, ConnectTimeout);

            var secondConnectionId = client.Connection.Id;

            secondConnectionId.Should().NotBeNullOrEmpty();

            // RTN8d clears the connection key on the way into CLOSED, so the second connect has
            // nothing to resume with and the server must issue a fresh connection id.
            secondConnectionId.Should().NotBe(firstConnectionId);

            client.Connection.ErrorReason.Should().BeNull();

            // The section's requirement table claims RTN4b for this test - "each connection follows
            // CONNECTING then CONNECTED" - which its Assertions block never checks. The recorder is
            // already in hand, so the claim is asserted here.
            var states = UtsClients.Snapshot(recordedStates);
            UtsClients.ContainsInOrder(
                    states,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Closing,
                    ConnectionState.Closed,
                    ConnectionState.Connecting,
                    ConnectionState.Connected)
                .Should().BeTrue(
                    "RTN11 with RTN4b requires each connect to run CONNECTING then CONNECTED; " +
                    "observed " + string.Join(", ", states));
        }
    }
}
