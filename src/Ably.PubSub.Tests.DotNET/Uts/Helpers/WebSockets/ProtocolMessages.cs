using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The protocol-message templates and builders of <c>mock_websocket.md</c>.
    ///
    /// Everything here is a <see cref="JObject"/> of wire names rather than a <c>ProtocolMessage</c>, for
    /// two reasons. Unknown fields and unknown action numbers survive to the wire untouched, so
    /// <c>send_to_client</c> with a literal already *is* the specs' <c>send_to_client_raw</c> and the
    /// forwards-compatibility specs (RTF1/RSF1) need no extra mock method. And a template cannot be
    /// mutated by one test into something the next test inherits: the builders return fresh objects.
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

        public const string TestConnectionId = "test-connection-id";
        public const string TestConnectionKey = "test-connection-key";

        /// <summary>The spec's <c>CONNECTED_MESSAGE</c>.</summary>
        public static JObject ConnectedMessage() => ConnectedMessage(TestConnectionId);

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

        /// <summary>
        /// CONNECTED with the idle timer disabled. A <c>maxIdleInterval</c> of 0 stops the transport
        /// arming its idle check, which otherwise fires on its own schedule and disturbs any test that is
        /// measuring something else.
        /// </summary>
        public static JObject ConnectedMessageNoIdle() => ConnectedMessage(maxIdleInterval: 0);

        public static JObject ClosedMessage() => new JObject { ["action"] = Closed };

        public static JObject HeartbeatMessage() => new JObject { ["action"] = Heartbeat };

        public static JObject DisconnectedMessage(
            int code = 80003,
            string message = "Connection disconnected",
            int statusCode = 500)
            => new JObject
            {
                ["action"] = Disconnected,
                ["error"] = ErrorObject(code, message, statusCode),
            };

        /// <summary>
        /// The spec's <c>ERROR_MESSAGE(code, message)</c>. Its <c>statusCode: code / 100</c> is integer
        /// division on the wire code, so 40142 becomes 401 and 80000-range codes — which have no HTTP
        /// equivalent — default to 500 as ably-python's template does.
        /// </summary>
        public static JObject ErrorMessage(int code, string message, int? statusCode = null)
            => new JObject
            {
                ["action"] = Error,
                ["error"] = ErrorObject(code, message, statusCode ?? DefaultStatusCode(code)),
            };

        /// <summary>A channel-scoped ERROR: an attach failure that does not end the connection.</summary>
        public static JObject ChannelErrorMessage(string channel, int code, string message, int? statusCode = null)
        {
            var error = ErrorMessage(code, message, statusCode);
            error["channel"] = channel;
            return error;
        }

        public static JObject AttachedMessage(string channel, IDictionary<string, JToken> fields = null)
            => Channelled(Attached, channel, fields);

        public static JObject DetachedMessage(string channel, IDictionary<string, JToken> fields = null)
            => Channelled(Detached, channel, fields);

        /// <summary>A DETACHED carrying an error — the RTL13 server-initiated detach.</summary>
        public static JObject ServerDetachedMessage(string channel, int code, string message, int? statusCode = null)
        {
            var detached = Channelled(Detached, channel, null);
            detached["error"] = ErrorObject(code, message, statusCode ?? DefaultStatusCode(code));
            return detached;
        }

        public static JObject MessageProtocolMessage(
            string channel,
            JArray messages,
            IDictionary<string, JToken> fields = null)
        {
            var protocolMessage = Channelled(Message, channel, fields);
            protocolMessage["messages"] = messages;
            return protocolMessage;
        }

        public static JObject PresenceProtocolMessage(
            string channel,
            JArray presence,
            IDictionary<string, JToken> fields = null)
        {
            var protocolMessage = Channelled(Presence, channel, fields);
            protocolMessage["presence"] = presence;
            return protocolMessage;
        }

        /// <summary>
        /// The specs' <c>HAS_PRESENCE</c> flag, for an ATTACHED that promises a presence sync.
        /// Matches <c>ProtocolMessage.Flag.HasPresence</c>, which is internal to the SDK.
        /// </summary>
        public const int HasPresenceFlag = 1 << 0;

        /// <summary>An ATTACHED carrying HAS_PRESENCE, so a SYNC is expected to follow.</summary>
        public static JObject AttachedWithPresenceMessage(string channel)
            => AttachedMessage(channel, new Dictionary<string, JToken> { ["flags"] = HasPresenceFlag });

        /// <summary>
        /// One entry for a SYNC or PRESENCE message's <c>presence</c> array, in the shape the specs
        /// write it. <paramref name="action"/> is the numeric presence action: 0 absent, 1 present,
        /// 2 enter, 3 leave, 4 update.
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

        public static JObject SyncMessage(string channel, string channelSerial, JArray presence)
        {
            var protocolMessage = Channelled(Sync, channel, null);
            protocolMessage["channelSerial"] = channelSerial;
            protocolMessage["presence"] = presence;
            return protocolMessage;
        }

        /// <summary>An ACK for a message the client sent, built against its captured <c>msgSerial</c>.</summary>
        public static JObject AckMessage(long msgSerial, int count = 1)
            => new JObject { ["action"] = Ack, ["msgSerial"] = msgSerial, ["count"] = count };

        public static JObject NackMessage(
            long msgSerial,
            int code,
            string description,
            int? statusCode = null,
            int count = 1)
            => new JObject
            {
                ["action"] = Nack,
                ["msgSerial"] = msgSerial,
                ["count"] = count,
                ["error"] = ErrorObject(code, description, statusCode ?? DefaultStatusCode(code)),
            };

        /// <summary>An AUTH, the server's request that the client reauthenticate (RTN22).</summary>
        public static JObject AuthMessage() => new JObject { ["action"] = Auth };

        public static JObject ErrorObject(int code, string message, int statusCode)
            => new JObject
            {
                ["code"] = code,
                ["message"] = message,
                ["statusCode"] = statusCode,
            };

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

        private static int DefaultStatusCode(int code)
        {
            // 4xxxx -> 4xx, 5xxxx -> 5xx. 8xxxx and 9xxxx are connection/transport codes with no HTTP
            // equivalent, and the server sends them with a 500.
            var prefix = code / 100;
            return prefix >= 400 && prefix < 600 ? prefix : 500;
        }
    }
}
