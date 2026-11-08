using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub
{
    /// <summary>
    /// Contains the annotation summaries of a <see cref="Message"/> (TM2u, TM8).
    /// </summary>
    public class MessageAnnotations
    {
        /// <summary>
        /// A map of annotation type to the summary for that type (TM8a). The values are deliberately left as loose
        /// JSON (TM2q1, TM8a1) as the shape of a summary depends on the annotation type; use the typed accessors
        /// on <see cref="Summary"/> to interpret an entry (TM7).
        /// </summary>
        [JsonProperty("summary")]
        public IDictionary<string, JToken> Summary { get; set; }

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

            var other = (MessageAnnotations)obj;
            if (ReferenceEquals(Summary, other.Summary))
            {
                return true;
            }

            if (Summary == null || other.Summary == null || Summary.Count != other.Summary.Count)
            {
                return false;
            }

            return Summary.All(pair => other.Summary.TryGetValue(pair.Key, out var value) && JToken.DeepEquals(pair.Value, value));
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Summary != null ? Summary.Count : 0;
        }
    }
}
