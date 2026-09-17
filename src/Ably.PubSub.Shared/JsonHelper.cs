using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IO.Ably.CustomSerialisers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>
    /// Public helper class for serialising and deserialising
    /// json using Ably's specific converters for DateTimeOffset, TimeSpan and Capability.
    /// </summary>
    public static class JsonHelper
    {
        private static JsonSerializerSettings GetJsonSettings()
        {
            var res = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new DateTimeOffsetJsonConverter(),
                    new CapabilityJsonConverter(),
                    new TimeSpanJsonConverter(),
                    new MessageExtrasConverter(),
                },
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                NullValueHandling = NullValueHandling.Ignore,
                MaxDepth = null, // Maintain unlimited depth behavior for backward compatibility
            };
            return res;
        }

        private static JsonSerializer GetSerializer()
        {
            var settings = GetJsonSettings();

            return JsonSerializer.CreateDefault(settings);
        }

        private static JsonSerializerSettings _settings;

        internal static JsonSerializerSettings Settings => _settings ?? (_settings = GetJsonSettings());

        /// <summary>
        /// Serialise an object to json.
        /// </summary>
        /// <param name="obj">Object to be serialised.</param>
        /// <returns>json string.</returns>
        public static string Serialize(object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Cannot serialize null object");
            }

            return SerializeObject(obj, obj.GetType());
        }

        /// <summary>
        /// Deserialise a json string to an object of type T.
        /// </summary>
        /// <typeparam name="T">type of object.</typeparam>
        /// <param name="json">input json string.</param>
        /// <returns>deserialised object of type T.</returns>
        public static T Deserialize<T>(string json)
        {
            return (T)DeserializeObject(json, typeof(T));
        }

        /// <summary>
        /// Deserialise a json string to an object.
        /// </summary>
        /// <param name="json">input json string.</param>
        /// <returns>deserialised object.</returns>
        public static object Deserialize(string json)
        {
            return DeserializeObject(json, null);
        }

        /// <summary>
        /// Convert a JObject to an object of type T.
        /// </summary>
        /// <typeparam name="T">Type of object to deserialise to.</typeparam>
        /// <param name="obj">input JObject.</param>
        /// <returns>object of type T.</returns>
        public static T DeserializeObject<T>(JObject obj)
        {
            return obj.ToObject<T>(GetSerializer());
        }

        private static string SerializeObject(object value, Type type)
        {
            var jsonSerializer = GetSerializer();
            StringWriter stringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (JsonTextWriter jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                jsonTextWriter.Formatting = jsonSerializer.Formatting;
                jsonSerializer.Serialize(jsonTextWriter, value, type);
            }

            return stringWriter.ToString();
        }

        private static object DeserializeObject(string value, Type type)
        {
            JsonSerializer jsonSerializer = GetSerializer();
            using (JsonTextReader jsonTextReader = new JsonTextReader(new StringReader(value)))
            {
                return jsonSerializer.Deserialize(jsonTextReader, type);
            }
        }
    }
}
