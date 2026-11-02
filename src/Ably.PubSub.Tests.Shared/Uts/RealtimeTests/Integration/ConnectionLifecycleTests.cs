// Realtime Connection Lifecycle Integration Tests (RTN4b, RTN4c, RTN11, RTN12, RTN12a, RTN21)
// Derived from uts/realtime/integration/connection_lifecycle_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class ConnectionLifecycleTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ConnectionLifecycleTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTN4b/successful-connection-0
        [Fact(Skip = "UTS spec error RTN4b: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN4b")]
        public Task RTN4b_SuccessfulConnection()
        {
            // SPEC ERROR RTN4b: the test sets no autoConnect (TO3e: autoConnect defaults to true, so the client connects at construction), yet asserts INITIALIZED and then calls connect().
            // Fix the UTS spec first (autoConnect: false) - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN4b - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }

        // UTS: realtime/integration/RTN4c/graceful-close-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN4c", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN4c")]
        public async Task RTN4c_GracefulClose()
        {
            // Setup
            var client = Realtime();

            // Establish connection first
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Close the connection
            client.Connection.Close();

            // Should transition through CLOSING
            // NOTE: AWAIT_STATE closing could miss a transient state; the states are recorded before close() and verified in order below
            // Should reach CLOSED
            await AwaitConnectionState(client.Connection, ConnectionState.Closed, TimeSpan.FromSeconds(5));
            UtsAssert.ContainsInOrder(states.Snapshot(), new[] { ConnectionState.Closing, ConnectionState.Closed });

            // Assertions
            // Final state is CLOSED
            client.Connection.State.Should().Be(ConnectionState.Closed);

            // No error reason (clean close)
            client.Connection.ErrorReason.Should().BeNull();

            // Connection ID is cleared
            client.Connection.Id.Should().BeNull();

            // Connection key is cleared
            client.Connection.Key.Should().BeNull();
        }

        // UTS: realtime/integration/RTN11/connect-reconnect-cycle-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN11")]
        public async Task RTN11_ConnectReconnectCycle()
        {
            // Setup
            // Don't connect automatically
            var client = Realtime(new ClientOptions { AutoConnect = false });

            // Test Steps
            // Initial state
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // First connection
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var firstConnectionId = client.Connection.Id;

            // Close connection
            client.Connection.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);

            // Reconnect
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var secondConnectionId = client.Connection.Id;

            // Assertions
            // Successfully connected twice
            secondConnectionId.Should().NotBeNull();

            // Each connection gets a new ID (not a resume)
            firstConnectionId.Should().NotBe(secondConnectionId);

            // No errors
            client.Connection.ErrorReason.Should().BeNull();

            await CloseClient(client);
        }
    }
}
