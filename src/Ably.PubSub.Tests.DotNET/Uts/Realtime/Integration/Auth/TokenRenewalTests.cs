using System;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Auth
{
    /// <summary>
    /// Derived from uts/realtime/integration/auth/token_renewal_test.md in ably/specification.
    ///
    /// Spec points: RSA4b, RTN14b
    ///
    /// The spec's <c>auth_callback</c> returns the output of <c>generate_jwt</c> directly. .NET
    /// cannot take it that way: a bare string handed back from an <c>AuthCallback</c> is routed
    /// through <c>JsonHelper.Deserialize&lt;TokenRequest&gt;()</c> in AblyAuth's
    /// <c>GetTokenRequest</c>, which throws on a JWT because a JWT is not JSON. The JWT is
    /// therefore wrapped in a <see cref="TokenDetails"/>, which is the shape the repo's own
    /// JwtSandboxSpec already uses for the same thing. Wrapping it without an <c>Expires</c> also
    /// keeps the test on the path the spec is about: the client has no client-side expiry to
    /// pre-empt with, so the renewal can only be driven by the server's DISCONNECTED.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class TokenRenewalTests : UtsRealtimeIntegrationTestBase
    {
        /// <summary>The spec's first-token <c>ttl: 5000</c>.</summary>
        private static readonly TimeSpan FirstTokenTtl = TimeSpan.FromMilliseconds(5000);

        /// <summary>The spec's subsequent-token <c>ttl: 3600000</c>.</summary>
        private static readonly TimeSpan RenewedTokenTtl = TimeSpan.FromMilliseconds(3600000);

        public TokenRenewalTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RSA4b/token-renewal-on-expiry-0
        [Fact]
        public async Task RSA4b_TokenRenewalOnExpiry()
        {
            var sandbox = await Sandbox();
            var keyName = UtsSandbox.ExtractKeyName(sandbox.KeyStr);
            var keySecret = UtsSandbox.ExtractKeySecret(sandbox.KeyStr);

            var callbackCount = 0;

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    var invocation = Interlocked.Increment(ref callbackCount);

                    // First token: very short TTL. Subsequent tokens: long TTL.
                    var ttl = invocation == 1 ? FirstTokenTtl : RenewedTokenTtl;
                    return Task.FromResult<object>(
                        new TokenDetails(UtsSandbox.GenerateJwt(keyName, keySecret, ttl)));
                };
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec records the initial connection ID here but never asserts on it. Asserting
            // the premise it documents instead - the connection is live and identified - rather
            // than leaving an unused local behind.
            client.Connection.Id.Should().NotBeNullOrEmpty();
            Volatile.Read(ref callbackCount).Should().Be(1);

            // Wait for the token to expire and the client to recover. The server sends a
            // DISCONNECTED carrying a token error once the token expires; the client should renew
            // through the authCallback automatically and reconnect.
            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(Volatile.Read(ref callbackCount) >= 2),
                "the authCallback to be invoked a second time for the renewal",
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromSeconds(1));

            // Wait for reconnection.
            await AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(15));

            // authCallback was invoked at least twice (initial + renewal).
            Volatile.Read(ref callbackCount).Should().BeGreaterOrEqualTo(2);

            client.Connection.State.Should().Be(ConnectionState.Connected);

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }
    }
}
