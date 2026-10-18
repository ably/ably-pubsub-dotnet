// Realtime Token Request Integration Tests (RSA9, RSA9a, RSA9g)
// Derived from uts/realtime/integration/auth/token_request_test.md
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
    public class TokenRequestTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public TokenRequestTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RSA9a/token-request-server-accepted-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA9a")]
        public async Task RSA9a_TokenRequestServerAccepted()
        {
            // Setup
            // Client A creates TokenRequests using the API key
            var creator = Rest(new ClientOptions { Key = ApiKey });

            // Client B connects using TokenRequests from client A
            // NOTE: CreateTokenRequestAsync returns the signed request as a JSON string; a string callback result is read as TokenRequest JSON (RSA8d)
            var client = Realtime(new ClientOptions
            {
                AuthCallback = async tokenParams =>
                {
                    var tokenRequest = await creator.Auth.CreateTokenRequestAsync(null, null);
                    return tokenRequest;
                },
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNull();
            client.Connection.ErrorReason.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/integration/RSA9/token-request-with-clientid-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA9")]
        public async Task RSA9_TokenRequestWithClientid()
        {
            // Setup
            var testClientId = "token-request-client-" + RandomId();

            var creator = Rest(new ClientOptions { Key = ApiKey });

            // NOTE: CreateTokenRequestAsync returns the signed request as a JSON string; a string callback result is read as TokenRequest JSON (RSA8d)
            var client = Realtime(new ClientOptions
            {
                AuthCallback = async tokenParams =>
                {
                    var tokenRequest = await creator.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = testClientId }, null);
                    return tokenRequest;
                },
                ClientId = testClientId,
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Auth.ClientId.Should().Be(testClientId);

            await CloseClient(client);
        }
    }
}
