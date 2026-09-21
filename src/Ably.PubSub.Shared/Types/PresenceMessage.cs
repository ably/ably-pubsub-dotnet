using System;
using Newtonsoft.Json;

namespace IO.Ably
{
    /// <summary>
    /// Presence Action: the event signified by a PresenceMessage.
    /// </summary>
    public enum PresenceAction : byte
    {
        /// <summary>
        /// Absent.
        /// </summary>
        Absent = 0,

        /// <summary>
        /// Present.
        /// </summary>
        Present,

        /// <summary>
        /// Enter.
        /// </summary>
        Enter,

        /// <summary>
        /// Leave.
        /// </summary>
        Leave,

        /// <summary>
        /// Update.
        /// </summary>
        Update,
    }

    /// <summary>
    /// A class representing an individual presence update to be sent or received
    /// via the Ably Realtime service.
    /// </summary>
    public class PresenceMessage : IMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PresenceMessage"/> class.
        /// </summary>
        public PresenceMessage()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PresenceMessage"/> class.
        /// </summary>
        /// <param name="action">presence action.</param>
        /// <param name="clientId">id of client.</param>
        public PresenceMessage(PresenceAction action, string clientId)
            : this(action, clientId, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PresenceMessage"/> class.
        /// </summary>
        /// <param name="action">presence action.</param>
        /// <param name="clientId">id of client.</param>
        /// <param name="data">custom data object passed with the presence message.</param>
        /// <param name="id">ably message id.</param>
        public PresenceMessage(PresenceAction action, string clientId, object data, string id = null)
        {
            Action = action;
            ClientId = clientId;
            Data = data;
            Id = id;
        }

        /// <summary>
        /// Ably message id.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Presence action associated with the presence message.
        /// </summary>
        [JsonProperty("action")]
        public PresenceAction Action { get; set; }

        /// <summary>
        /// Id of the client associate.
        /// </summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        /// <summary>
        /// Id of the current connection.
        /// </summary>
        [JsonProperty("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>The connection key of the publisher of the message. Used for impersonation.</summary>
        [JsonProperty("connectionKey")]
        public string ConnectionKey { get; set; }

        /// <summary>
        /// Custom data object associated with the message.
        /// </summary>
        [JsonProperty("data")]
        public object Data { get; set; }

        /// <summary>
        /// Encoding for the message.
        /// </summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        /// <summary>
        /// Server timestamp for the message.
        /// </summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// Member key which is a combination of ClientId:ConnectionId.
        /// </summary>
        [JsonIgnore]
        public string MemberKey => $"{ClientId}:{ConnectionId}";

        /// <summary>
        /// Clones the current object.
        /// </summary>
        /// <returns>a new Presence message.</returns>
        public PresenceMessage ShallowClone()
        {
            return (PresenceMessage)MemberwiseClone();
        }
    }
}
