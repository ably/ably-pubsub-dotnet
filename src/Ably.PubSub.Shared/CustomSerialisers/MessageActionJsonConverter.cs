using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.CustomSerialisers
{
    /// <summary>
    /// Reads and writes <see cref="MessageAction"/> values as their TM5 wire integers.
    /// Wire values which are not defined in <see cref="MessageAction"/> are read as <c>null</c> rather than
    /// being surfaced as an undefined enum value.
    /// </summary>
    internal class MessageActionJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is MessageAction action)
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
            if (value < 0 || value > int.MaxValue || !Enum.IsDefined(typeof(MessageAction), (int)value))
            {
                return null;
            }

            return (MessageAction)(int)value;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(MessageAction) || objectType == typeof(MessageAction?);
        }
    }
}
