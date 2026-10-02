using System;
using System.Linq;
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
    /// Derived from uts/realtime/integration/proxy/rest_faults.md in ably/specification.
    ///
    /// Spec points: RSC10, RSC15m, REC2c2, RTL6
    ///
    /// <para>
    /// Two faults and a golden path. RSC10 is the renewal a mock can only approximate - a real
    /// token, really rejected, really replaced - and RSC15m is its opposite, an error that must
    /// reach the caller because there is nowhere else to try. RTL6 asserts the proxy itself is
    /// transparent, which everything else in the tier assumes.
    /// </para>
    /// </summary>
    public class RestFaultsTests : UtsProxyTestBase
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(10);

        public RestFaultsTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RSC10/token-renewal-on-401-0
        [ProxyFact]
        public async Task RSC10_A401TokenErrorIsRenewedAndTheRequestRetried()
        {
            var channelName = "test-RSC10-token-renewal-" + UtsSandbox.RandomId();

            var session = await ProxySession(new JArray
            {
                ChannelRule(
                    401,
                    ErrorBody(40142, 401, "Token expired"),
                    "RSC10: 401 token error on the first channel request"),
            });

            var callbackCount = 0;
            var client = ProxyRestClient(
                session,
                await TokenAuthCallback(onInvoked: () => Interlocked.Increment(ref callbackCount)));

            await client.Channels.Get(channelName).PublishAsync("test-event", "hello");

            Volatile.Read(ref callbackCount).Should().BeGreaterOrEqualTo(
                2,
                "RSC10 - the first token authenticated, the second replaced it after the 401");

            var log = await session.GetLog();

            ProxyLog.HttpRequests(log, "/channels/").Count.Should().BeGreaterOrEqualTo(
                2,
                "RSC10 - the publish was retried with the new token");

            var responses = ProxyLog.HttpResponses(log);
            responses.Count.Should().BeGreaterOrEqualTo(2);
            ((int)responses[0]["status"]).Should().Be(401, "the injected response came first");
            ((int)responses[1]["status"]).Should().BeInRange(
                200,
                299,
                "and the retry was accepted");
        }

        // UTS: realtime/proxy/RSC15m/http-503-no-fallback-0
        [ProxyFact]
        public async Task RSC15m_A503WithNoFallbackHostsReachesTheCaller()
        {
            var channelName = "test-RSC15m-503-error-" + UtsSandbox.RandomId();

            var session = await ProxySession(new JArray
            {
                ChannelRule(
                    503,
                    ErrorBody(50300, 503, "Service temporarily unavailable"),
                    "RSC15m: 503 on the first channel request"),
            });

            // No FallbackHosts: an explicit host has already emptied the default list, which is
            // REC2c2, so there is nothing for the retry machinery to try.
            var client = ProxyRestClient(session, await TokenAuthCallback());

            var error = await Assert.ThrowsAsync<AblyException>(
                () => client.Channels.Get(channelName).PublishAsync("test-event", "hello"));

            error.ErrorInfo.Code.Should().Be(50300);
            error.ErrorInfo.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);

            ProxyLog.HttpRequests(await session.GetLog(), "/channels/").Should().HaveCount(
                1,
                "RSC15m - with an empty fallback set the error is immediate, not retried");
        }

        // UTS: realtime/proxy/RTL6/publish-history-through-proxy-0
        [ProxyFact]
        public async Task RTL6_PublishAndHistoryBothWorkThroughTheProxy()
        {
            var channelName = "test-RTL6-publish-history-" + UtsSandbox.RandomId();

            var session = await ProxySession();

            var realtime = ProxyRealtimeClient(session, await JwtAuthCallback());
            var rest = ProxyRestClient(session, await JwtAuthCallback());

            realtime.Connect();
            await UtsClients.AwaitConnectionState(
                realtime.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = realtime.Channels.Get(channelName);
            await channel.AttachAsync();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, AttachTimeout);

            await channel.PublishAsync("test-msg", "hello world");

            var restChannel = rest.Channels.Get(channelName);
            var published = await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var page = await restChannel.HistoryAsync();
                    return page.Items.FirstOrDefault(m => m.Name == "test-msg");
                },
                "the published message to appear in history");

            published.Data.Should().Be("hello world");

            var log = await session.GetLog();

            ProxyLog.WsConnects(log).Should().NotBeEmpty("the realtime client went through it");
            ProxyLog.HttpRequests(log).Should().NotBeEmpty("and so did the REST client");
        }

        private static JObject ChannelRule(int status, JObject body, string comment)
            => new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "http_request",
                    ["pathContains"] = "/channels/",
                },
                ["action"] = new JObject
                {
                    ["type"] = "http_respond",
                    ["status"] = status,
                    ["body"] = body,
                },
                ["times"] = 1,
                ["comment"] = comment,
            };

        private static JObject ErrorBody(int code, int statusCode, string message)
            => new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["statusCode"] = statusCode,
                    ["message"] = message,
                },
            };
    }
}
