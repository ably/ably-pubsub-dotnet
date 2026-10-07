using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// One proxy session: a port the SDK talks plain HTTP/WS to, a rule set, and an event log.
    /// The specs' <c>session</c>.
    /// </summary>
    public sealed class UtsProxySession
    {
        /// <summary>
        /// Passed as the session's <c>timeoutMs</c>. The proxy's own default is 30s and it is an
        /// <em>idle</em> timer measured from the last traffic through the session, so a test that makes
        /// the proxy delay a response for twenty seconds and then reads the log can idle past the
        /// default and have its session collected mid-test.
        /// </summary>
        public const int DefaultSessionTimeoutMs = 120000;

        private readonly UtsProxyControl _control;
        private bool _closed;

        internal UtsProxySession(UtsProxyControl control, string sessionId, int proxyPort)
        {
            _control = control;
            SessionId = sessionId;
            ProxyPort = proxyPort;
        }

        public string SessionId { get; }

        public int ProxyPort { get; }

        /// <summary>Always <c>localhost</c>.</summary>
        public string ProxyHost => "localhost";

        /// <summary>
        /// Rules added while the session runs. <c>prepend</c> puts them ahead of the existing ones,
        /// which is how a spec faults traffic only once the client has reached some state.
        /// </summary>
        public async Task AddRules(JArray rules, string position = "append")
        {
            var body = new JObject
            {
                ["rules"] = rules,
                ["position"] = position,
            };

            await _control.Post($"/sessions/{SessionId}/rules", body).ConfigureAwait(false);
        }

        /// <summary>
        /// The imperative half — <c>{"type": "disconnect"}</c>,
        /// <c>{"type": "close", "closeCode": 1000}</c>, <c>inject_to_client</c>. The control API answers
        /// 409 when no WebSocket connection is open, which surfaces here as an exception naming that.
        /// </summary>
        public async Task TriggerAction(JObject action)
            => await _control.Post($"/sessions/{SessionId}/actions", action).ConfigureAwait(false);

        /// <summary>
        /// Every event the session recorded, in order.
        ///
        /// The field names are the <em>proxy's</em>, not the specs': a <c>ws_connect</c> carries
        /// <c>queryParams</c>; a <c>ws_frame</c> carries <c>direction</c>
        /// (<c>server_to_client</c> / <c>client_to_server</c>), a <c>message</c> whose <c>action</c> is
        /// an <strong>integer</strong>, and <c>ruleMatched</c> holding the rule's <c>comment</c>
        /// verbatim; a <c>ws_disconnect</c> carries <c>initiator</c>. A spec writing
        /// <c>e.type == "ws_frame_to_server"</c> or <c>action == "MESSAGE"</c> is reading fields that do
        /// not exist — derive against the real names through a filter defined once per file, and record
        /// the drift in deviations.md.
        /// </summary>
        public async Task<List<JObject>> GetLog()
        {
            var response = await _control.Get($"/sessions/{SessionId}/log").ConfigureAwait(false);
            var events = response["events"] as JArray;
            return events == null
                ? new List<JObject>()
                : events.Children<JObject>().ToList();
        }

        /// <summary>Best effort, never throws. <see cref="UtsProxyTestBase"/> calls it for you.</summary>
        public async Task Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            await _control.Delete($"/sessions/{SessionId}").ConfigureAwait(false);
        }

        public override string ToString() => $"proxy session {SessionId} on {ProxyHost}:{ProxyPort}";
    }

    /// <summary>
    /// Readers for the proxy's event log, so the field-name drift between the specs and the proxy is
    /// handled in one place rather than in every derived file.
    /// </summary>
    public static class ProxyLog
    {
        public const string WsConnect = "ws_connect";
        public const string WsFrame = "ws_frame";
        public const string WsDisconnect = "ws_disconnect";
        public const string HttpRequest = "http_request";
        public const string HttpResponse = "http_response";

        public const string ServerToClient = "server_to_client";
        public const string ClientToServer = "client_to_server";

        public static List<JObject> OfType(IEnumerable<JObject> log, string type)
            => log.Where(e => (string)e["type"] == type).ToList();

        public static List<JObject> WsConnects(IEnumerable<JObject> log) => OfType(log, WsConnect);

        /// <summary>
        /// Frames in one direction, optionally filtered to a protocol action. The action is an integer
        /// in the log, never a name — a spec comparing it to <c>"MESSAGE"</c> is reading a field shape
        /// the proxy does not produce.
        /// </summary>
        public static List<JObject> Frames(IEnumerable<JObject> log, string direction, int? action = null)
            => OfType(log, WsFrame)
                .Where(e => (string)e["direction"] == direction)
                .Where(e => action == null || (int?)e["message"]?["action"] == action)
                .ToList();

        public static List<JObject> FramesToServer(IEnumerable<JObject> log, int? action = null)
            => Frames(log, ClientToServer, action);

        public static List<JObject> FramesToClient(IEnumerable<JObject> log, int? action = null)
            => Frames(log, ServerToClient, action);

        /// <summary>
        /// HTTP requests, optionally filtered by a path substring. Unlike responses, request events do
        /// carry <c>method</c> and <c>path</c>.
        /// </summary>
        public static List<JObject> HttpRequests(IEnumerable<JObject> log, string pathContains = null)
            => OfType(log, HttpRequest)
                .Where(e => pathContains == null ||
                            ((string)e["path"] ?? string.Empty).Contains(pathContains))
                .ToList();

        /// <summary>
        /// HTTP responses, in order. They carry <c>status</c> and <c>ruleMatched</c> only — there is no
        /// <c>path</c> — so "the injected response fired" is read off position, not by endpoint.
        /// </summary>
        public static List<JObject> HttpResponses(IEnumerable<JObject> log) => OfType(log, HttpResponse);

        /// <summary>A rule with no <c>comment</c> appears as <c>rule-0</c>, <c>rule-1</c>, and so on.</summary>
        public static string RuleMatched(JObject logEvent) => (string)logEvent["ruleMatched"];
    }
}
