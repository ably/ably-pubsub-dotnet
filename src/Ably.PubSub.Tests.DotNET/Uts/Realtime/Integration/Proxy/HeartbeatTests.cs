using System;
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
    /// Derived from uts/realtime/integration/proxy/heartbeat.md in ably/specification.
    ///
    /// Spec point: RTN23a
    ///
    /// <para>
    /// The unit tier tests RTN23a's idle timer against a mock. This tests the thing the mock
    /// stands in for: a real transport that really goes away mid-connection, and the real
    /// reconnect that follows, with both WebSocket connections visible in the proxy's log.
    /// </para>
    /// </summary>
    public class HeartbeatTests : UtsProxyTestBase
    {
        /// <summary>
        /// A connection that has to cross the network to the sandbox and back needs more than the
        /// unit tier's five seconds; the spec allows fifteen.
        /// </summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The reconnect has to wait out disconnectedRetryTimeout as well as connecting, so the
        /// spec allows thirty.
        /// </summary>
        private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(30);

        public HeartbeatTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN23a/heartbeat-starvation-reconnect-0
        [ProxyFact]
        public async Task RTN23a_ATransportThatGoesAwayIsDetectedAndReconnected()
        {
            // The close fires once, two seconds in - long enough for the real CONNECTED to arrive
            // first, and leaving the second WebSocket untouched so the reconnect can succeed.
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "delay_after_ws_connect",
                        ["delayMs"] = 2000,
                    },
                    ["action"] = new JObject { ["type"] = "close" },
                    ["times"] = 1,
                    ["comment"] = "RTN23a: close the WebSocket after 2s",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var firstConnectionId = client.Connection.Id;
            firstConnectionId.Should().NotBeNullOrEmpty();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, ConnectTimeout);

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.Key.Should().NotBeNullOrEmpty();

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue("the transport failure was detected and recovered from");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Count.Should().BeGreaterOrEqualTo(
                2,
                "a second WebSocket had to be opened");

            // RTN15c: the second attempt resumes rather than starting fresh.
            var resume = connects[1]["queryParams"]?["resume"];
            resume.Should().NotBeNull("RTN15c - the reconnect carries the resume key");
        }
    }
}
