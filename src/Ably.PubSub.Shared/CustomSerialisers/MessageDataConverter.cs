using System;
using Newtonsoft.Json;

namespace IO.Ably.Shared.CustomSerialisers
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements should be documented
    public class MessageDataConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var appDefaultNullValueHandling = serializer.NullValueHandling;
            serializer.NullValueHandling = NullValueHandling.Include;
            serializer.Serialize(writer, value);
            serializer.NullValueHandling = appDefaultNullValueHandling;
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return serializer.Deserialize(reader, objectType);
        }

        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }
}
