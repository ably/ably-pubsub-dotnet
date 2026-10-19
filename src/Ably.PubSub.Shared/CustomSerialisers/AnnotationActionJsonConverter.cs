using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.CustomSerialisers
{
    /// <summary>
    /// Reads and writes <see cref="AnnotationAction"/> values as their TAN2b wire integers.
    /// Wire values which are not defined in <see cref="AnnotationAction"/> are read as <c>null</c> rather than
    /// being surfaced as an undefined enum value (RSF1).
    /// </summary>
    internal class AnnotationActionJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is AnnotationAction action)
            {
                writer.WriteValue((int)action);
            }
            else
            {
                writer.WriteNull();
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type != JTokenType.Integer)
            {
                return null;
            }

            var value = (long)token;
            if (value < 0 || value > int.MaxValue || !Enum.IsDefined(typeof(AnnotationAction), (int)value))
            {
                return null;
            }

            return (AnnotationAction)(int)value;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(AnnotationAction) || objectType == typeof(AnnotationAction?);
        }
    }
}
