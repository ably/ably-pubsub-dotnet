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
    /// Derived from uts/realtime/integration/proxy/connection_open_failures.md in
    /// ably/specification.
    ///
    /// Spec points: RTN14a, RTN14b, RTN14c, RTN14d, RTN14g
    ///
    /// <para>
    /// Five ways a connection attempt can go wrong, sorted by what the client should do about it:
    /// give up (RTN14a, RTN14g), re-authenticate and try again (RTN14b), or wait and try again
    /// (RTN14c, RTN14d). The unit tier covers the same five against a mock; here the failure is
    /// injected into a real handshake with a real server behind it.
    /// </para>
    ///
    /// <para>
    /// The specs build three of these clients with <c>key: api_key</c>. That cannot work through
    /// the session, which speaks plain HTTP: RSC18 makes the SDK refuse basic auth over non-TLS
    /// before a frame is written. Every client here authenticates with a callback, as the base
    /// class requires and as the spec's own notes acknowledge elsewhere in the tier.
    /// </para>
    ///
    /// <para>
    /// A3 applies throughout: the spec's <c>connection.id IS null</c> is an empty string here, not
    /// a null, because <c>ClearKeyAndId</c> assigns <c>string.Empty</c>. The assertions ask
    /// whether there is an id, which is the question the spec is asking.
    /// </para>
    /// </summary>
    public class ConnectionOpenFailuresTests : UtsProxyTestBase
    {
        /// <summary>The ERROR protocol action.</summary>
        private const int ErrorAction = 9;

        private static readonly TimeSpan FailTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(30);

        public ConnectionOpenFailuresTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN14a/fatal-connect-error-0
        [ProxyFact]
        public async Task RTN14a_AFatalErrorDuringOpenFailsTheConnection()
        {
            var session = await ProxySession(new JArray
            {
                ReplaceConnectedWithError(
                    40005, 400, "Invalid key", "RTN14a: fatal ERROR instead of CONNECTED"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, FailTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(40005);
            client.Connection.ErrorReason.StatusCode.Should().Be(
                System.Net.HttpStatusCode.BadRequest);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Failed)
                .Should().BeTrue("RTN14a - a fatal error is not retried");

            client.Connection.Id.Should().BeNullOrEmpty("no CONNECTED ever arrived");
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/proxy/RTN14g/server-error-causes-failed-0
        //
        // DEVIATION, D22 - found at the unit tier and confirmed here against a real handshake.
        // HandleConnectingErrorCommand treats a 500-504 status as a recoverable transport failure
        // even when the ERROR carries no channel, so the connection went DISCONNECTED, retried,
        // and reached CONNECTED rather than FAILED. Measured: current state Connected after
        // fifteen seconds of waiting for Failed. See Uts/deviations.md.
        [ProxyDeviationFact]
        public async Task RTN14g_AConnectionLevelServerErrorFailsTheConnection()
        {
            var session = await ProxySession(new JArray
            {
                ReplaceConnectedWithError(
                    50000, 500, "Internal server error", "RTN14g: 5xx ERROR during open"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, FailTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            client.Connection.ErrorReason.StatusCode.Should().Be(
                System.Net.HttpStatusCode.InternalServerError);
            client.Connection.ErrorReason.Message.Should().Be("Internal server error");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Failed)
                .Should().BeTrue(
                    "RTN14g - everything outside the token range fails, 5xx included");

            client.Connection.Id.Should().BeNullOrEmpty();
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/proxy/RTN14b/token-error-renew-reconnect-0
        [ProxyFact]
        public async Task RTN14b_ATokenErrorIsRenewedAndTheConnectionRetried()
        {
            var session = await ProxySession(new JArray
            {
                ReplaceConnectedWithError(
                    40142, 401, "Token expired", "RTN14b: token error on the first connect"),
            });

            var callbackCount = 0;
            var client = ProxyRealtimeClient(
                session,
                await TokenAuthCallback(onInvoked: () => Interlocked.Increment(ref callbackCount)));

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.Key.Should().NotBeNullOrEmpty();

            Volatile.Read(ref callbackCount).Should().BeGreaterOrEqualTo(
                2,
                "RTN14b - the token error was answered with a new token, not a failure");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue();

            ProxyLog.WsConnects(await session.GetLog()).Count.Should().BeGreaterOrEqualTo(
                2,
                "the retry opened a second WebSocket");
        }

        // UTS: realtime/proxy/RTN14d/retry-after-refused-0
        [ProxyFact]
        public async Task RTN14d_ARefusedConnectionIsRetried()
        {
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject { ["type"] = "ws_connect", ["count"] = 1 },
                    ["action"] = new JObject { ["type"] = "refuse_connection" },
                    ["times"] = 1,
                    ["comment"] = "RTN14d: refuse the first WebSocket",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback(), options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(2000));

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.Key.Should().NotBeNullOrEmpty();

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue("RTN14d - a refused transport is recoverable, so it is retried");

            ProxyLog.WsConnects(await session.GetLog()).Count.Should().BeGreaterOrEqualTo(
                1,
                "the refused attempt may or may not be logged as a connect; the successful one is");
        }

        // UTS: realtime/proxy/RTN14c/connection-timeout-0
        [ProxyFact]
        public async Task RTN14c_NoConnectedWithinTheDeadlineDisconnects()
        {
            // No times limit: every CONNECTED is suppressed, so the retry times out too and the
            // connection stays where the test wants it.
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "ws_frame_to_client",
                        ["action"] = "CONNECTED",
                    },
                    ["action"] = new JObject { ["type"] = "suppress" },
                    ["comment"] = "RTN14c: never let a CONNECTED through",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback(), options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(3000);

                // Long enough that the connection sits in DISCONNECTED while it is asserted on.
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, FailTimeout);

            client.Connection.State.Should().Be(ConnectionState.Disconnected);
            client.Connection.ErrorReason.Should().NotBeNull();

            var reason = client.Connection.ErrorReason;
            var saysTimeout = (reason.Message ?? string.Empty)
                .IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

            (saysTimeout || reason.Code == 50003 || reason.Code == 80003).Should().BeTrue(
                "RTN14c - the caller has to be able to tell this was a timeout; got " + reason);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Disconnected)
                .Should().BeTrue();

            client.Connection.Id.Should().BeNullOrEmpty("CONNECTED never arrived");
            client.Connection.Key.Should().BeNullOrEmpty();
        }

        private static JObject ReplaceConnectedWithError(
            int code,
            int statusCode,
            string message,
            string comment)
            => new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "ws_frame_to_client",
                    ["action"] = "CONNECTED",
                },
                ["action"] = new JObject
                {
                    ["type"] = "replace",
                    ["message"] = new JObject
                    {
                        ["action"] = ErrorAction,
                        ["error"] = new JObject
                        {
                            ["code"] = code,
                            ["statusCode"] = statusCode,
                            ["message"] = message,
                        },
                    },
                },
                ["times"] = 1,
                ["comment"] = comment,
            };
    }
}
