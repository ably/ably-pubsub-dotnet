using System;
using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>
    /// provides details on the constraints or defaults for the connection such as max message size, client ID or connection state TTL.
    /// </summary>
    public class ConnectionDetails
    {
        /// <summary>
        /// Client id associated with the current connection.
        /// </summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        /// <summary>
        /// Connection key.
        /// </summary>
        [JsonProperty("connectionKey")]
        public string ConnectionKey { get; set; }

        /// <summary>
        /// Optional Connection state time to live.
        /// </summary>
        [JsonProperty("connectionStateTtl")]
        public TimeSpan? ConnectionStateTtl { get; set; }

        /// <summary>
        /// Max frame size.
        /// </summary>
        [JsonProperty("maxFrameSize")]
        public long MaxFrameSize { get; set; }

        /// <summary>
        /// The maximum length of time that the server will allow no activity to occur in the
        /// server to client direction. After such a period of inactivity the server will send a
        /// Heartbeat or a transport level ping. A value of zero means the server allows
        /// arbitrarily long levels of inactivity and no idle timeout should be applied.
        /// See CD2h - https://sdk.ably.com/builds/ably/specification/main/features/#CD2h.
        /// </summary>
        [JsonProperty("maxIdleInterval")]
        public TimeSpan? MaxIdleInterval { get; set; }

        /// <summary>
        /// Max inbound rate.
        /// </summary>
        [JsonProperty("maxInboundRate")]
        public long MaxInboundRate { get; set; }

        /// <summary>
        /// Max message size.
        /// </summary>
        [JsonProperty("maxMessageSize")]
        public long MaxMessageSize { get; set; }

        /// <summary>
        /// The length of time that must pass before resources for tombstoned objects and map entries
        /// may be released (CD2i). Received on the wire as an integer number of milliseconds. Parsed and
        /// stored only; this library has no object support that consumes it.
        /// </summary>
        [JsonProperty("objectsGCGracePeriod")]
        public TimeSpan? ObjectsGCGracePeriod { get; set; }

        /// <summary>
        /// An identifier for the site that the client has connected to (CD2j).
        /// </summary>
        [JsonProperty("siteCode")]
        public string SiteCode { get; set; }

        /// <summary>
        /// Server id associated with the current connection.
        /// </summary>
        [JsonProperty("serverId")]
        public string ServerId { get; set; }
    }
}
