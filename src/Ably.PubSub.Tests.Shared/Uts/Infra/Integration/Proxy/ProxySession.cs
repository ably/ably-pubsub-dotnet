using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>
    /// The specs' proxy session (<c>proxy.md</c> "Proxy Session Interface"): a port the SDK talks plain HTTP/WS
    /// to, a rule list, imperative actions and an event log, over uts-proxy's control API (<c>POST /sessions</c>,
    /// <c>POST /sessions/{id}/rules</c>, <c>POST /sessions/{id}/actions</c>, <c>GET /sessions/{id}/log</c>,
    /// <c>DELETE /sessions/{id}</c>).
    /// </summary>
    public sealed class ProxySession
    {
        /// <summary>The session's auto-cleanup timeout when the spec gives none.</summary>
        public const int DefaultTimeoutMs = 120000;

        private static readonly HttpClient Control = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private ProxySession(string sessionId, int proxyPort, string endpoint, string targetHost)
        {
            SessionId = sessionId;
            ProxyPort = proxyPort;
            Endpoint = endpoint;
            TargetHost = targetHost;
        }

        /// <summary>The specs' <c>session.session_id</c>.</summary>
        public string SessionId { get; }

        /// <summary>The specs' <c>session.proxy_host</c>: always <c>localhost</c>.</summary>
        public string ProxyHost => "localhost";

        /// <summary>The specs' <c>session.proxy_port</c>.</summary>
        public int ProxyPort { get; }

        /// <summary>The endpoint the session forwards to (as the spec gave it, or the sandbox default).</summary>
        public string Endpoint { get; }

        /// <summary>The upstream host the endpoint resolved to, sent as both <c>realtimeHost</c> and <c>restHost</c>.</summary>
        public string TargetHost { get; }

        /// <summary>True once <see cref="CloseAsync"/> has run.</summary>
        public bool Closed { get; private set; }

        /// <summary>The specs' <c>create_proxy_session(endpoint:, port:, rules:, timeoutMs:)</c>.</summary>
        /// <param name="endpoint">The spec's endpoint; null means the sandbox (<see cref="SandboxApp.DefaultEndpoint"/>).</param>
        /// <param name="timeoutMs">The session's auto-cleanup timeout; <see cref="DefaultTimeoutMs"/> when null.</param>
        public static async Task<ProxySession> CreateAsync(
            string endpoint = null,
            IEnumerable<JObject> rules = null,
            int? port = null,
            int? timeoutMs = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            try
            {
                await ProxyManager.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw UtsAssert.Failure("create_proxy_session: the proxy isn't running", callerFilePath, callerLineNumber, ex);
            }

            var effectiveEndpoint = endpoint ?? SandboxApp.DefaultEndpoint;
            var request = BuildCreateRequest(endpoint, rules, port, timeoutMs);
            var response = await SendAsync(HttpMethod.Post, "/sessions", request, callerFilePath, callerLineNumber).ConfigureAwait(false);

            var sessionId = (string)response["sessionId"];
            var proxyPort = (int?)response["proxy"]?["port"];
            if (string.IsNullOrEmpty(sessionId) || proxyPort == null)
            {
                throw UtsAssert.Failure(
                    $"create_proxy_session: POST /sessions answered without sessionId or proxy.port: {response.ToString(Formatting.None)}",
                    callerFilePath,
                    callerLineNumber);
            }

            return new ProxySession(sessionId, proxyPort.Value, effectiveEndpoint, (string)request["target"]["realtimeHost"]);
        }

        /// <summary>
        /// The <c>POST /sessions</c> body for <see cref="CreateAsync"/>, built without the network: the endpoint
        /// (the sandbox when null) resolved to <c>target { realtimeHost, restHost }</c>, the rules, the port and
        /// the timeout.
        /// </summary>
        internal static JObject BuildCreateRequest(string endpoint, IEnumerable<JObject> rules, int? port, int? timeoutMs)
        {
            var host = Defaults.GetPrimaryDomain(endpoint ?? SandboxApp.DefaultEndpoint);
            var body = new JObject
            {
                ["target"] = new JObject
                {
                    ["realtimeHost"] = host,
                    ["restHost"] = host,
                },
                ["timeoutMs"] = timeoutMs ?? DefaultTimeoutMs,
            };

            if (rules != null)
            {
                body["rules"] = new JArray(rules.Cast<object>().ToArray());
            }

            if (port.HasValue)
            {
                body["port"] = port.Value;
            }

            return body;
        }

        /// <summary>The specs' <c>session.add_rules(rules, position:)</c>.</summary>
        /// <param name="position"><see cref="ProxyRules.Append"/> (default) or <see cref="ProxyRules.Prepend"/>.</param>
        public async Task AddRulesAsync(
            IEnumerable<JObject> rules,
            string position = ProxyRules.Append,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            if (position != ProxyRules.Append && position != ProxyRules.Prepend)
            {
                throw UtsAssert.Failure($"add_rules: position must be \"append\" or \"prepend\", not \"{position}\"", callerFilePath, callerLineNumber);
            }

            var body = new JObject
            {
                ["rules"] = new JArray((rules ?? Enumerable.Empty<JObject>()).Cast<object>().ToArray()),
                ["position"] = position,
            };
            await SendAsync(HttpMethod.Post, $"/sessions/{SessionId}/rules", body, callerFilePath, callerLineNumber).ConfigureAwait(false);
        }

        /// <summary>The specs' <c>session.trigger_action(action)</c>, for example <c>ProxyRules.Disconnect()</c>.</summary>
        public async Task TriggerActionAsync(
            JObject action,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            await SendAsync(HttpMethod.Post, $"/sessions/{SessionId}/actions", action, callerFilePath, callerLineNumber).ConfigureAwait(false);
        }

        /// <summary>The specs' <c>session.get_log()</c>: every event the session recorded, in order, typed.</summary>
        public async Task<List<ProxyEvent>> GetLogAsync(
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var response = await SendAsync(HttpMethod.Get, $"/sessions/{SessionId}/log", null, callerFilePath, callerLineNumber).ConfigureAwait(false);
            var events = response["events"] as JArray;
            return events == null
                ? new List<ProxyEvent>()
                : events.Children<JObject>().Select(ProxyEvent.Parse).ToList();
        }

        /// <summary>Whether the control API still knows the session (<c>GET /sessions/{id}</c> answers 2xx).</summary>
        public async Task<bool> ExistsAsync()
        {
            try
            {
                using (var response = await Control.GetAsync(ProxyManager.ControlUrl + $"/sessions/{SessionId}").ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The specs' <c>session.close()</c>: <c>DELETE /sessions/{id}</c>. Best-effort, never throws,
        /// idempotent.
        /// </summary>
        /// <returns>A task that completes when the request has been answered or has failed.</returns>
        public async Task CloseAsync()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
            try
            {
                // Any answer will do: a session the proxy already collected is closed too.
                var response = await Control.DeleteAsync(ProxyManager.ControlUrl + $"/sessions/{SessionId}").ConfigureAwait(false);
                response.Dispose();
            }
            catch (Exception)
            {
                // Closing must never be the reason a test fails.
            }
        }

        public override string ToString() => $"proxy session {SessionId} on {ProxyHost}:{ProxyPort} -> {TargetHost}";

        private static async Task<JObject> SendAsync(
            HttpMethod method,
            string path,
            JObject body,
            string callerFilePath,
            int callerLineNumber)
        {
            var what = $"uts-proxy control API {method} {path}";
            string text;
            int status;
            try
            {
                using (var request = new HttpRequestMessage(method, ProxyManager.ControlUrl + path))
                {
                    if (body != null)
                    {
                        request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    }

                    using (var response = await Control.SendAsync(request).ConfigureAwait(false))
                    {
                        status = (int)response.StatusCode;
                        text = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                throw UtsAssert.Failure($"{what} failed", callerFilePath, callerLineNumber, ex);
            }

            if (status < 200 || status > 299)
            {
                throw UtsAssert.Failure($"{what} answered {status}: {text}", callerFilePath, callerLineNumber);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new JObject();
            }

            try
            {
                return JToken.Parse(text) as JObject ?? new JObject();
            }
            catch (JsonException ex)
            {
                throw UtsAssert.Failure($"{what} answered {status} with a body that isn't JSON: {text}", callerFilePath, callerLineNumber, ex);
            }
        }
    }
}
