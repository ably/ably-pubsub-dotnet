using System;
using System.Diagnostics;
using Ably.PubSub.CustomSerialisers;
using Ably.PubSub.Shared.CustomSerialisers;
using Ably.PubSub.Types;
using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>
    /// An annotation on a message (TAN1). Experimental: the annotations API may change.
    /// </summary>
    [DebuggerDisplay("{ToString()}")]
    public class Annotation : IMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Annotation"/> class.
        /// </summary>
        [JsonConstructor]
        public Annotation()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Annotation"/> class.
        /// </summary>
        /// <param name="type">the annotation type, which identifies the annotation scheme (TAN2k).</param>
        /// <param name="name">the annotation name, whose meaning depends on the type (TAN2d).</param>
        /// <param name="data">optional annotation payload (TAN2f).</param>
        /// <param name="count">optional count, used by annotation types which aggregate counts (TAN2e).</param>
        /// <param name="clientId">optional clientId of the publisher (TAN2c).</param>
        /// <param name="extras">optional extras (TAN2l).</param>
        public Annotation(string type, string name = null, object data = null, int? count = null, string clientId = null, MessageExtras extras = null)
        {
            Type = type;
            Name = name;
            Data = data;
            Count = count;
            ClientId = clientId;
            Extras = extras;
        }

        /// <summary>A unique id for this annotation (TAN2a).</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// The action of the annotation (TAN2b). <c>null</c> for an annotation which has not been received from
        /// Ably, or when the service sent an action this library does not know about. It is set by the library
        /// when publishing or deleting an annotation.
        /// </summary>
        [JsonProperty("action")]
        [JsonConverter(typeof(AnnotationActionJsonConverter))]
        public AnnotationAction? Action { get; set; }

        /// <summary>The clientId of the publisher of the annotation (TAN2c).</summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        /// <summary>The connection id of the publisher of the annotation (not a TAN2 attribute; populated from the protocol message, as for messages).</summary>
        [JsonProperty("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>The name of the annotation, whose meaning depends on its type (TAN2d).</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>The count of the annotation, used by annotation types which aggregate counts (TAN2e).</summary>
        [JsonProperty("count")]
        public int? Count { get; set; }

        /// <summary>The annotation payload. Supported data types are objects, byte[] and strings (TAN2f).</summary>
        [JsonProperty("data")]
        [JsonConverter(typeof(MessageDataConverter))]
        public object Data { get; set; }

        /// <summary>
        /// The encoding of the annotation data (TAN2g). Encoding and decoding is handled automatically by the
        /// client library, so this should be empty unless a decoding error has occurred.
        /// </summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        /// <summary>Timestamp when the annotation was received by the Ably service (TAN2h).</summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>The serial of this annotation, assigned by the Ably service (TAN2i).</summary>
        [JsonProperty("serial")]
        public string Serial { get; set; }

        /// <summary>The serial of the message this annotation is for (TAN2j).</summary>
        [JsonProperty("messageSerial")]
        public string MessageSerial { get; set; }

        /// <summary>The type of the annotation, which identifies the annotation scheme (TAN2k).</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Extra properties associated with the annotation (TAN2l).</summary>
        [JsonProperty("extras")]
        public MessageExtras Extras { get; set; }

        // Required by the internal IMessage contract, which the encoder pipeline relies on.
        // Annotations carry no connection key, so it is neither exposed nor serialized.
#pragma warning disable SA1600 // Elements should be documented
        string IMessage.ConnectionKey { get; set; }
#pragma warning restore SA1600 // Elements should be documented

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Type: {Type}, Name: {Name}, Action: {Action}, MessageSerial: {MessageSerial}, Serial: {Serial}";
        }
    }
}
