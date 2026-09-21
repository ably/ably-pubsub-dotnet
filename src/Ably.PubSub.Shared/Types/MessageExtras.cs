using IO.Ably.CustomSerialisers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Types
{
    /// <summary>
    /// Extra properties on the Message.
    /// </summary>
    [JsonConverter(typeof(MessageExtrasConverter))]
    public class MessageExtras
    {
        private static readonly JTokenEqualityComparer _comparer = new JTokenEqualityComparer();
        internal const string DeltaProperty = "delta";

        /// <summary>
        /// Data holds actual extra information associated with message.
        /// </summary>
        [JsonIgnore]
        private JToken Data { get; }

        /// <summary>
        /// Delta extras is part of the Ably delta's functionality.
        /// </summary>
        [JsonIgnore]
        public DeltaExtras Delta { get; }

        /// <summary>
        /// Messages extras is a flexible object that may other properties that are not exposed by the strongly typed implementation.
        /// </summary>
        /// <param name="data">the json object passed to Message extras.</param>
        public MessageExtras(JToken data = null)
        {
            Data = data;
            Delta = ParseDeltaExtras(data);
        }

        private MessageExtras(JToken data, DeltaExtras delta)
        {
            Data = data;
            Delta = delta;
        }

        internal static MessageExtras From(JToken data = null)
        {
            return new MessageExtras(data, ParseDeltaExtras(data));
        }

        private static DeltaExtras ParseDeltaExtras(JToken data)
        {
            DeltaExtras delta = null;
            if (data != null && data is JObject dataObject)
            {
                var deltaProp = dataObject[DeltaProperty];
                if (deltaProp != null && deltaProp is JObject deltaObject)
                {
                    delta = deltaObject.ToObject<DeltaExtras>();
                }
            }

            return delta;
        }

        /// <summary>
        /// Returns the inner message extras json object including any changes made to DeltaExtras.
        /// </summary>
        /// <returns>returns the inner json.</returns>
        public JToken ToJson()
        {
            return Data?.DeepClone();
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (Data != null)
            {
                return _comparer.GetHashCode(Data);
            }

            return 0;
        }

        /// <summary>
        /// Compares two MessageExtras objects by comparing the underlying json data.
        /// </summary>
        /// <param name="other">other Message extras object.</param>
        /// <returns>true or false.</returns>
        private bool Equals(MessageExtras other)
        {
            return JToken.DeepEquals(Data, other.Data);
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

            return Equals((MessageExtras)obj);
        }
    }

    /// <summary>
    /// Extra message properties relating to the delta's functionality.
    /// </summary>
    public class DeltaExtras
    {
        /// <summary>
        /// Format.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// From.
        /// </summary>
        public string From { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeltaExtras"/> class.
        /// </summary>
        /// <param name="from">from parameter.</param>
        /// <param name="format">format parameter.</param>
        public DeltaExtras(string from, string format)
        {
            From = from;
            Format = format;
        }
    }
}
