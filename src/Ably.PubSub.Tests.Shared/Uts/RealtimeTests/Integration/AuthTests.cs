// Realtime Auth Integration Tests (RTC8, RSA8, RSA7)
// Derived from uts/realtime/integration/auth.md
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
    public class AuthTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public AuthTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The spec's auth_callback: a long-lived JWT, optionally for a clientId
        // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
        private Func<TokenParams, Task<object>> JwtCallback(string clientId = null)
        {
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);
            return tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret, TimeSpan.FromMilliseconds(3600000), clientId)));
        }

        // UTS: realtime/integration/RTC8a/in-band-reauth-connected-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTC8a")]
        public async Task RTC8a_InBandReauthConnected()
        {
            // Setup
            var client = Realtime(new ClientOptions
            {
                AuthCallback = JwtCallback(),
                AutoConnect = false,
            });

            // Test Steps
            // Connect and wait for CONNECTED
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record connection ID before reauth
            var connectionIdBefore = client.Connection.Id;

            // Collect state changes during reauth
            var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);

            // Call authorize — should send AUTH and get UPDATE, not disconnect
            var token = await client.Auth.AuthorizeAsync();

            // Check state after reauth
            var connectionIdAfter = client.Connection.Id;

            // Assertions
            // authorize() returned a valid token
            token.Should().NotBeNull();

            // NOTE: no assertion: TokenDetails.Token is a string, guaranteed at compile time
            // ASSERT token.token IS String
            token.Token.Should().NotBeNull();

            // Connection remained connected — same connection ID
            connectionIdAfter.Should().Be(connectionIdBefore);

            // No state transitions occurred (UPDATE has current == previous == connected,
            // so filtering for actual transitions should yield nothing)
            var stateTransitions = stateChanges.Snapshot().Where(c => c.Current != c.Previous).ToList();
            stateTransitions.Should().BeEmpty();

            await CloseClient(client);
        }

        // UTS: realtime/integration/RTC8c/authorize-initiates-connection-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTC8c")]
        public async Task RTC8c_AuthorizeInitiatesConnection()
        {
            // Setup
            var client = Realtime(new ClientOptions
            {
                AuthCallback = JwtCallback(),
                AutoConnect = false,
            });

            // Test Steps
            // Client starts in INITIALIZED, no connection
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // authorize() should trigger connection
            var token = await client.Auth.AuthorizeAsync();

            // Wait for connection to be established
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            token.Should().NotBeNull();
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNull();

            await CloseClient(client);
        }

        // UTS: realtime/integration/RSA8/token-auth-connect-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA8")]
        public async Task RSA8_TokenAuthConnect()
        {
            // Setup
            var client = Realtime(new ClientOptions
            {
                AuthCallback = JwtCallback(),
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNull();
            client.Connection.ErrorReason.Should().BeNull();

            await CloseClient(client);
        }

        // UTS: realtime/integration/RSA7/matching-clientid-succeeds-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA7")]
        public async Task RSA7_MatchingClientidSucceeds()
        {
            // Setup
            var testClientId = "test-client-" + RandomId();

            var client = Realtime(new ClientOptions
            {
                AuthCallback = JwtCallback(testClientId),
                ClientId = testClientId,
                AutoConnect = false,
            });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Auth.ClientId.Should().Be(testClientId);

            await CloseClient(client);
        }

        // UTS: realtime/integration/RSA7/mismatched-clientid-fails-1
        [Fact(Skip = "UTS spec error RSA7: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA7")]
        public Task RSA7_MismatchedClientidFails()
        {
            // SPEC ERROR RSA7: the Test Steps expect creating the Realtime client to throw, but RSA15c says the mismatch is found following an auth request and the connection transitions to FAILED; the spec's own Assertions note says the same (FAILED with 40102).
            // Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSA7 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }
    }
}
