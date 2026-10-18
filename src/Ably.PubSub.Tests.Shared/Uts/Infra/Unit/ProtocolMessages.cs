using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The protocol-message templates of <c>mock_websocket.md</c> (<c>CONNECTED_MESSAGE</c>,
    /// <c>CLOSED_MESSAGE</c>, <c>DISCONNECTED_MESSAGE</c>, <c>HEARTBEAT_MESSAGE</c>,
    /// <c>ERROR_MESSAGE(code, message)</c>, <c>PING_MESSAGE(id)</c>), the wire action numbers, and builders
    /// for the other messages the corpus sends (ATTACHED, DETACHED, MESSAGE, PRESENCE, SYNC, ACK, NACK, AUTH).
    ///
    /// Every template is a fresh <see cref="JObject"/> of wire names, not a <c>ProtocolMessage</c>: unknown
    /// fields and action numbers survive to the wire untouched, and one test can't mutate a template the
    /// next test inherits. A spec's inline <c>ProtocolMessage(action: X, ...)</c> becomes a JObject literal
    /// with these constants.
    /// </summary>
    public static class ProtocolMessages
    {
        public const int Heartbeat = 0;
        public const int Ack = 1;
        public const int Nack = 2;
        public const int Connect = 3;
        public const int Connected = 4;
        public const int Disconnect = 5;
        public const int Disconnected = 6;
        public const int Close = 7;
        public const int Closed = 8;
        public const int Error = 9;
        public const int Attach = 10;
        public const int Attached = 11;
        public const int Detach = 12;
        public const int Detached = 13;
        public const int Presence = 14;
        public const int Message = 15;
        public const int Sync = 16;
        public const int Auth = 17;
        public const int Activate = 18;
        public const int Object = 19;
        public const int ObjectSync = 20;
        public const int Annotation = 21;
        public const int Ping = 22;
        public const int Pong = 23;

        /// <summary>The specs' <c>HAS_PRESENCE</c> channel flag (<c>ProtocolMessage.Flag.HasPresence</c>, internal to the SDK).</summary>
        public const int HasPresenceFlag = 1 << 0;

        public const string TestConnectionId = "test-connection-id";
        public const string TestConnectionKey = "test-connection-key";

        /// <summary>The spec's <c>CONNECTED_MESSAGE</c>.</summary>
        public static JObject ConnectedMessage() => ConnectedMessage(TestConnectionId);

        /// <summary>CONNECTED with chosen details; <c>connectionStateTtl</c> and <c>maxIdleInterval</c> in wire milliseconds.</summary>
        public static JObject ConnectedMessage(
            string connectionId = TestConnectionId,
            string connectionKey = TestConnectionKey,
            string clientId = null,
            long connectionStateTtl = 120000,
            long maxIdleInterval = 15000)
        {
            var details = new JObject
            {
                ["connectionKey"] = connectionKey,
                ["connectionStateTtl"] = connectionStateTtl,
                ["maxIdleInterval"] = maxIdleInterval,
            };

            if (clientId != null)
            {
                details["clientId"] = clientId;
            }

            return new JObject
            {
                ["action"] = Connected,
                ["connectionId"] = connectionId,
                ["connectionDetails"] = details,
            };
        }

        /// <summary>The spec's <c>CLOSED_MESSAGE</c>.</summary>
        public static JObject ClosedMessage() => new JObject { ["action"] = Closed };

        /// <summary>The spec's <c>HEARTBEAT_MESSAGE</c>.</summary>
        public static JObject HeartbeatMessage() => new JObject { ["action"] = Heartbeat };

        /// <summary>
        /// The spec's <c>DISCONNECTED_MESSAGE</c>: <c>error: {code: 80003, message: "Connection disconnected"}</c>,
        /// with no <c>statusCode</c> unless one is given (the template has none).
        /// </summary>
        public static JObject DisconnectedMessage(
            int code = 80003,
            string message = "Connection disconnected",
            int? statusCode = null)
            => new JObject
            {
                ["action"] = Disconnected,
                ["error"] = ErrorObject(code, message, statusCode),
            };

        /// <summary>
        /// The spec's <c>ERROR_MESSAGE(code, message)</c>, with <c>statusCode: code / 100</c> exactly as the
        /// template says (integer division: 40142 gives 401, 80000 gives 800, which is not an HTTP status).
        /// Pass <paramref name="statusCode"/> to override.
        /// </summary>
        public static JObject ErrorMessage(int code, string message, int? statusCode = null)
            => new JObject
            {
                ["action"] = Error,
                ["error"] = ErrorObject(code, message, statusCode ?? code / 100),
            };

        /// <summary>A channel-scoped ERROR: an attach failure that doesn't end the connection.</summary>
        public static JObject ChannelErrorMessage(string channel, int code, string message, int? statusCode = null)
        {
            var error = ErrorMessage(code, message, statusCode);
            error["channel"] = channel;
            return error;
        }

        /// <summary>The spec's <c>PING_MESSAGE(id)</c> (RTN23c1): the client must answer <c>{action: PONG, id}</c>.</summary>
        public static JObject PingMessage(string id) => new JObject { ["action"] = Ping, ["id"] = id };

        public static JObject AttachedMessage(string channel, IDictionary<string, JToken> fields = null)
            => Channelled(Attached, channel, fields);

        /// <summary>An ATTACHED carrying HAS_PRESENCE, so a SYNC is expected to follow.</summary>
        public static JObject AttachedWithPresenceMessage(string channel)
            => AttachedMessage(channel, new Dictionary<string, JToken> { ["flags"] = HasPresenceFlag });

        public static JObject DetachedMessage(string channel, IDictionary<string, JToken> fields = null)
            => Channelled(Detached, channel, fields);

        /// <summary>A DETACHED carrying an error: the RTL13 server-initiated detach. <c>statusCode</c> defaults to <c>code / 100</c>.</summary>
        public static JObject ServerDetachedMessage(string channel, int code, string message, int? statusCode = null)
        {
            var detached = Channelled(Detached, channel, null);
            detached["error"] = ErrorObject(code, message, statusCode ?? code / 100);
            return detached;
        }

        public static JObject MessageProtocolMessage(string channel, JArray messages, IDictionary<string, JToken> fields = null)
        {
            var protocolMessage = Channelled(Message, channel, fields);
            protocolMessage["messages"] = messages;
            return protocolMessage;
        }

        public static JObject PresenceProtocolMessage(string channel, JArray presence, IDictionary<string, JToken> fields = null)
        {
            var protocolMessage = Channelled(Presence, channel, fields);
            protocolMessage["presence"] = presence;
            return protocolMessage;
        }

        public static JObject SyncMessage(string channel, string channelSerial, JArray presence)
        {
            var protocolMessage = Channelled(Sync, channel, null);
            protocolMessage["channelSerial"] = channelSerial;
            protocolMessage["presence"] = presence;
            return protocolMessage;
        }

        /// <summary>
        /// One entry of a PRESENCE or SYNC message's <c>presence</c> array. <paramref name="action"/> is the
        /// numeric presence action: 0 absent, 1 present, 2 enter, 3 leave, 4 update.
        /// </summary>
        public static JObject PresenceEntry(
            int action,
            string clientId,
            string connectionId = null,
            string id = null,
            long? timestamp = null,
            object data = null)
        {
            var entry = new JObject
            {
                ["action"] = action,
                ["clientId"] = clientId,
            };

            if (connectionId != null)
            {
                entry["connectionId"] = connectionId;
            }

            if (id != null)
            {
                entry["id"] = id;
            }

            if (timestamp.HasValue)
            {
                entry["timestamp"] = timestamp.Value;
            }

            if (data != null)
            {
                entry["data"] = JToken.FromObject(data);
            }

            return entry;
        }

        /// <summary>An ACK for messages the client sent, built against its captured <c>msgSerial</c>.</summary>
        public static JObject AckMessage(long msgSerial, int count = 1)
            => new JObject { ["action"] = Ack, ["msgSerial"] = msgSerial, ["count"] = count };

        /// <summary>A NACK; <c>statusCode</c> defaults to <c>code / 100</c>.</summary>
        public static JObject NackMessage(long msgSerial, int code, string message, int? statusCode = null, int count = 1)
            => new JObject
            {
                ["action"] = Nack,
                ["msgSerial"] = msgSerial,
                ["count"] = count,
                ["error"] = ErrorObject(code, message, statusCode ?? code / 100),
            };

        /// <summary>An AUTH: the server asks the client to reauthenticate (RTN22).</summary>
        public static JObject AuthMessage() => new JObject { ["action"] = Auth };

        /// <summary>An ErrorInfo on the wire; <c>statusCode</c> is left out when null.</summary>
        public static JObject ErrorObject(int code, string message, int? statusCode = null)
        {
            var error = new JObject
            {
                ["code"] = code,
                ["message"] = message,
            };

            if (statusCode.HasValue)
            {
                error["statusCode"] = statusCode.Value;
            }

            return error;
        }

        private static JObject Channelled(int action, string channel, IDictionary<string, JToken> fields)
        {
            var protocolMessage = new JObject
            {
                ["action"] = action,
                ["channel"] = channel,
            };

            if (fields != null)
            {
                foreach (var field in fields)
                {
                    protocolMessage[field.Key] = field.Value;
                }
            }

            return protocolMessage;
        }
    }
}
