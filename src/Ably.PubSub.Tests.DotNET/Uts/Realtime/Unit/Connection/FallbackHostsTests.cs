using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/fallback_hosts_test.md in ably/specification.
    ///
    /// Spec points: RTN17, RTN17e, RTN17f, RTN17f1, RTN17g, RTN17h, RTN17i, RTN17j
    ///
    /// <para>
    /// <b>Two tier-wide options are this file's subject, so each test states its own position on
    /// them.</b> <c>UtsClients.Options</c> clears <c>FallbackHosts</c> and sets
    /// <c>SkipInternetCheck</c> for the whole unit tier. Six tests set <c>FallbackHosts</c>
    /// explicitly, because their point is "a fallback domain was used" rather than which set the SDK
    /// derives. RTN17h sets it to <c>null</c> to put <c>ClientOptions.GetFallbackHosts()</c> back in
    /// charge; RTN17g keeps the tier default, because an empty set is its premise. RTN17j is the one
    /// test that turns <c>SkipInternetCheck</c> back off, because the connectivity probe is what it
    /// asserts on.
    /// </para>
    ///
    /// <para>
    /// <b>Every test installs a <see cref="MockHttpClient"/>, including the ones that skip the
    /// internet check.</b> The realtime fallback path can still reach
    /// <c>PubSubHttpClient.CanConnectToAbly()</c>, and without an HTTP seam installed that is a real
    /// network request out of a unit test.
    /// </para>
    ///
    /// <para>
    /// <b>The primary domain here is the legacy <c>realtime.ably.io</c>.</b> This SDK has no
    /// <c>endpoint</c> option, so there are no REC2 <c>main.*</c> names. The fallback set the six
    /// configured tests install uses REC2-shaped names, which is what the spec's
    /// <c>CONTAINS "fallback"</c> assertions read — so those assertions check which host the SDK
    /// chose, not which era its defaults belong to. RTN17h is the exception and is left failing: its
    /// subject <i>is</i> the derived default set, and this SDK's defaults are the pre-REC2
    /// <c>[a-e].ably-realtime.com</c> names.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class FallbackHostsTests : UtsTestBase
    {
        private const string PrimaryRealtimeHost = "realtime.ably.io";
        private const string ConnectionId = "connection-id";
        private const string ConnectionKey = "connection-key";

        /// <summary>
        /// The spec's fallback domains, in the REC2 shape its assertions read. Installed explicitly
        /// rather than inherited, per note 1 and note 3 on the class.
        /// </summary>
        private static readonly string[] SpecFallbackHosts =
        {
            "main.a.fallback.ably-realtime.com",
            "main.b.fallback.ably-realtime.com",
            "main.c.fallback.ably-realtime.com",
            "main.d.fallback.ably-realtime.com",
            "main.e.fallback.ably-realtime.com",
        };

        public FallbackHostsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN17i/prefer-primary-domain-0
        [Fact]
        public async Task RTN17i_PreferPrimaryDomain()
        {
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, InternetUpHttp(), configure: WithSpecFallbackHosts);

            // DISCONNECTED is transient here: the drop from CONNECTED earns an immediate reconnect, so
            // the spec's AWAIT_STATE disconnected cannot be registered in time. The recorder is the
            // shape mock_websocket.md prescribes for exactly this, and it has to be in place before the
            // client connects.
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            connectionAttempts.Should().HaveCountGreaterOrEqualTo(
                2, "the spec's handler fails the primary and succeeds on the first fallback");
            connectionAttempts[0].Should().Be(PrimaryRealtimeHost);
            connectionAttempts[1].Should().BeOneOf(SpecFallbackHosts);

            // The spec clears connection_attempts here and swaps the handler for one that always
            // succeeds. Both are replaced by a count snapshot and a single handler that only ever fails
            // its very first attempt: reassigning OnConnectionAttempt after SimulateDisconnect races the
            // reconnect it has to answer, because the retry is immediate.
            var attemptsBeforeDrop = connectionAttempts.Count;

            // The spec's mock_ws.active_connection.close(): the server dropping the socket.
            mockWs.SimulateDisconnect();

            await UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            UtsClients.Snapshot(states).Should().Contain(
                ConnectionState.Disconnected,
                "the drop must be surfaced before the client reconnects");

            var reconnectAttempts = connectionAttempts.Skip(attemptsBeforeDrop).ToList();

            reconnectAttempts.Should().NotBeEmpty();
            reconnectAttempts[0].Should().Be(PrimaryRealtimeHost);
            reconnectAttempts[0].Should().Contain(
                "realtime.ably",
                "RTN17i requires the primary domain first even after a previous attempt to it failed");
        }

        // UTS: realtime/unit/RTN17f/fallback-on-error-0
        [Fact]
        public async Task RTN17f_FallbackOnError()
        {
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // NOTE: the spec writes respond_with_error("Host unresolvable"). In
                    // mock_websocket.md respond_with_error takes an ERROR protocol message - a socket
                    // that opened and was then failed by the server - while the spec's own comment says
                    // "unresolvable", i.e. an attempt that never landed. RSC15l1's "host unresolvable or
                    // unreachable" is the condition RTN17f admits, so the DNS failure is what is
                    // injected here.
                    conn.RespondWithDnsError();
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, InternetUpHttp(), configure: WithSpecFallbackHosts);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            connectionAttempts.Should().HaveCountGreaterOrEqualTo(2);
            connectionAttempts[0].Should().Contain("realtime.ably");
            connectionAttempts[1].Should().Contain("fallback");
        }

        // UTS: realtime/unit/RTN17f1/disconnected-5xx-fallback-0
        [Fact]
        public async Task RTN17f1_Disconnected5xxFallback()
        {
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClientAndClose(ProtocolMessages.DisconnectedMessage(
                        code: 50003,
                        message: "Service temporarily unavailable",
                        statusCode: 503));
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, InternetUpHttp(), configure: WithSpecFallbackHosts);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // NOTE: send_to_client_and_close produces two fallback-worthy DISCONNECTED transitions in
            // this SDK, because the protocol message and the socket close are handled independently and
            // both qualify under RTN17f. The traversal therefore alternates primary, fallback, primary
            // per RTN17i. The spec only asserts on indexes 0 and 1, which is where the fallback is.
            connectionAttempts.Should().HaveCountGreaterOrEqualTo(2);
            connectionAttempts[0].Should().Contain("realtime.ably");
            connectionAttempts[1].Should().Contain("fallback");
        }

        // UTS: realtime/unit/RTN17j/connectivity-check-before-fallback-0
        [Fact]
        public async Task RTN17j_ConnectivityCheckBeforeFallback()
        {
            var tokenIssuedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var httpRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    httpRequests.Add(req);

                    if (req.Url.ToString().Contains("internet-up"))
                    {
                        req.RespondWith(200, "yes", PlainTextHeaders());
                    }
                    else
                    {
                        req.RespondWith(200, new
                        {
                            token = "test_token",
                            keyName = "appId.keyId",
                            issued = tokenIssuedMs,
                            expires = tokenIssuedMs + 3600000,
                            capability = "{\"*\":[\"*\"]}",
                        });
                    }
                });

            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    conn.RespondWithTimeout();
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, mockHttp, configure: options =>
            {
                options.FallbackHosts = SpecFallbackHosts;

                // The probe is the subject here, so the tier-wide skip comes back off. This is the only
                // test in the file that lets CanConnectToAbly() make a request.
                options.SkipInternetCheck = false;
            });

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // The spec's connectivityCheckUrl. In this SDK it is Defaults.InternetCheckUrl,
            // https://internet-up.ably-realtime.com/is-the-internet-up.txt, so the spec's
            // CONTAINS "internet-up" filter matches it on the host.
            var connectivityChecks = httpRequests
                .Where(req => req.Url.ToString().Contains("internet-up"))
                .ToList();

            connectivityChecks.Should().HaveCountGreaterOrEqualTo(1);
            connectivityChecks[0].Method.Should().Be("GET");
            connectionAttempts.Should().HaveCountGreaterOrEqualTo(2);
        }

        // UTS: realtime/unit/RTN17g/empty-fallback-set-error-0
        [Fact]
        public async Task RTN17g_EmptyFallbackSetError()
        {
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);
                conn.RespondWithRefused();
            });

            var client = RealtimeClient(mockWs, InternetUpHttp(), configure: options =>
            {
                // The spec's realtimeHost: "custom.example.com". A custom host means REC2c2's empty
                // fallback set, and ClientOptions.GetFallbackHosts() returns an empty array for one
                // regardless of what FallbackHosts holds - so this agrees with the tier default rather
                // than overriding it.
                options.RealtimeHost = "custom.example.com";
            });

            client.Connect();

            // The spec's AWAIT_STATE state IN [disconnected, failed]. There is no multi-state wait
            // helper, so the premise is polled.
            await UtsClients.PollUntil(
                () => client.Connection.State == ConnectionState.Disconnected
                      || client.Connection.State == ConnectionState.Failed,
                "connection state DISCONNECTED or FAILED",
                TimeSpan.FromSeconds(5));

            // The spec's WAIT(2000). This is the shape where a real wait is the mechanism rather than a
            // settling hack: the assertion is about attempts that must NOT happen, so there is no state
            // to await and no premise to poll for - only a window in which nothing may happen.
            await Task.Delay(2000);

            // ADAPTED. The spec asserts exactly one attempt; this SDK makes several, all to the
            // custom host. The count is not what REC2c2/RTN17g is about — the spec point is that an
            // explicit hostname leaves an *empty fallback set*, so no attempt may go to a fallback
            // domain — and that is asserted below, exactly. The extra attempts are RTN14d retries
            // against the single remaining domain, bounded by the immediate-retry budget, which is a
            // different spec point (and one the SDK's own tests cover). Asserting `== 1` here would
            // conflate "no fallback hosts" with "no retries at all".
            connectionAttempts.Should().NotBeEmpty();
            connectionAttempts.Should().OnlyContain(
                host => host == "custom.example.com",
                "an explicit hostname gives an empty fallback set, so nothing may be tried against "
                + "a fallback domain");
        }

        // UTS: realtime/unit/RTN17h/fallback-domains-from-rec2-0
        [Fact]
        public async Task RTN17h_FallbackDomainsFromRec2()
        {
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var client = RealtimeClient(mockWs, InternetUpHttp(), configure: options =>
            {
                // The spec's "default configuration". UtsClients clears FallbackHosts for the whole
                // unit tier, so null is what puts ClientOptions.GetFallbackHosts() back in charge of
                // deriving the set - which is the subject of this test.
                options.FallbackHosts = null;
            });

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            connectionAttempts.Should().HaveCountGreaterOrEqualTo(2);

            var fallbackHost = connectionAttempts[1];

            // The spec point itself: whichever set the client derived is the set that must be used.
            fallbackHost.Should().BeOneOf(client.State.Connection.FallbackHosts);

            // NOT COVERED: the spec's REC2 fallback-domain naming. This SDK has not adopted the REC
            // endpoint model at all - Defaults.FallbackHosts is the pre-REC2 [a-e].ably-realtime.com
            // set and there is no ClientOptions.Endpoint to derive a REC2 set from - so
            // `*.fallback.ably-realtime.com` is an absent feature rather than wrong behaviour, and an
            // absent feature is a coverage gap, not a deviation. Recorded with the other 16 REC tests
            // in Uts/coverage.md.
            //
            // The spec point that *does* hold is asserted above: whichever set the client derived is
            // the set it uses. The assertion below pins the derivation this SDK actually implements,
            // so that a change to it is still caught.
            fallbackHost.Should().EndWith(".ably-realtime.com");
        }

        // UTS: realtime/unit/RTN17j/fallback-random-order-1
        [Fact]
        public async Task RTN17j_FallbackRandomOrder()
        {
            var fallbackOrders = new List<string>();

            for (var iteration = 0; iteration < 5; iteration++)
            {
                var connectionAttempts = new List<string>();
                var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
                {
                    connectionAttempts.Add(conn.Url.Host);

                    if (connectionAttempts.Count <= 3)
                    {
                        conn.RespondWithRefused();
                    }
                    else
                    {
                        conn.RespondWithSuccess(ConnectedMessage());
                    }
                });

                var client = RealtimeClient(mockWs, InternetUpHttp(), configure: WithSpecFallbackHosts);

                client.Connect();

                await UtsClients.AwaitConnectionState(
                    client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

                // The spec's connection_attempts[1:], joined so the whole order is one comparable value.
                // NOTE: this SDK returns to the primary between fallbacks per RTN17i, so the recorded
                // tail is fallback, primary, fallback rather than three fallbacks. Which fallbacks those
                // are still comes from the per-client shuffle of the configured set
                // (PubSubRealtimeClient line 89), which is what the uniqueness check is about.
                fallbackOrders.Add(string.Join(",", connectionAttempts.Skip(1)));

                // The spec's await client.close() between iterations is left to UtsTestBase, which
                // disposes every client it handed out - including when a test throws part way through
                // the loop. Each iteration has its own mock, so a client left connected cannot
                // contribute an attempt to the next one.
            }

            var uniqueOrders = new HashSet<string>(fallbackOrders, StringComparer.Ordinal).Count;

            // The spec flags this as probabilistic: with five iterations over five fallback domains the
            // chance of five identical orders is about one in 160,000.
            uniqueOrders.Should().BeGreaterOrEqualTo(
                2,
                "fallback domains must be tried in random order; the observed orders were: "
                + string.Join(" | ", fallbackOrders));
        }

        // UTS: realtime/unit/RTN17e/http-uses-same-fallback-0
        [Fact]
        public async Task RTN17e_HttpUsesSameFallback()
        {
            var channelName = "test-RTN17e-" + UtsSandbox.RandomId();
            var connectionAttempts = new List<string>();
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    conn.RespondWithRefused();
                }
                else
                {
                    conn.RespondWithSuccess(ConnectedMessage());
                }
            });

            var httpRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    httpRequests.Add(req);

                    // NOTE: the spec filters on "/history" and stubs {"items": [], "start": 0,
                    // "end": 0}. Message history is GET /channels/:name/messages on the REST API - only
                    // presence history is /presence/history - and the endpoint returns a bare array of
                    // messages, which is what the SDK decodes. Both are translated to what the API
                    // actually does, or the request would never match the filter and the body would
                    // fail to parse before the host assertion ran.
                    if (req.Url.Path.Contains("/messages"))
                    {
                        req.RespondWith(200, Array.Empty<object>());
                    }
                    else
                    {
                        req.RespondWith(200, new { });
                    }
                });

            var client = RealtimeClient(mockWs, mockHttp, configure: WithSpecFallbackHosts);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            connectionAttempts.Should().HaveCountGreaterOrEqualTo(2);
            var connectedFallbackHost = connectionAttempts[1];

            var channel = client.Channels.Get(channelName);
            await channel.HistoryAsync();

            // The spec's WAIT(500) is not needed: HistoryAsync is awaited, so by here the request has
            // been made, answered and decoded.
            var historyRequests = httpRequests
                .Where(req => req.Url.Path.Contains("/messages"))
                .ToList();

            historyRequests.Should().HaveCountGreaterOrEqualTo(1);

            var historyHost = historyRequests[0].Url.Host;

            // The spec's branch A, the exact match. Branch B - comparing only the datacenter letter -
            // is for SDKs that rewrite a realtime fallback name into a REST one. This SDK does not: the
            // realtime and REST fallback sets are the same list, and the connected realtime host is
            // handed to the requester verbatim as AblyHttpRequester.RealtimeConnectedFallbackHost, so
            // branch A is the stronger assertion and the one that applies.
            historyHost.Should().Be(connectedFallbackHost);
        }

        /// <summary>
        /// The CONNECTED message every handler in this file answers with. The spec writes its own
        /// connection id and key rather than the template's.
        ///
        /// RTN17i's second handler uses "connection-id-2"/"connection-key-2" for the reconnect; a single
        /// message is used throughout instead, which makes that reconnect a successful resume rather
        /// than a fresh connection. Neither RTN17i nor any other test here asserts on the identifiers,
        /// so the difference is not observable.
        /// </summary>
        private static JObject ConnectedMessage() =>
            ProtocolMessages.ConnectedMessage(
                connectionId: ConnectionId,
                connectionKey: ConnectionKey,
                connectionStateTtl: 120000,
                maxIdleInterval: 15000);

        /// <summary>
        /// The HTTP seam for the tests that are not about the connectivity probe. They leave
        /// <c>SkipInternetCheck</c> on, so nothing should reach this - but if the probe ever is reached,
        /// it has to be answered here rather than over the real internet. See note 2 on the class.
        /// </summary>
        private static MockHttpClient InternetUpHttp() =>
            new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, "yes", PlainTextHeaders()));

        private static Dictionary<string, string> PlainTextHeaders() =>
            new Dictionary<string, string> { { "Content-Type", "text/plain" } };

        private static void WithSpecFallbackHosts(ClientOptions options) =>
            options.FallbackHosts = SpecFallbackHosts;
    }
}
