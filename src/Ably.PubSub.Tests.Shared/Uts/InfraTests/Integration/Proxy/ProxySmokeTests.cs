using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Tests.Uts.Infra.Integration.Proxy;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Integration.Proxy
{
    /// <summary>
    /// The proxy smoke test (guide 2.7 "Proxy"): the pinned proxy is installed and healthy, a pass-through session
    /// carries a token-authenticated client to the sandbox, the typed log shows the traffic, an imperative disconnect
    /// and a one-shot frame rule (string <c>match.action</c>) each fault the client once and it recovers, and
    /// sessions and clients are closed in teardown even when the test fails. Every wait is wall-clock and bounded;
    /// JSON only (the proxy relays text frames only).
    /// </summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("uts-infra", "proxy")]
    [Trait("type", "integration")]
    public class ProxySmokeTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 90000;

        // Protocol actions (proxy.md "Protocol Message Action Numbers").
        private const int ConnectedAction = 4;
        private const int ErrorAction = 9;
        private const int AttachAction = 10;
        private const int AttachedAction = 11;

        private static readonly TimeSpan LogTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);

        public ProxySmokeTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task ThePinnedProxyIsInstalledVerifiedStartedAndHealthy()
        {
            await ProxyManager.EnsureStartedAsync();
            Output.WriteLine($"uts-proxy v{ProxyManager.Version}: {ProxyManager.Source} {ProxyManager.BinaryPath} ({ProxyManager.Platform})");

            new[] { ProxyBinarySource.LocalOverride, ProxyBinarySource.Cache, ProxyBinarySource.VerifiedDownload }
                .Should().Contain(ProxyManager.Source);
            File.Exists(ProxyManager.BinaryPath).Should().BeTrue();
            if (ProxyManager.Source != ProxyBinarySource.LocalOverride)
            {
                // Only a platform with a pinned archive hash gets a release binary; a download was verified against
                // it before extraction, and a cache hit was installed by a verifying installer (this SDK's, ably-java's
                // or ably-cocoa's: the cache is shared on purpose).
                ProxyManager.PinnedSha256(ProxyManager.Platform).Should().NotBeNull();
                ProxyManager.BinaryPath.Should().Be(Path.Combine(ProxyManager.CacheDirectory(ProxyManager.Version), "uts-proxy"));
            }

            (await ProxyManager.IsHealthyAsync()).Should().BeTrue();
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task APassThroughSessionConnectsATokenAuthClientAndTheLogShowsTheConnection()
        {
            var session = await CreateProxySession();
            var client = ProxyRealtime(session, new ClientOptions { AutoConnect = false });
            var states = UtsRecord.ConnectionStates(client.Connection);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);
            states.Last().Should().Be(ConnectionState.Connected);

            var log = await UtsWait.PollUntil<List<ProxyEvent>>(
                async () =>
                {
                    var events = await session.GetLogAsync();
                    return events.FramesToClient(ConnectedAction).Any() ? events : null;
                },
                timeout: LogTimeout,
                description: "a server-to-client CONNECTED frame in the proxy log");

            var connects = log.WsConnects();
            connects.Should().NotBeEmpty();

            // The chosen proxy auth: a token (accessToken), never the key (Basic auth is refused without TLS, RSC18).
            // ToString is redacted: the access token never reaches the test output.
            Output.WriteLine($"first ws_connect: {connects[0]}");
            connects[0].ToString().Should().NotContain(connects[0].QueryParams["accessToken"]);
            connects[0].QueryParams.ContainsKey("accessToken").Should().BeTrue();
            connects[0].QueryParams.ContainsKey("key").Should().BeFalse();
            log.FramesToClient(ConnectedAction).Should().NotBeEmpty();

            // REST through the same session: the proxy logs the request and its response (token auth again).
            var rest = ProxyRest(session);
            var history = await UtsWait.WithTimeout(
                rest.Channels.Get(UniqueChannelName("proxy-rest")).HistoryAsync(),
                StateTimeout,
                "a REST history request through the proxy");
            history.Items.Should().BeEmpty();

            log = await session.GetLogAsync();
            var requests = log.HttpRequests("/messages");
            Output.WriteLine($"http_request: {string.Join(" | ", requests.Select(e => e.Method + " " + ProxyRedaction.RedactText(e.Path)))}; http_response: {string.Join(" | ", log.HttpResponses().Select(e => e.Status))}");
            requests.Should().ContainSingle();
            requests[0].Method.Should().Be("GET");
            requests[0].Path.Should().StartWith("/channels/");
            log.HttpResponses().Select(e => e.Status).Should().Contain(200);
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task AnImperativeDisconnectIsRecordedAsDisconnectedThenTheClientRecovers()
        {
            var session = await CreateProxySession();
            var client = ProxyRealtime(session, new ClientOptions { AutoConnect = false });
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var states = UtsRecord.ConnectionStates(client.Connection);
            await session.TriggerActionAsync(ProxyRules.Disconnect());

            // Record-and-verify: DISCONNECTED may last a single tick, so wait for the recovery and assert the record.
            await UtsWait.PollUntil(
                () => ConnectedAfterDisconnected(states.Snapshot()),
                timeout: RecoveryTimeout,
                description: "DISCONNECTED followed by CONNECTED");
            UtsAssert.ContainsInOrder(
                states,
                new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected });
            client.Connection.State.Should().Be(ConnectionState.Connected);

            var log = await session.GetLogAsync();
            Output.WriteLine($"actions: {string.Join(" | ", log.Actions())}; ws_disconnects: {string.Join(" | ", log.WsDisconnects())}");
            log.WsConnects().Count.Should().BeGreaterThanOrEqualTo(2, "the client reconnects through the same session");
            var actions = log.Actions();
            actions.Should().ContainSingle();
            ((string)actions[0].Message["type"]).Should().Be("disconnect");
            actions[0].Initiator.Should().Be("proxy");
            log.WsDisconnects().Select(e => e.Initiator).Should().Contain("proxy");
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task AFrameRuleWithAStringActionFiresOnceThenTheChannelRecovers()
        {
            var channelName = UniqueChannelName("proxy-smoke");
            const string comment = "smoke: replace the first ATTACHED with a channel ERROR";
            var rule = ProxyRules.Rule(
                ProxyRules.WsFrameToClient(AttachedAction, channelName),
                ProxyRules.Replace(new JObject
                {
                    ["action"] = ErrorAction,
                    ["channel"] = channelName,
                    ["error"] = new JObject { ["code"] = 40160, ["statusCode"] = 403, ["message"] = "Not permitted" },
                }),
                times: 1,
                comment: comment);
            rule["match"]["action"].Type.Should().Be(JTokenType.String);

            // The proxy answers 400 to a numeric match.action, so creating the session is itself a check.
            var session = await CreateProxySession(new[] { rule });
            var client = ProxyRealtime(session, new ClientOptions { AutoConnect = false });
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            channel.Attach();
            await AwaitChannelState(channel, ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);

            // times: 1 — the rule is spent, so the next ATTACHED passes through.
            var reattach = await UtsWait.WithTimeout(channel.AttachAsync(), StateTimeout, "the re-attach's ATTACHED");
            AssertSucceeded(reattach, "reattach");
            channel.State.Should().Be(ChannelState.Attached);
            client.Connection.State.Should().Be(ConnectionState.Connected);

            var log = await session.GetLogAsync();
            log.MatchedBy(comment).Should().HaveCount(1);
            log.FramesToServer(AttachAction).Count.Should().BeGreaterThanOrEqualTo(2, "both ATTACHes reached the server");
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task SessionsAndClientsAreClosedInTeardownEvenWhenTheTestFails()
        {
            // A second instance of the base stands in for a failing test: it builds a session and a client, the body
            // throws, and xUnit's teardown (IAsyncLifetime.DisposeAsync) runs, as it would after any failure.
            var probe = new TeardownProbe(Fixture, Output);
            var failure = new InvalidOperationException("simulated test failure");
            ProxySession session = null;
            PubSubRealtimeClient client = null;
            try
            {
                session = await probe.CreateProxySession();
                client = probe.ProxyRealtime(session, new ClientOptions { AutoConnect = false });
                client.Connect();
                await probe.AwaitConnectionState(client.Connection, ConnectionState.Connected);
                throw failure;
            }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure))
            {
                // The simulated failure; teardown below must still close everything.
            }
            finally
            {
                await ((IAsyncLifetime)probe).DisposeAsync();
            }

            client.Should().NotBeNull();
            client.Connection.State.Should().Be(ConnectionState.Closed);
            client.Disposed.Should().BeTrue();
            session.Closed.Should().BeTrue();
            (await session.ExistsAsync()).Should().BeFalse("teardown deleted the session from the proxy");
        }

        private static bool ConnectedAfterDisconnected(List<ConnectionState> states)
        {
            var disconnected = states.IndexOf(ConnectionState.Disconnected);
            return disconnected >= 0 && states.Skip(disconnected + 1).Contains(ConnectionState.Connected);
        }

        /// <summary>The base, used directly; it has no tests of its own.</summary>
        private sealed class TeardownProbe : UtsProxyTestBase
        {
            public TeardownProbe(SandboxAppFixture fixture, ITestOutputHelper output)
                : base(fixture, output)
            {
            }
        }
    }
}
