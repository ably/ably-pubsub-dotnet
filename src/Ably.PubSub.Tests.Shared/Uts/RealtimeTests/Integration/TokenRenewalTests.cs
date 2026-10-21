// Realtime Token Renewal Integration Tests (RSA4b, RTN14b)
// Derived from uts/realtime/integration/auth/token_renewal_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
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
    public class TokenRenewalTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 90000;

        public TokenRenewalTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RSA4b/token-renewal-on-expiry-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA4b")]
        public async Task RSA4b_TokenRenewalOnExpiry()
        {
            // Setup
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);
            var callbackCount = 0;

            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var client = Realtime(new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    if (Interlocked.Increment(ref callbackCount) == 1)
                    {
                        // First token: very short TTL (5 seconds)
                        return Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret, TimeSpan.FromMilliseconds(5000))));
                    }

                    // Subsequent tokens: long TTL
                    return Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret, TimeSpan.FromMilliseconds(3600000))));
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record the initial connection ID
            var initialConnectionId = client.Connection.Id;
            Volatile.Read(ref callbackCount).Should().Be(1);

            // Wait for token to expire and the client to recover.
            // The server will send a DISCONNECTED with a token error once the token
            // expires. The client should automatically renew and reconnect.
            await UtsWait.PollUntil(
                () => Volatile.Read(ref callbackCount) >= 2,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(1000),
                description: "authCallback invoked a second time");

            // Wait for reconnection
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            // authCallback was invoked at least twice (initial + renewal)
            Volatile.Read(ref callbackCount).Should().BeGreaterThanOrEqualTo(2);

            // Client is connected
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // NOTE: initial_connection_id is recorded by the spec but never asserted
            _ = initialConnectionId;
            await CloseClient(client);
        }
    }
}
