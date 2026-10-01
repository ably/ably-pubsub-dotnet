using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ably.PubSub.CustomSerialisers;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Shared.CustomSerialisers;
using Ably.PubSub.Types;
using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>A class representing an individual message to be sent or received via the Ably realtime service.</summary>
    [DebuggerDisplay("{ToString()}")]
    public class Message : IMessage
    {
        private static readonly Message DefaultInstance = new Message();

        /// <summary>
        /// Initializes a new instance of the <see cref="Message"/> class.
        /// </summary>
        [JsonConstructor]
        public Message()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Message"/> class.
        /// </summary>
        /// <param name="name">message name.</param>
        /// <param name="data">message data.</param>
        /// <param name="extras">extra message parameters.</param>
        /// <param name="clientId">id of the publisher of this message.</param>
        public Message(string name = null, object data = null, string clientId = null, MessageExtras extras = null)
        {
            Name = name;
            Data = data;
            if (clientId.IsNotEmpty())
            {
                ClientId = clientId;
            }

            Extras = extras;
        }

        /// <summary>A globally unique message id.</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>The id of the publisher of this message.</summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        /// <summary>The connection id of the publisher of the message.</summary>
        [JsonProperty("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>The connection key of the publisher of the message. Used for impersonation.</summary>
        [JsonProperty("connectionKey")]
        public string ConnectionKey { get; set; }

        /// <summary>The event name, if available.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Timestamp when the message was received by the Ably real-time service.</summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>The message payload. Supported data types are objects, byte[] and strings.</summary>
        [JsonProperty("data")]
        [JsonConverter(typeof(MessageDataConverter))]
        public object Data { get; set; }

        /// <summary>
        /// Extra properties associated with the message.
        /// </summary>
        [JsonProperty("extras")]
        public MessageExtras Extras { get; set; }

        /// <summary>
        ///     The encoding for the message data. Encoding and decoding of messages is handled automatically by the client
        ///     library.
        ///     Therefore, the `encoding` attribute should always be nil unless an Ably library decoding error has occurred.
        /// </summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        /// <summary>
        /// The serial of the message, assigned by the Ably service (TM2r).
        /// </summary>
        [JsonProperty("serial")]
        public string Serial { get; set; }

        /// <summary>
        /// The version of the message (TM2s). Populated with defaults derived from the message when decoding messages
        /// received from Ably (TM2s1, TM2s2).
        /// </summary>
        [JsonProperty("version")]
        public MessageVersion Version { get; set; }

        /// <summary>
        /// The action of the message (TM2j). <c>null</c> for a message which has not been received from Ably, or
        /// when the service sent an action this library does not know about. The action is only serialized
        /// when set, so a plain publish does not send one.
        /// </summary>
        [JsonProperty("action")]
        [JsonConverter(typeof(MessageActionJsonConverter))]
        public MessageAction? Action { get; set; }

        /// <summary>
        /// The annotations summary of the message (TM2u). Populated with an empty value when decoding messages
        /// received from Ably.
        /// </summary>
        [JsonProperty("annotations")]
        public MessageAnnotations Annotations { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var result = $"Name: {Name}, Data: {Data}, Encoding: {Encoding}, Timestamp: {Timestamp}";
            if (Id.IsNotEmpty())
            {
                return "Id: " + Id + ", " + result;
            }

            return result;
        }

        /// <summary>
        /// Checks if this is an empty message.
        /// </summary>
        [JsonIgnore]
        public bool IsEmpty => Equals(this, DefaultInstance);

        /// <summary>
        /// Checks equality with another message.
        /// </summary>
        /// <param name="other">other Message object.</param>
        /// <returns>true / false..</returns>
        protected bool Equals(Message other)
        {
            return string.Equals(Id, other.Id)
                   && string.Equals(ClientId, other.ClientId)
                   && string.Equals(ConnectionId, other.ConnectionId)
                   && string.Equals(Name, other.Name)
                   && Timestamp.Equals(other.Timestamp)
                   && Equals(Data, other.Data)
                   && string.Equals(Encoding, other.Encoding)
                   && Equals(Extras, other.Extras)
                   && string.Equals(Serial, other.Serial)
                   && Equals(Version, other.Version)
                   && Action == other.Action
                   && Equals(Annotations, other.Annotations);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Message)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id != null ? Id.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (ClientId != null ? ClientId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (ConnectionId != null ? ConnectionId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                hashCode = (hashCode * 397) ^ (Data != null ? Data.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Encoding != null ? Encoding.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Extras != null ? Extras.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Serial != null ? Serial.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Version != null ? Version.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Action.GetHashCode();
                hashCode = (hashCode * 397) ^ (Annotations != null ? Annotations.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// Builds the message which is sent to Ably to update, delete or append to the message with the serial of
        /// <paramref name="message"/> (RSL15, RTL32). The result is always a fresh object, so the message supplied
        /// by the caller is never mutated (RSL15c, RTL32c).
        /// </summary>
        /// <remarks>
        /// RSL15b and RTL32b ask for "whatever fields were in the user-supplied Message". The fields a publisher may
        /// supply are carried over: id, clientId, name, data, encoding and extras. The remaining fields are assigned by
        /// Ably (connectionId, connectionKey, timestamp, annotations) or are set here (serial, version, action).
        /// </remarks>
        /// <param name="message">the message supplied by the caller. It must have a populated serial.</param>
        /// <param name="operation">optional description of the operation, sent as the version (RSL15b7, RTL32b2).</param>
        /// <param name="action">the action to set: update, delete or append (RSL15b1, RTL32b1).</param>
        /// <returns>A new message to send.</returns>
        /// <exception cref="AblyException">The serial is empty (RSL15a, RTL32a).</exception>
        internal static Message CreateEdit(Message message, MessageOperation operation, MessageAction action)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (message.Serial.IsEmpty())
            {
                throw new AblyException(new ErrorInfo("A message serial is required to update, delete or append to a message", ErrorCodes.InvalidParameterValue, System.Net.HttpStatusCode.BadRequest));
            }

            var edit = new Message
            {
                Id = message.Id,
                ClientId = message.ClientId,
                Name = message.Name,
                Data = message.Data,
                Encoding = message.Encoding,
                Extras = message.Extras,
                Serial = message.Serial,
                Action = action,
            };

            if (operation != null)
            {
                edit.Version = new MessageVersion
                {
                    ClientId = operation.ClientId,
                    Description = operation.Description,
                    Metadata = operation.Metadata == null ? null : new Dictionary<string, string>(operation.Metadata),
                };
            }

            return edit;
        }

        /// <summary>
        /// Decodes the current message data using the default list of encoders.
        /// </summary>
        /// <param name="encoded">encoded message object.</param>
        /// <param name="options">optional channel options. <see cref="ChannelOptions"/>.</param>
        /// <returns>message with decoded payload.</returns>
        public static Message FromEncoded(Message encoded, ChannelOptions options = null)
        {
            return MessageHandler.FromEncoded(encoded, options);
        }

        /// <summary>
        /// Decodes an array of messages. <see cref="FromEncoded(Message, ChannelOptions)"/>.
        /// </summary>
        /// <param name="encoded">array of encoded Messages.</param>
        /// <param name="options">optional channel options. <see cref="ChannelOptions"/>.</param>
        /// <returns>array of decoded messages.</returns>
        public static Message[] FromEncodedArray(Message[] encoded, ChannelOptions options = null)
        {
            return MessageHandler.FromEncodedArray(encoded, options);
        }

        /// <summary>
        /// Decodes the json representation of a Message using the default list of encoders.
        /// </summary>
        /// <param name="messageJson">json representation of a Message.</param>
        /// <param name="options">optional channel options. <see cref="ChannelOptions"/>.</param>
        /// <returns>message with decoded payload.</returns>
        /// <exception cref="AblyException">AblyException if there is an issue decoding the message. The most likely error is invalid json string.</exception>
        public static Message FromEncoded(string messageJson, ChannelOptions options = null)
        {
            try
            {
                var message = JsonHelper.Deserialize<Message>(messageJson);
                return FromEncoded(message, options);
            }
            catch (Exception e)
            {
                DefaultLogger.Error($"Error decoding message: {messageJson}", e);
                throw new AblyException("Error decoding message. Error: " + e.Message, ErrorCodes.InternalError);
            }
        }

        /// <summary>
        /// Decodes a json representation of an array of messages using the default list of encoders.
        /// </summary>
        /// <param name="messagesJson">json representation of an array of messages.</param>
        /// <param name="options">optional channel options. <see cref="ChannelOptions"/>.</param>
        /// <returns>array of decoded messages.</returns>
        /// <exception cref="AblyException">AblyException if there is an issue decoding the message. The most likely error is invalid json string.</exception>
        public static Message[] FromEncodedArray(string messagesJson, ChannelOptions options = null)
        {
            try
            {
                var messages = JsonHelper.Deserialize<List<Message>>(messagesJson).ToArray();
                return FromEncodedArray(messages, options);
            }
            catch (Exception e)
            {
                DefaultLogger.Error($"Error decoding message: {messagesJson}", e);
                throw new AblyException("Error decoding messages. Error: " + e.Message, ErrorCodes.InternalError);
            }
        }
    }
}
