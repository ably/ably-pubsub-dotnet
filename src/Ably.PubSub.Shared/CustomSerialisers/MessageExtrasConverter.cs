using System;
using IO.Ably.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably.CustomSerialisers
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements should be documented
    public class MessageExtrasConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is MessageExtras messageExtras)
            {
                messageExtras.ToJson().WriteTo(writer);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var extrasToken = JToken.Load(reader);
            return MessageExtras.From(extrasToken);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(MessageExtras);
        }
    }
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
