using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration
{
    /// <summary>
    /// Derived from uts/realtime/integration/auth.md in ably/specification.
    ///
    /// Spec points: RTC8a, RTC8c, RSA8, RSA7
    ///
    /// Three translation notes apply to every test in the file.
    ///
    /// The specs' <c>auth_callback</c> returns the output of <c>generate_jwt</c> directly. .NET
    /// cannot take it that way: a bare string handed back from an <c>AuthCallback</c> is routed
    /// through <c>JsonHelper.Deserialize&lt;TokenRequest&gt;()</c> in AblyAuth's
    /// <c>GetTokenRequest</c>, which throws on a JWT because a JWT is not JSON. The JWT is
    /// therefore wrapped in a <see cref="TokenDetails"/>, which is the shape the repo's own
    /// JwtSandboxSpec already uses for the same thing.
    ///
    /// <c>Key</c> is cleared in each test because the specs' ClientOptions carry no key - only an
    /// authCallback - and leaving the sandbox key in place would give the client a second way to
    /// authenticate.
    ///
    /// The specs' <c>AWAIT client.close()</c> is <c>Close()</c> plus a wait for CLOSED: .NET's
    /// <c>Close()</c> is void and only sends a CLOSE, so the await is the state wait.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class AuthTests : UtsRealtimeIntegrationTestBase
    {
        /// <summary>The specs' <c>ttl: 3600000</c>, in the milliseconds the spec states it in.</summary>
        private static readonly TimeSpan TokenTtl = TimeSpan.FromMilliseconds(3600000);

        public AuthTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTC8a/in-band-reauth-connected-0
        [Fact]
        public async Task RTC8a_InBandReauthConnected()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, TokenTtl)));
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Record connection ID before reauth.
            var connectionIdBefore = client.Connection.Id;

            // Collect state changes during reauth. Registered after CONNECTED, so only what the
            // reauth itself produces is recorded - which is what the spec's subscription does.
            var stateChanges = UtsClients.RecordConnectionStateChanges(client.Connection);

            // Call authorize - should send AUTH and get UPDATE, not disconnect.
            var token = await client.Auth.AuthorizeAsync();

            // Check state after reauth.
            var connectionIdAfter = client.Connection.Id;

            token.Should().NotBeNull();

            // "token.token IS String" is static here - TokenDetails.Token is typed string - so the
            // runnable half of the spec's assertion is that it carries a value.
            token.Token.Should().NotBeNullOrEmpty();

            // Connection remained connected - same connection ID.
            connectionIdAfter.Should().Be(connectionIdBefore);

            // No state transitions occurred: an UPDATE has Current == Previous == Connected, so
            // filtering for actual transitions should yield nothing.
            var transitions = UtsClients.Snapshot(stateChanges)
                .Where(change => change.Current != change.Previous)
                .ToList();
            transitions.Should().BeEmpty();

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }

        // UTS: realtime/integration/RTC8c/authorize-initiates-connection-0
        [Fact]
        public async Task RTC8c_AuthorizeInitiatesConnection()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, TokenTtl)));
                options.AutoConnect = false;
            });

            // Client starts in INITIALIZED, no connection.
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // authorize() should trigger connection.
            var token = await client.Auth.AuthorizeAsync();

            // Wait for connection to be established.
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            token.Should().NotBeNull();
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }

        // UTS: realtime/integration/RSA8/token-auth-connect-0
        [Fact]
        public async Task RSA8_TokenAuthConnect()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, TokenTtl)));
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.ErrorReason.Should().BeNull();

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }

        // UTS: realtime/integration/RSA7/matching-clientid-succeeds-0
        [Fact]
        public async Task RSA7_MatchingClientIdSucceeds()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            var testClientId = "test-client-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, TokenTtl, testClientId)));
                options.ClientId = testClientId;
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Auth.ClientId.Should().Be(testClientId);

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }

        // UTS: realtime/integration/RSA7/mismatched-clientid-fails-1
        [Fact]
        public async Task RSA7_MismatchedClientIdFails()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            // NOTE: this spec section is internally inconsistent, and the Assertions block is the
            // half translated here. Its Test Steps say "EXPECT THROW creating Realtime(...)", but
            // its Assertions block overrides that in words: "The mismatch is detected client-side
            // when the token is obtained. The exact behavior depends on implementation: it may
            // throw during authorize() or during token validation. The key assertion is that the
            // connection enters FAILED state with error code 40102." Construction cannot be the
            // detection point in any implementation - with autoConnect false, no token has been
            // fetched by the time the constructor returns, so there is nothing to compare a
            // clientId against - so the construction-throws reading is the one discarded.
            // Reported as a suspected spec error.
            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(
                    new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, TokenTtl, "token-client-id")));
                options.ClientId = "wrong-client-id";
                options.AutoConnect = false;
            });

            // Registered before the connect is provoked: an SDK call starts its work eagerly here.
            var failed = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(15));

            client.Connect();
            await failed;

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40102);
        }
    }
}
