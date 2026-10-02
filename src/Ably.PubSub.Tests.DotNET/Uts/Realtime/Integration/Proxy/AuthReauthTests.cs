using System;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Proxy
{
    /// <summary>
    /// Derived from uts/realtime/integration/proxy/auth_reauth.md in ably/specification.
    ///
    /// Spec points: RTN22, RTC8a
    ///
    /// <para>
    /// The server can ask a connected client to re-authenticate by sending it an AUTH, and the
    /// client has to fetch a new token and send an AUTH back without the connection going
    /// anywhere. Here the AUTH is injected into a real connection by the proxy, so what is tested
    /// is the SDK's handling of a frame arriving unprompted mid-connection.
    /// </para>
    /// </summary>
    public class AuthReauthTests : UtsProxyTestBase
    {
        /// <summary>The AUTH protocol action, TR2's 17.</summary>
        private const int AuthAction = 17;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        public AuthReauthTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN22/server-initiated-reauth-0
        [ProxyFact]
        public async Task RTN22_AServerInitiatedAuthReauthenticatesWithoutDisturbingTheConnection()
        {
            var session = await ProxySession();

            // The callback is wrapped so the test can count invocations; the inner one is the
            // base class's locally-signed JWT, which costs no round trip through the session.
            var inner = await JwtAuthCallback();
            var callbackCount = 0;

            var client = ProxyRealtimeClient(session, tokenParams =>
            {
                Interlocked.Increment(ref callbackCount);
                return inner(tokenParams);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var originalConnectionId = client.Connection.Id;
            var countBefore = Volatile.Read(ref callbackCount);

            originalConnectionId.Should().NotBeNullOrEmpty();
            countBefore.Should().BeGreaterOrEqualTo(1, "connecting authenticated once");

            var states = UtsClients.RecordConnectionStates(client.Connection);

            await session.TriggerAction(new JObject
            {
                ["type"] = "inject_to_client",
                ["message"] = new JObject { ["action"] = AuthAction },
            });

            await UtsClients.PollUntil(
                () => Volatile.Read(ref callbackCount) > countBefore,
                "RTC8a - the re-authentication to fetch a new token",
                ConnectTimeout);

            Volatile.Read(ref callbackCount).Should().Be(
                countBefore + 1,
                "RTN22 - one AUTH, one re-authentication");

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RTN22 - re-authenticating is not a reconnection");

            client.Connection.Id.Should().Be(
                originalConnectionId,
                "the connection was never torn down");

            // Usually empty; an RTN24 CONNECTED update is the only entry allowed to appear.
            // OnlyContain would fail on the empty case, which is the better of the two.
            UtsClients.Snapshot(states).Should().NotContain(
                state => state != ConnectionState.Connected,
                "RTN22 - re-authenticating does not take the connection anywhere");

            // Polled, not read once. The auth callback returns before the AUTH frame it feeds is
            // written to the socket, so the callback count going up does not mean the frame has
            // left yet.
            await UtsSandbox.WallClockPollUntil(
                async () => ProxyLog.FramesToServer(await session.GetLog(), AuthAction).Count > 0,
                "RTC8a - the client's own AUTH on the wire");

            var sentAuth = ProxyLog.FramesToServer(await session.GetLog(), AuthAction);

            sentAuth[0]["message"]["auth"].Should().NotBeNull(
                "RTC8a - carrying an AuthDetails with the new token");
        }
    }
}
