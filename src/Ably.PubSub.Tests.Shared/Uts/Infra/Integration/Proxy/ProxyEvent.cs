using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>One entry of a proxy session's event log (<c>proxy.md</c> "Event Log"), typed.</summary>
    public sealed class ProxyEvent
    {
        public const string WsConnectType = "ws_connect";
        public const string WsFrameType = "ws_frame";
        public const string WsDisconnectType = "ws_disconnect";
        public const string HttpRequestType = "http_request";
        public const string HttpResponseType = "http_response";
        public const string ActionType = "action";

        public const string ServerToClient = "server_to_client";
        public const string ClientToServer = "client_to_server";

        private ProxyEvent(JObject raw)
        {
            Raw = raw;
            Type = (string)raw["type"];
            Url = (string)raw["url"];
            QueryParams = ReadQueryParams(raw["queryParams"]);
            Direction = (string)raw["direction"];
            Message = raw["message"] as JObject;
            MessageAction = ReadInt(Message?["action"]);
            Initiator = (string)raw["initiator"];
            CloseCode = ReadInt(raw["closeCode"]);
            Method = (string)raw["method"];
            Path = (string)raw["path"];
            Status = ReadInt(raw["status"]);

            // v0.3.0 always writes ruleMatched, as JSON null for pass-through traffic (checked against the real proxy);
            // "" is treated the same.
            var ruleMatched = raw["ruleMatched"]?.Type == JTokenType.String ? (string)raw["ruleMatched"] : null;
            RuleMatched = string.IsNullOrEmpty(ruleMatched) ? null : ruleMatched;
            Timestamp = ReadTimestamp(raw["timestamp"]);
        }

        /// <summary>The entry as the control API returned it.</summary>
        public JObject Raw { get; }

        /// <summary>
        /// <c>ws_connect</c>, <c>ws_frame</c>, <c>ws_disconnect</c>, <c>http_request</c>, <c>http_response</c> or
        /// <c>action</c>.
        /// </summary>
        public string Type { get; }

        /// <summary>When the proxy recorded it, if the entry says.</summary>
        public DateTimeOffset? Timestamp { get; }

        /// <summary><c>ws_connect</c>: the URL the client connected to.</summary>
        public string Url { get; }

        /// <summary>
        /// <c>ws_connect</c>: the connection's query parameters (the first value of a repeated one). Empty, never
        /// null.
        /// </summary>
        public IReadOnlyDictionary<string, string> QueryParams { get; }

        /// <summary><c>ws_frame</c>: <see cref="ServerToClient"/> or <see cref="ClientToServer"/>.</summary>
        public string Direction { get; }

        /// <summary><c>ws_frame</c>: the protocol message; <c>action</c>: the imperative action.</summary>
        public JObject Message { get; }

        /// <summary><c>ws_frame</c>: the protocol message's numeric <c>action</c>.</summary>
        public int? MessageAction { get; }

        /// <summary>
        /// <c>ws_disconnect</c>: who closed (<c>client</c>, <c>server</c>, <c>proxy</c>); <c>action</c>:
        /// <c>proxy</c>.
        /// </summary>
        public string Initiator { get; }

        /// <summary><c>ws_disconnect</c>: the close code, when the proxy logs one (v0.3.0 omits it for a server close).</summary>
        public int? CloseCode { get; }

        /// <summary><c>http_request</c>: the method.</summary>
        public string Method { get; }

        /// <summary><c>http_request</c>: the path (with the query string, as the proxy logs it).</summary>
        public string Path { get; }

        /// <summary><c>http_response</c>: the status.</summary>
        public int? Status { get; }

        /// <summary>The comment (or <c>rule-N</c>) of the rule that handled this traffic, or null for pass-through.</summary>
        public string RuleMatched { get; }

        public bool IsServerToClient => Type == WsFrameType && Direction == ServerToClient;

        public bool IsClientToServer => Type == WsFrameType && Direction == ClientToServer;

        /// <summary>
        /// The entry as JSON with credentials redacted (<see cref="ProxyRedaction.Redact(JObject)"/>), for output
        /// lines.
        /// </summary>
        public override string ToString() => ProxyRedaction.Redact(Raw).ToString(Newtonsoft.Json.Formatting.None);

        internal static ProxyEvent Parse(JObject raw) => new ProxyEvent(raw ?? throw new ArgumentNullException(nameof(raw)));

        private static IReadOnlyDictionary<string, string> ReadQueryParams(JToken token)
        {
            var values = new Dictionary<string, string>();
            if (token is JObject query)
            {
                foreach (var property in query.Properties())
                {
                    // Go's url.Values serialises as a list per key; a plain map gives a string.
                    var value = property.Value is JArray list ? list.FirstOrDefault() : property.Value;
                    values[property.Name] = value == null || value.Type == JTokenType.Null ? null : value.ToString();
                }
            }

            return values;
        }

        private static int? ReadInt(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return (int)token;
            }

            return token.Type == JTokenType.String && int.TryParse((string)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : (int?)null;
        }

        private static DateTimeOffset? ReadTimestamp(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)token);
            }

            if (token.Type == JTokenType.Date)
            {
                return token.ToObject<DateTimeOffset>();
            }

            return DateTimeOffset.TryParse((string)token, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
        }
    }

    /// <summary>
    /// Keeps credentials out of what the proxy infra prints: the <c>accessToken</c> and <c>key</c> query values
    /// (in a URL, a path or a <c>queryParams</c> map) and any <c>Authorization</c> header (the proxy logs request
    /// headers on <c>http_request</c>, bearer token included) become <see cref="Redacted"/>.
    /// </summary>
    public static class ProxyRedaction
    {
        public const string Redacted = "<redacted>";

        private static readonly string[] SecretQueryKeys = { "accessToken", "key" };

        private static readonly Regex SecretQueryValue = new Regex(
            @"(?<=[?&](?:accessToken|key)=)[^&#\s""]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AuthorizationValue = new Regex(
            @"(\bAuthorization[""']?\s*[:=]\s*[""']?)(?:(?:Bearer|Basic)\s+)?[^\s""',}\]]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A copy of <paramref name="entry"/> with every secret query value and Authorization header redacted.</summary>
        public static JObject Redact(JObject entry)
        {
            if (entry == null)
            {
                return null;
            }

            var copy = (JObject)entry.DeepClone();
            RedactToken(copy);
            return copy;
        }

        /// <summary>
        /// Free text (a URL, a path, a proxy output line) with every secret query value and Authorization value
        /// redacted.
        /// </summary>
        public static string RedactText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return AuthorizationValue.Replace(SecretQueryValue.Replace(text, Redacted), "${1}" + Redacted);
        }

        private static void RedactToken(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties().ToList())
                {
                    if (string.Equals(property.Name, "Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        property.Value = Redacted;
                    }
                    else if (string.Equals(property.Name, "queryParams", StringComparison.Ordinal) && property.Value is JObject query)
                    {
                        foreach (var param in query.Properties().ToList())
                        {
                            if (SecretQueryKeys.Any(k => string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                param.Value = Redacted;
                            }
                        }
                    }
                    else
                    {
                        RedactToken(property.Value);
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    RedactToken(item);
                }
            }
            else if (token is JValue value && value.Type == JTokenType.String)
            {
                var text = (string)value.Value;
                var redacted = RedactText(text);
                if (!string.Equals(text, redacted, StringComparison.Ordinal))
                {
                    value.Value = redacted;
                }
            }
        }
    }

    /// <summary>Filters over a session's typed log, so the specs' log assertions read like the pseudocode.</summary>
    public static class ProxyLog
    {
        /// <summary>The specs' <c>log.filter(e =&gt; e.type == type)</c>.</summary>
        public static List<ProxyEvent> OfType(this IEnumerable<ProxyEvent> log, string type)
            => log.Where(e => e.Type == type).ToList();

        public static List<ProxyEvent> WsConnects(this IEnumerable<ProxyEvent> log) => log.OfType(ProxyEvent.WsConnectType);

        public static List<ProxyEvent> WsDisconnects(this IEnumerable<ProxyEvent> log) => log.OfType(ProxyEvent.WsDisconnectType);

        /// <summary>Frames in one direction, optionally only those with a protocol action (a number, as in the log).</summary>
        public static List<ProxyEvent> Frames(this IEnumerable<ProxyEvent> log, string direction, int? action = null)
            => log.Where(e => e.Type == ProxyEvent.WsFrameType && e.Direction == direction)
                .Where(e => action == null || e.MessageAction == action)
                .ToList();

        public static List<ProxyEvent> FramesToClient(this IEnumerable<ProxyEvent> log, int? action = null)
            => log.Frames(ProxyEvent.ServerToClient, action);

        public static List<ProxyEvent> FramesToServer(this IEnumerable<ProxyEvent> log, int? action = null)
            => log.Frames(ProxyEvent.ClientToServer, action);

        /// <summary>HTTP requests, optionally only those whose path contains <paramref name="pathContains"/>.</summary>
        public static List<ProxyEvent> HttpRequests(this IEnumerable<ProxyEvent> log, string pathContains = null)
            => log.Where(e => e.Type == ProxyEvent.HttpRequestType)
                .Where(e => pathContains == null || (e.Path ?? string.Empty).Contains(pathContains))
                .ToList();

        public static List<ProxyEvent> HttpResponses(this IEnumerable<ProxyEvent> log) => log.OfType(ProxyEvent.HttpResponseType);

        /// <summary>The imperative actions the session was asked to perform (<c>trigger_action</c>).</summary>
        public static List<ProxyEvent> Actions(this IEnumerable<ProxyEvent> log) => log.OfType(ProxyEvent.ActionType);

        /// <summary>Every event a rule handled, by the rule's comment.</summary>
        public static List<ProxyEvent> MatchedBy(this IEnumerable<ProxyEvent> log, string comment)
            => log.Where(e => e.RuleMatched == comment).ToList();
    }
}
