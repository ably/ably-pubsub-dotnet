using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// Tests for the proxy harness itself, not for the SDK.
    ///
    /// The proxy tier's assertions are all read off <c>session.GetLog()</c>, so a harness that
    /// mis-reads the control API — or silently fails to start the proxy at all — would make every
    /// proxy test either empty or unexplainable. This pins the session lifecycle and the log shape
    /// before anything is built on them.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class UtsProxyHarnessTests : UtsProxyTestBase
    {
        public UtsProxyHarnessTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [ProxyFact]
        public async Task ASessionBindsAPortAndStartsWithAnEmptyLog()
        {
            var session = await ProxySession();

            session.SessionId.Should().NotBeNullOrEmpty();
            session.ProxyHost.Should().Be("localhost");
            session.ProxyPort.Should().BeGreaterThan(0);
            (await session.GetLog()).Should().BeEmpty();
        }

        [ProxyFact]
        public async Task RulesCanBeAddedWhileTheSessionRuns()
        {
            var session = await ProxySession();

            await session.AddRules(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "http_request",
                        ["pathContains"] = "/time",
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "http_respond",
                        ["status"] = 403,
                    },
                    ["times"] = 1,
                    ["comment"] = "harness check",
                },
            });

            // Adding a rule is the assertion: the control API answers 400 on a malformed rule, so a
            // clean return means the rule shape the derived tests use is accepted.
        }

        [ProxyFact]
        public async Task TrafficThroughTheSessionReachesTheSandboxAndIsLogged()
        {
            // The one end-to-end check: a real request, through the proxy, to the real sandbox, and
            // then read back off the event log. /time is the cheapest call that proves the whole path.
            var session = await ProxySession();
            var client = ProxyRestClient(session, await JwtAuthCallback());

            var serverTime = await client.TimeAsync();

            serverTime.Year.Should().BeGreaterThan(2000);

            var log = await session.GetLog();
            var requests = ProxyLog.HttpRequests(log, "/time");

            requests.Should().NotBeEmpty("the request must have crossed the proxy");
            ProxyLog.HttpResponses(log).Should().NotBeEmpty();
        }

        [ProxyFact]
        public async Task AnInjectedResponseFiresAndIsVisibleInTheLog()
        {
            // Proves fault injection works and that the harness reads ruleMatched the way the derived
            // tests will. A 403 carrying Server: CloudFront is the shape RSC15l4 uses.
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "http_request",
                        ["pathContains"] = "/time",
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "http_respond",
                        ["status"] = 403,
                        ["body"] = new JObject
                        {
                            ["error"] = new JObject
                            {
                                ["message"] = "Forbidden",
                                ["code"] = 40300,
                                ["statusCode"] = 403,
                            },
                        },
                        ["headers"] = new JObject { ["Server"] = "CloudFront" },
                    },
                    ["times"] = 1,
                    ["comment"] = "harness: CloudFront 403 on the first /time",
                },
            });

            var client = ProxyRestClient(session, await JwtAuthCallback());

            try
            {
                await client.TimeAsync();
            }
            catch (AblyException)
            {
                // Whether the SDK retries or propagates is the subject of the derived RSC15l4 test,
                // not of this one. All this asserts is that the rule fired.
            }

            var log = await session.GetLog();
            var responses = ProxyLog.HttpResponses(log);

            responses.Should().NotBeEmpty();
            responses.Select(r => (int?)r["status"]).Should().Contain(403);
            responses.Should().Contain(r => ProxyLog.RuleMatched(r) != null);
        }
    }
}
