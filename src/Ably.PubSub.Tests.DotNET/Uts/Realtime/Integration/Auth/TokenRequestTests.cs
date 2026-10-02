using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Auth
{
    /// <summary>
    /// Derived from uts/realtime/integration/auth/token_request_test.md in ably/specification.
    ///
    /// Spec points: RSA9, RSA9a, RSA9g
    ///
    /// The specs' <c>creator.auth.createTokenRequest()</c> is <c>CreateTokenRequestAsync()</c>,
    /// which returns the signed TokenRequest already serialised to JSON rather than a
    /// <c>TokenRequest</c> object. That is the form .NET's <c>AuthCallback</c> accepts for a token
    /// request - AblyAuth's <c>GetTokenRequest</c> deserialises a returned string back into a
    /// <c>TokenRequest</c> and posts it to <c>/keys/{keyName}/requestToken</c> - so the callback
    /// returns it unchanged, which keeps the server as the thing that validates the HMAC.
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    /// <c>Key</c> is cleared on the connecting client because the specs' ClientOptions carry only an
    /// authCallback; the creator client is the one holding the API key.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class TokenRequestTests : UtsRealtimeIntegrationTestBase
    {
        public TokenRequestTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RSA9a/token-request-server-accepted-0
        [Fact]
        public async Task RSA9a_TokenRequestServerAccepted()
        {
            // Client A creates TokenRequests using the API key.
            var creator = await SandboxRestClient();

            // Client B connects using TokenRequests from client A.
            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = async tokenParams => await creator.Auth.CreateTokenRequestAsync();
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(15));

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.ErrorReason.Should().BeNull();

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }

        // UTS: realtime/integration/RSA9/token-request-with-clientid-0
        [Fact]
        public async Task RSA9_TokenRequestWithClientId()
        {
            var testClientId = "token-request-client-" + UtsSandbox.RandomId();

            var creator = await SandboxRestClient();

            var client = await SandboxRealtimeClient(configure: options =>
            {
                options.Key = null;
                options.AuthCallback = async tokenParams =>
                    await creator.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = testClientId });
                options.ClientId = testClientId;
                options.AutoConnect = false;
            });

            client.Connect();
            await AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(15));

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Auth.ClientId.Should().Be(testClientId);

            client.Close();
            await AwaitConnectionState(client.Connection, ConnectionState.Closed);
        }
    }
}
