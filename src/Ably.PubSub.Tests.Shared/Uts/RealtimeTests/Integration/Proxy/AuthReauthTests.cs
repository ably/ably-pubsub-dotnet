// Auth Re-authorization Proxy Integration Tests (RTN22, RTC8a)
// Derived from uts/realtime/integration/proxy/auth_reauth.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/connection/server_initiated_reauth_test.md (RTN22, RTN22a), uts/realtime/unit/auth/realtime_authorize.md (RTC8a, RTC8a1), uts/realtime/unit/auth/connection_auth_test.md (RSA4c3)
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
using Ably.PubSub.Tests.Uts.Infra.Integration.Proxy;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration.Proxy
{
    /// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("type", "integration")]
    public class AuthReauthTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 60000;

        public AuthReauthTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN22/server-initiated-reauth-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTN22")]
        public async Task RTN22_ServerInitiatedReauth()
        {
            // Setup
            // Proxy rules: None (passthrough). The AUTH injection is triggered imperatively after the SDK connects.
            var session = await CreateProxySession();

            // SDK config: Use authCallback so re-authentication can be observed. The callback generates a JWT token from the sandbox key parts.
            var authCallbackCount = 0;
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);

            // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = tokenParams =>
                {
                    Interlocked.Increment(ref authCallbackCount);

                    // Generate a JWT token signed with the sandbox key
                    var jwt = GenerateJwt(keyName, keySecret);
                    return Task.FromResult<object>(new TokenDetails(jwt));
                },
                AutoConnect = false,
            });

            // Test Steps
            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Record identity and auth state before injection
            var originalConnectionId = client.Connection.Id;
            var originalAuthCallbackCount = Volatile.Read(ref authCallbackCount);
            originalConnectionId.Should().NotBeNull();
            originalAuthCallbackCount.Should().BeGreaterThanOrEqualTo(1);

            // Record state changes from this point
            var stateChanges = UtsRecord.ConnectionStates(client.Connection);

            // Inject a server-initiated AUTH ProtocolMessage (action 17)
            // This simulates Ably requesting re-authentication
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject { ["action"] = ProtocolMessages.Auth }));

            // Wait for the SDK to process the AUTH and send its response
            // The authCallback should be invoked, and the SDK should send AUTH back.
            // Allow time for the token request round-trip to the sandbox.
            await UtsWait.PollUntil(
                () => Volatile.Read(ref authCallbackCount) > originalAuthCallbackCount,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "authCallback invoked again after the injected AUTH");

            // Assertions
            // authCallback was called again (re-authentication triggered)
            Volatile.Read(ref authCallbackCount).Should().Be(originalAuthCallbackCount + 1);

            // Connection remains CONNECTED (re-auth does not disrupt the connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Connection ID is unchanged (no reconnection occurred)
            client.Connection.Id.Should().Be(originalConnectionId);

            // No state transitions away from CONNECTED occurred
            var nonConnectedChanges = stateChanges.Snapshot().Where(s => s != ConnectionState.Connected).ToList();
            nonConnectedChanges.Should().BeEmpty();

            // Proxy log shows the SDK sent an AUTH frame (action 17) from client to server
            // NOTE: timing adaptation: the authCallback runs before the AUTH frame is sent, so the log is polled for the frame rather than read once
            var clientAuthFrames = await UtsWait.PollUntil<List<ProxyEvent>>(
                async () =>
                {
                    var log = await session.GetLogAsync();
                    var frames = log.FramesToServer(ProtocolMessages.Auth).Where(e => e.Message["auth"] != null && e.Message["auth"].Type != JTokenType.Null).ToList();
                    return frames.Count >= 1 ? frames : null;
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "proxy log shows an AUTH frame with auth sent to the server");
            clientAuthFrames.Count.Should().BeGreaterThanOrEqualTo(1);

            // Cleanup
            await CloseClient(client);
        }
    }
}
