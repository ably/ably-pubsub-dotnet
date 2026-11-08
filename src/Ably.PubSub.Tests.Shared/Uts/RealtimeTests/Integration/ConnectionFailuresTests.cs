// Realtime Connection Failures Integration Tests (RTN14a, RTN14g)
// Derived from uts/realtime/integration/connection/connection_failures_test.md
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
    public class ConnectionFailuresTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ConnectionFailuresTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTN14a/invalid-key-failed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14a")]
        public async Task RTN14a_InvalidKeyFailed()
        {
            // Setup
            var client = Realtime(new ClientOptions { Key = "invalid.key:secret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Failed);
            var error = client.Connection.ErrorReason;
            error.Should().NotBeNull();
            error.Code.Should().BeOneOf(40005, 40101);
            ((int)error.StatusCode.Value).Should().BeOneOf(401, 404);

            await CloseClient(client);
        }

        // UTS: realtime/integration/RTN14g/revoked-key-failed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN14g")]
        public async Task RTN14g_RevokedKeyFailed()
        {
            // Setup
            // Use a key with a valid format but non-existent app
            var client = Realtime(new ClientOptions { Key = "nonexistent.keyname:keysecret", AutoConnect = false });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Failed, TimeSpan.FromSeconds(15));

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Failed);
            var error = client.Connection.ErrorReason;
            error.Should().NotBeNull();

            // Server returns 40005 (invalid key) or similar non-token error
            (error.Code < 40140 || error.Code >= 40150).Should().BeTrue("the error code {0} is not a token error (40140-40149)", error.Code);

            await CloseClient(client);
        }
    }
}
