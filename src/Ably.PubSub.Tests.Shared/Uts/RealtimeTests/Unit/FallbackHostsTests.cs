// Fallback Hosts Tests (RTN17)
// Derived from uts/realtime/unit/connection/fallback_hosts_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: no fake timers (the spec has none): connect timeouts and waits run on the real clock; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class FallbackHostsTests : UtsUnitTestBase
    {
        public FallbackHostsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN17i/prefer-primary-domain-0
        [UtsFact]
        [Trait("spec", "RTN17i")]
        public async Task RTN17i_PreferPrimaryDomain()
        {
            // Setup
            var connectionAttempts = new CaptureList<string>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Record which host was attempted
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // First attempt (to primary): fail
                    conn.RespondWithRefused();
                }
                else if (connectionAttempts.Count == 2)
                {
                    // Second attempt (to fallback): succeed
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // First connection attempt
            client.Connect();

            // Wait for successful connection (after trying primary then fallback)
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // NOTE: the spec clears the attempts and swaps the handler after DISCONNECTED, but RTN15a/RTN15h3 reconnect at once, so the
            // reconnection attempt would reach the first handler (a race in the spec); both steps are done before the close instead
            // Clear previous attempts
            connectionAttempts.Clear();

            // Allow next connection to primary to succeed
            mockWs.OnConnectionAttempt = conn =>
            {
                connectionAttempts.Add(conn.Url.Host);
                conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id-2", "connection-key-2"));
            };

            // Now force a disconnection
            mockWs.ActiveConnection.CloseFromServer();

            // Wait for DISCONNECTED
            // NOTE: DISCONNECTED may be left again at once (the SDK retries immediately), so the record is polled instead of AWAIT_STATE
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Connected).Any(s => s == ConnectionState.Disconnected),
                timeout: TimeSpan.FromSeconds(5),
                description: "connection entered DISCONNECTED after the server close");

            // Wait for automatic reconnection
            // NOTE: AWAIT_STATE connected would return at once if the reconnection already happened; the record is polled for the re-entry into CONNECTED
            await UtsWait.PollUntil(
                () => states.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                timeout: TimeSpan.FromSeconds(10),
                description: "connection re-entered CONNECTED after DISCONNECTED");

            // Assertions
            // The reconnection attempt should have tried primary domain first
            connectionAttempts.Count.Should().BeGreaterThanOrEqualTo(1);
            (connectionAttempts[0] == "realtime.ably.io" || connectionAttempts[0].Contains("realtime.ably")).Should().BeTrue("the first reconnection attempt is to the primary domain (was {0})", connectionAttempts[0]);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17f/fallback-on-error-0
        [UtsFact]
        [Trait("spec", "RTN17f")]
        public async Task RTN17f_FallbackOnError()
        {
            // Setup
            var connectionAttempts = new CaptureList<string>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // Primary domain: unresolvable (simulated)
                    // NOTE: the spec calls respond_with_error("Host unresolvable"), but respond_with_error takes an ERROR ProtocolMessage
                    // (mock_websocket.md), and an ERROR while CONNECTING means FAILED (RTN14g); the comment's "unresolvable (simulated)"
                    // is read as RSC15l1 host unresolvable, which mock_websocket.md renders as respond_with_dns_error()
                    conn.RespondWithDnsError();
                }
                else
                {
                    // Fallback domain: succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for successful connection via fallback
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // Assertions
            // Should have tried at least 2 hosts (primary + fallback)
            connectionAttempts.Count.Should().BeGreaterThanOrEqualTo(2);

            // First attempt was to primary domain
            connectionAttempts[0].Should().Contain("realtime.ably");

            // Second attempt was to a fallback domain
            connectionAttempts[1].Should().Contain("fallback");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17f1/disconnected-5xx-fallback-0
        [UtsFact]
        [Trait("spec", "RTN17f1")]
        public async Task RTN17f1_Disconnected5xxFallback()
        {
            // Setup
            var connectionAttempts = new CaptureList<string>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // Primary domain: connect then send DISCONNECTED with 503 and close
                    conn.RespondWithSuccess();
                    conn.SendToClientAndClose(ProtocolMessages.DisconnectedMessage(50003, "Service temporarily unavailable", 503));
                }
                else
                {
                    // Fallback domain: succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for successful connection via fallback
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // Assertions
            // Should have tried at least 2 hosts
            connectionAttempts.Count.Should().BeGreaterThanOrEqualTo(2);

            // First was primary, second was fallback
            connectionAttempts[0].Should().Contain("realtime.ably");
            connectionAttempts[1].Should().Contain("fallback");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17j/connectivity-check-before-fallback-0
        [UtsFact]
        [Trait("spec", "RTN17j")]
        public async Task RTN17j_ConnectivityCheckBeforeFallback()
        {
            // Setup
            var httpRequests = new CaptureList<PendingHttpRequest>();
            var connectionAttempts = new CaptureList<string>();

            // Mock HTTP client for connectivity check
            var mockHttp = new MockHttpClient(onRequest: req =>
            {
                httpRequests.Add(req);

                if (req.Url.ToString().Contains("internet-up"))
                {
                    // Connectivity check succeeds
                    req.RespondWith(200, "yes", new Dictionary<string, string> { ["Content-Type"] = "text/plain" });
                }
                else
                {
                    // Token requests etc
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    req.RespondWith(200, new JObject
                    {
                        ["token"] = "test_token",
                        ["keyName"] = "appId.keyId",
                        ["issued"] = now,
                        ["expires"] = now + 3600000,
                        ["capability"] = "{\"*\":[\"*\"]}",
                    });
                }
            });
            InstallMock(mockHttp);

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // Primary domain fails
                    // NOTE: respond_with_timeout is partial in the infra: the SDK's own connect timer (real clock here, as the spec has no fake timers) decides the outcome
                    conn.RespondWithTimeout();
                }
                else
                {
                    // Fallback succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for successful connection
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Assertions
            // Connectivity check should have been performed
            var connectivityChecks = httpRequests.Snapshot().Where(req => req.Url.ToString().Contains("internet-up")).ToList();
            connectivityChecks.Count.Should().BeGreaterThanOrEqualTo(1);

            // Connectivity check was a GET request
            connectivityChecks[0].Method.Should().Be("GET");

            // Connection attempts proceeded to fallback after check
            connectionAttempts.Count.Should().BeGreaterThanOrEqualTo(2);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17g/empty-fallback-set-error-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTN17g", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTN17g")]
        public async Task RTN17g_EmptyFallbackSetError()
        {
            // Setup
            var connectionAttempts = new CaptureList<string>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                // Connection fails
                conn.RespondWithRefused();
            });
            InstallMock(mockWs);

            // Use custom endpoint which results in empty fallback set (REC2c2)
            // NOTE: realtimeHost (deprecated, TO3k3) is not in this SDK; Endpoint = "custom.example.com" (REC1b2: a hostname endpoint has an empty fallback set) is the substitution
            var client = Realtime(new ClientOptions
            {
                Key = ValidKey,
                Endpoint = "custom.example.com", // Custom host = no fallbacks
                AutoConnect = false,
            });
            var states = UtsRecord.ConnectionStates(client.Connection);

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for DISCONNECTED (should not try fallbacks)
            // NOTE: AWAIT_STATE ... IN [disconnected, failed]: the record is polled
            await UtsWait.PollUntil(
                () => states.Snapshot().Any(s => s == ConnectionState.Disconnected || s == ConnectionState.Failed),
                timeout: TimeSpan.FromSeconds(5),
                description: "connection entered DISCONNECTED or FAILED");

            // Give it time to potentially try fallbacks (it shouldn't)
            await Task.Delay(2000); // NOTE: spec real-time WAIT kept: a drain would weaken the negative check

            // Assertions
            // Should have only tried the custom host once, no fallbacks
            connectionAttempts.Count.Should().Be(1);
            connectionAttempts[0].Should().Be("custom.example.com");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17h/fallback-domains-from-rec2-0
        [UtsFact]
        [Trait("spec", "RTN17h")]
        public async Task RTN17h_FallbackDomainsFromRec2()
        {
            // Setup
            var connectionAttempts = new CaptureList<string>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts.Add(conn.Url.Host);

                if (connectionAttempts.Count == 1)
                {
                    // Primary fails
                    conn.RespondWithRefused();
                }
                else
                {
                    // Fallback succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                }
            });
            InstallMock(mockWs);

            // Use default configuration (should use default fallback hosts)
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Test Steps
            // Start connection
            client.Connect();

            // Wait for successful connection
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));

            // Assertions
            // Should have tried primary then fallback
            connectionAttempts.Count.Should().BeGreaterThanOrEqualTo(2);

            // Second attempt should be a default fallback host
            // Default fallback pattern: *.a|b|c|d|e.fallback.ably-realtime.com
            var fallbackHost = connectionAttempts[1];
            fallbackHost.Should().Contain("fallback.ably-realtime.com");
            fallbackHost.Should().MatchRegex(@"\.[abcde]\.fallback\.ably-realtime\.com$");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTN17j/fallback-random-order-1
        [UtsFact]
        [Trait("spec", "RTN17j")]
        public async Task RTN17j_FallbackRandomOrder()
        {
            // Setup
            // Run multiple test iterations to check randomness
            var fallbackOrders = new List<string>();

            foreach (var iteration in new[] { 1, 2, 3, 4, 5 })
            {
                var connectionAttempts = new CaptureList<string>();

                var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
                {
                    connectionAttempts.Add(conn.Url.Host);

                    if (connectionAttempts.Count <= 3)
                    {
                        // Primary and first 2 fallbacks fail
                        conn.RespondWithRefused();
                    }
                    else
                    {
                        // Third fallback succeeds
                        conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key"));
                    }
                });
                InstallMock(mockWs);

                var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

                client.Connect();

                await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

                // Record the order of fallback hosts (skip primary at index 0)
                var fallbackOrder = connectionAttempts.Snapshot().Skip(1).ToList();
                fallbackOrders.Add(string.Join(",", fallbackOrder));

                await CloseClient(client);
            }

            // Test Steps
            // Analyze the collected fallback orders

            // Assertions
            // At least one iteration should have different order than another
            // (This is probabilistic - with 5 iterations and 5 fallback hosts,
            // we should see some variation)
            var uniqueOrders = fallbackOrders.Distinct().Count();
            uniqueOrders.Should().BeGreaterThanOrEqualTo(2);

            // Note: This test may occasionally fail due to randomness
            // In production, this should use a larger sample size
        }

        // UTS: realtime/unit/RTN17e/http-uses-same-fallback-0
        [UtsFact(Skip = "UTS spec error RTN17e: the UTS spec contradicts the features spec; skipped until the spec is fixed; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTN17e")]
        public Task RTN17e_HttpUsesSameFallback()
        {
            // SPEC ERROR RTN17e: the spec's mock HTTP answers every request, the RTN17j connectivity check included, with {} (no "yes"), so per
            // RTN17j the client must not move to a fallback domain, yet the test expects the realtime connection on a fallback; its history
            // response {items, start, end} is also not the REST wire format (a JSON array). Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RTN17e - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }
    }
}
