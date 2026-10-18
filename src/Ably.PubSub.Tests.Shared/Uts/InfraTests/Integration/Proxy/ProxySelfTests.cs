using System.IO;
using System.Linq;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Tests.Uts.Infra.Integration.Proxy;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ably.PubSub.Tests.Uts.InfraTests.Integration.Proxy
{
    /// <summary>
    /// Offline self-tests of the proxy helpers (guide 2.7 "Proxy" self-tests: rule builders stringify
    /// <c>match.action</c>; a session with <c>rules:</c> only defaults its endpoint to the sandbox), plus the typed log
    /// parser and the pinned asset table. No proxy, no network, no collection fixture.
    /// </summary>
    [Trait("uts", "proxy")]
    [Trait("uts-infra", "proxy")]
    [Trait("type", "integration")]
    public class ProxySelfTests
    {
        [Fact]
        public void RuleBuildersSendMatchActionAsAString()
        {
            ProxyRules.WsFrameToClient(11)["action"].Type.Should().Be(JTokenType.String);
            ((string)ProxyRules.WsFrameToClient(11)["action"]).Should().Be("11");
            ((string)ProxyRules.WsFrameToServer(ProtocolMessage.MessageAction.Attach)["action"]).Should().Be("10");
            ((string)ProxyRules.WsFrameToClient(ProtocolMessage.MessageAction.Connected)["action"]).Should().Be("4");

            // Above AUTH (17) uts-proxy v0.3.0 resolves no names, so numeric strings are the only working form.
            ((string)ProxyRules.WsFrameToClient(20, "objects-channel")["action"]).Should().Be("20");
            ((string)ProxyRules.WsFrameToClient(20, "objects-channel")["channel"]).Should().Be("objects-channel");

            var rule = ProxyRules.Rule(
                ProxyRules.WsFrameToClient(11, "ch"),
                ProxyRules.Replace(new JObject { ["action"] = 9, ["channel"] = "ch" }),
                times: 1,
                comment: "c");
            var json = rule.ToString(Formatting.None);
            json.Should().Be(
                "{\"match\":{\"type\":\"ws_frame_to_client\",\"action\":\"11\",\"channel\":\"ch\"}," +
                "\"action\":{\"type\":\"replace\",\"message\":{\"action\":9,\"channel\":\"ch\"}},\"times\":1,\"comment\":\"c\"}");

            // Only match.action is a string: the rule's action is an object and an injected message's action a number.
            rule["action"].Type.Should().Be(JTokenType.Object);
            rule["action"]["message"]["action"].Type.Should().Be(JTokenType.Integer);
        }

        [Fact]
        public void OtherBuildersProduceTheProxyMdShapes()
        {
            ProxyRules.WsConnect(count: 2).ToString(Formatting.None).Should().Be("{\"type\":\"ws_connect\",\"count\":2}");
            ProxyRules.HttpRequest("POST", "/keys/").ToString(Formatting.None)
                .Should().Be("{\"type\":\"http_request\",\"method\":\"POST\",\"pathContains\":\"/keys/\"}");
            ProxyRules.DelayAfterWsConnect(5000).ToString(Formatting.None).Should().Be("{\"type\":\"delay_after_ws_connect\",\"delayMs\":5000}");
            ProxyRules.AcceptAndClose().ToString(Formatting.None).Should().Be("{\"type\":\"accept_and_close\",\"closeCode\":1011}");
            ProxyRules.Close().ToString(Formatting.None).Should().Be("{\"type\":\"close\",\"closeCode\":1000}");
            ProxyRules.Disconnect().ToString(Formatting.None).Should().Be("{\"type\":\"disconnect\"}");
            ProxyRules.HttpRespond(401).ToString(Formatting.None).Should().Be("{\"type\":\"http_respond\",\"status\":401}");
            ProxyRules.Rule(ProxyRules.WsConnect(), ProxyRules.RefuseConnection()).ContainsKey("times").Should().BeFalse();
        }

        [Fact]
        public void ASessionWithRulesOnlyDefaultsItsEndpointToTheSandbox()
        {
            var rules = new[] { ProxyRules.Rule(ProxyRules.WsConnect(), ProxyRules.Passthrough()) };

            var request = ProxySession.BuildCreateRequest(null, rules, null, null);

            var sandboxHost = Defaults.GetPrimaryDomain(SandboxApp.DefaultEndpoint);
            ((string)request["target"]["realtimeHost"]).Should().Be(sandboxHost);
            ((string)request["target"]["restHost"]).Should().Be(sandboxHost);
            if (System.Environment.GetEnvironmentVariable(SandboxApp.EndpointVariable) == null)
            {
                sandboxHost.Should().Be("sandbox.realtime.ably-nonprod.net");
            }

            ((JArray)request["rules"]).Should().HaveCount(1);
            ((int)request["timeoutMs"]).Should().Be(ProxySession.DefaultTimeoutMs);
            request.ContainsKey("port").Should().BeFalse();

            var explicitRequest = ProxySession.BuildCreateRequest("nonprod:sandbox", null, 12345, 30000);
            ((string)explicitRequest["target"]["realtimeHost"]).Should().Be("sandbox.realtime.ably-nonprod.net");
            ((int)explicitRequest["port"]).Should().Be(12345);
            ((int)explicitRequest["timeoutMs"]).Should().Be(30000);
            explicitRequest.ContainsKey("rules").Should().BeFalse();
        }

        [Fact]
        public void TheTypedLogReadsTheProxyFieldNames()
        {
            var log = new JArray
            {
                JObject.Parse("{\"type\":\"ws_connect\",\"url\":\"ws://localhost/\",\"queryParams\":{\"accessToken\":[\"tok\"],\"v\":\"6\"},\"ruleMatched\":\"\"}"),
                JObject.Parse("{\"type\":\"ws_frame\",\"direction\":\"server_to_client\",\"message\":{\"action\":4},\"ruleMatched\":\"rule-0\"}"),
                JObject.Parse("{\"type\":\"ws_disconnect\",\"initiator\":\"proxy\",\"closeCode\":1006}"),
                JObject.Parse("{\"type\":\"http_request\",\"method\":\"GET\",\"path\":\"/channels/x/messages\"}"),
                JObject.Parse("{\"type\":\"http_response\",\"status\":200}"),
            }.Children<JObject>().Select(e => ProxyEvent.Parse(e)).ToList();

            log.WsConnects().Single().QueryParams["accessToken"].Should().Be("tok");
            log.WsConnects().Single().QueryParams["v"].Should().Be("6");
            log.WsConnects().Single().RuleMatched.Should().BeNull("an empty ruleMatched is pass-through");
            log.FramesToClient(4).Single().RuleMatched.Should().Be("rule-0");
            log.FramesToServer().Should().BeEmpty();
            log.WsDisconnects().Single().CloseCode.Should().Be(1006);
            log.WsDisconnects().Single().Initiator.Should().Be("proxy");
            log.HttpRequests("/messages").Single().Method.Should().Be("GET");
            log.HttpResponses().Single().Status.Should().Be(200);
            log.MatchedBy("rule-0").Should().HaveCount(1);
        }

        [Fact]
        public void AnEventPrintsWithItsAccessTokenAndAuthorizationHeaderRedacted()
        {
            const string token = "xVLyHw.secret-token_value";
            var connect = ProxyEvent.Parse(new JObject
            {
                ["type"] = "ws_connect",
                ["url"] = "/?v=6&accessToken=" + token + "&format=json",
                ["queryParams"] = new JObject { ["accessToken"] = token, ["v"] = "6" },
            });
            var request = ProxyEvent.Parse(new JObject
            {
                ["type"] = "http_request",
                ["method"] = "GET",
                ["path"] = "/channels/c/messages",
                ["headers"] = new JObject { ["Authorization"] = new JArray("Bearer " + token), ["Accept"] = "application/json" },
            });

            connect.ToString().Should().NotContain(token).And.Contain("accessToken=" + ProxyRedaction.Redacted).And.Contain("format=json");
            request.ToString().Should().NotContain(token).And.Contain("application/json");
            connect.QueryParams["accessToken"].Should().Be(token, "only the printed form is redacted; the typed fields stay usable");
            ProxyRedaction.RedactText("GET /x?key=app.id:secret&y=1").Should().Be("GET /x?key=" + ProxyRedaction.Redacted + "&y=1");
            ProxyRedaction.RedactText("Authorization: Bearer " + token).Should().Be("Authorization: " + ProxyRedaction.Redacted);
        }

        [Fact]
        public void ThePinnedReleaseHasAChecksumForEveryPublishedAssetAndTheSharedCacheLayout()
        {
            foreach (var platform in new[] { "darwin_amd64", "darwin_arm64", "linux_amd64", "linux_arm64" })
            {
                ProxyManager.PinnedSha256(platform).Should().MatchRegex("^[0-9a-f]{64}$");
                ProxyManager.AssetName(platform).Should().Be($"uts-proxy_0.3.0_{platform}.tar.gz");
            }

            ProxyManager.PinnedSha256("windows_amd64").Should().BeNull("v0.3.0 publishes no Windows asset");

            var directory = ProxyManager.CacheDirectory(ProxyManager.Version);
            directory.Should().EndWith(Path.Combine(".cache", "uts-proxy", "v0.3.0"));
        }
    }
}
