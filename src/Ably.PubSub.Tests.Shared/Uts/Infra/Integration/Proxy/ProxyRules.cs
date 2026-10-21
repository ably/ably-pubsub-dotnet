using System;
using System.Collections.Generic;
using System.Globalization;
using Ably.PubSub.Types;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>
    /// Builders for <c>proxy.md</c>'s rule format: match conditions, actions, and <see cref="Rule"/>, which puts
    /// a match and an action together.
    /// </summary>
    public static class ProxyRules
    {
        /// <summary>The <c>add_rules</c> position that puts new rules after the existing ones (the default).</summary>
        public const string Append = "append";

        /// <summary>The <c>add_rules</c> position that puts new rules ahead of the existing ones.</summary>
        public const string Prepend = "prepend";

        /// <summary>A rule: <c>{ match, action, times?, comment? }</c>.</summary>
        /// <param name="times">After this many firings the rule removes itself; null means it never does.</param>
        public static JObject Rule(JObject match, JObject action, int? times = null, string comment = null)
        {
            var rule = new JObject
            {
                ["match"] = match ?? throw new ArgumentNullException(nameof(match)),
                ["action"] = action ?? throw new ArgumentNullException(nameof(action)),
            };

            if (times.HasValue)
            {
                rule["times"] = times.Value;
            }

            if (comment != null)
            {
                rule["comment"] = comment;
            }

            return rule;
        }

        /// <summary><c>{ "type": "ws_connect", "count"?, "queryContains"? }</c>.</summary>
        /// <param name="queryContains">Query parameters the connection must carry; <c>"*"</c> matches any value.</param>
        public static JObject WsConnect(int? count = null, IDictionary<string, string> queryContains = null)
        {
            var match = NewMatch("ws_connect");
            if (count.HasValue)
            {
                match["count"] = count.Value;
            }

            if (queryContains != null)
            {
                var query = new JObject();
                foreach (var pair in queryContains)
                {
                    query[pair.Key] = pair.Value;
                }

                match["queryContains"] = query;
            }

            return match;
        }

        /// <summary>
        /// <c>{ "type": "ws_frame_to_client", "action": "&lt;n&gt;", "channel"? }</c>: a server-to-client frame.
        /// </summary>
        public static JObject WsFrameToClient(int action, string channel = null) => Frame("ws_frame_to_client", action, channel);

        /// <summary><see cref="WsFrameToClient(int, string)"/> with the SDK's action enum.</summary>
        public static JObject WsFrameToClient(ProtocolMessage.MessageAction action, string channel = null) => WsFrameToClient((int)action, channel);

        /// <summary>
        /// <c>{ "type": "ws_frame_to_server", "action": "&lt;n&gt;", "channel"? }</c>: a client-to-server frame.
        /// </summary>
        public static JObject WsFrameToServer(int action, string channel = null) => Frame("ws_frame_to_server", action, channel);

        /// <summary><see cref="WsFrameToServer(int, string)"/> with the SDK's action enum.</summary>
        public static JObject WsFrameToServer(ProtocolMessage.MessageAction action, string channel = null) => WsFrameToServer((int)action, channel);

        /// <summary><c>{ "type": "http_request", "method"?, "pathContains"? }</c>.</summary>
        public static JObject HttpRequest(string method = null, string pathContains = null)
        {
            var match = NewMatch("http_request");
            if (method != null)
            {
                match["method"] = method;
            }

            if (pathContains != null)
            {
                match["pathContains"] = pathContains;
            }

            return match;
        }

        /// <summary>
        /// <c>{ "type": "delay_after_ws_connect", "delayMs" }</c>: fires once, that long after a WebSocket
        /// connect.
        /// </summary>
        public static JObject DelayAfterWsConnect(int delayMs)
        {
            var match = NewMatch("delay_after_ws_connect");
            match["delayMs"] = delayMs;
            return match;
        }

        /// <summary><c>{ "type": "passthrough" }</c>.</summary>
        public static JObject Passthrough() => NewAction("passthrough");

        /// <summary><c>{ "type": "refuse_connection" }</c>.</summary>
        public static JObject RefuseConnection() => NewAction("refuse_connection");

        /// <summary><c>{ "type": "accept_and_close", "closeCode" }</c>.</summary>
        public static JObject AcceptAndClose(int closeCode = 1011)
        {
            var action = NewAction("accept_and_close");
            action["closeCode"] = closeCode;
            return action;
        }

        /// <summary>
        /// <c>{ "type": "disconnect" }</c>: drop the connection without a close frame. Also an imperative action.
        /// </summary>
        public static JObject Disconnect() => NewAction("disconnect");

        /// <summary><c>{ "type": "close", "closeCode" }</c>: a clean close. Also an imperative action.</summary>
        public static JObject Close(int closeCode = 1000)
        {
            var action = NewAction("close");
            action["closeCode"] = closeCode;
            return action;
        }

        /// <summary><c>{ "type": "suppress" }</c>: drop the matched frame.</summary>
        public static JObject Suppress() => NewAction("suppress");

        /// <summary><c>{ "type": "delay", "delayMs" }</c>: forward the matched frame that much later.</summary>
        public static JObject Delay(int delayMs)
        {
            var action = NewAction("delay");
            action["delayMs"] = delayMs;
            return action;
        }

        /// <summary><c>{ "type": "inject_to_client", "message" }</c>. Also an imperative action.</summary>
        public static JObject InjectToClient(JObject message)
        {
            var action = NewAction("inject_to_client");
            action["message"] = message ?? throw new ArgumentNullException(nameof(message));
            return action;
        }

        /// <summary>
        /// <c>{ "type": "inject_to_client_and_close", "message", "closeCode"? }</c>. Also an imperative action.
        /// </summary>
        public static JObject InjectToClientAndClose(JObject message, int? closeCode = null)
        {
            var action = NewAction("inject_to_client_and_close");
            action["message"] = message ?? throw new ArgumentNullException(nameof(message));
            if (closeCode.HasValue)
            {
                action["closeCode"] = closeCode.Value;
            }

            return action;
        }

        /// <summary><c>{ "type": "replace", "message" }</c>: send this instead of the matched frame.</summary>
        public static JObject Replace(JObject message)
        {
            var action = NewAction("replace");
            action["message"] = message ?? throw new ArgumentNullException(nameof(message));
            return action;
        }

        /// <summary>
        /// <c>{ "type": "suppress_onwards" }</c>: drop the matched frame and everything after it in that
        /// direction.
        /// </summary>
        public static JObject SuppressOnwards() => NewAction("suppress_onwards");

        /// <summary><c>{ "type": "http_respond", "status", "body"?, "headers"? }</c>: answer without forwarding upstream.</summary>
        public static JObject HttpRespond(int status, JToken body = null, IDictionary<string, string> headers = null)
            => HttpResponseAction("http_respond", status, body, headers);

        /// <summary>
        /// <c>{ "type": "http_replace_response", "status", "body"?, "headers"? }</c>: forward the request upstream
        /// (so the server acts on it), discard the real response and answer with this one instead. Supported by
        /// uts-proxy v0.3.0; not yet in <c>proxy.md</c>.
        /// </summary>
        public static JObject HttpReplaceResponse(int status, JToken body = null, IDictionary<string, string> headers = null)
            => HttpResponseAction("http_replace_response", status, body, headers);

        /// <summary><c>{ "type": "http_delay", "delayMs" }</c>.</summary>
        public static JObject HttpDelay(int delayMs)
        {
            var action = NewAction("http_delay");
            action["delayMs"] = delayMs;
            return action;
        }

        /// <summary><c>{ "type": "http_drop" }</c>: close the HTTP connection without answering.</summary>
        public static JObject HttpDrop() => NewAction("http_drop");

        private static JObject NewMatch(string type) => new JObject { ["type"] = type };

        private static JObject NewAction(string type) => new JObject { ["type"] = type };

        private static JObject HttpResponseAction(string type, int status, JToken body, IDictionary<string, string> headers)
        {
            var action = NewAction(type);
            action["status"] = status;
            if (body != null)
            {
                action["body"] = body;
            }

            if (headers != null)
            {
                var values = new JObject();
                foreach (var pair in headers)
                {
                    values[pair.Key] = pair.Value;
                }

                action["headers"] = values;
            }

            return action;
        }

        private static JObject Frame(string type, int action, string channel)
        {
            var match = NewMatch(type);

            // A string, never a number.
            match["action"] = action.ToString(CultureInfo.InvariantCulture);
            if (channel != null)
            {
                match["channel"] = channel;
            }

            return match;
        }
    }
}
