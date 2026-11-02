using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.CustomSerialisers
{
    /// <summary>
    /// Reads a property of type <typeparamref name="T"/> only when the wire value is a JSON object, and reads
    /// any other wire value (a string, a number, an array, ...) as <c>null</c> instead of failing.
    /// Used for message fields whose wire shape depends on the protocol version: below protocol version 4
    /// the service sends <c>version</c> as a plain string serial, which must not make the whole received
    /// protocol message fail to decode. A <c>null</c> value is then populated by the TM2 decode defaults.
    /// Writing is left to the default serialisation.
    /// </summary>
    /// <typeparam name="T">the type the JSON object is read as.</typeparam>
    internal class ObjectOnlyJsonConverter<T> : JsonConverter
        where T : class
    {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("ObjectOnlyJsonConverter only reads values.");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type != JTokenType.Object)
            {
                return null;
            }

            using (var objectReader = token.CreateReader())
            {
                return serializer.Deserialize<T>(objectReader);
            }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(T);
        }
    }
}
