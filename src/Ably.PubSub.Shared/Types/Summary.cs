using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub
{
    /// <summary>
    /// Typed views over entries of <see cref="MessageAnnotations.Summary"/> (TM7).
    /// The summary entries are never converted automatically (TM7a); callers opt in by calling
    /// the accessor which matches the aggregation type of the annotation type being read.
    /// Each accessor returns <c>null</c> when the entry is absent or is not a JSON object.
    /// </summary>
    public static class Summary
    {
        /// <summary>Interprets a <c>distinct.v1</c> summary entry (TM7b1).</summary>
        /// <param name="entry">the summary entry: a JSON object keyed by annotation name.</param>
        /// <returns>the typed view per annotation name, or <c>null</c> if the entry is absent or not an object.</returns>
        public static IDictionary<string, SummaryClientIdList> AsSummaryDistinctV1(JToken entry) => AsByName<SummaryClientIdList>(entry);

        /// <summary>Interprets a <c>unique.v1</c> summary entry (TM7b2).</summary>
        /// <param name="entry">the summary entry: a JSON object keyed by annotation name.</param>
        /// <returns>the typed view per annotation name, or <c>null</c> if the entry is absent or not an object.</returns>
        public static IDictionary<string, SummaryClientIdList> AsSummaryUniqueV1(JToken entry) => AsByName<SummaryClientIdList>(entry);

        /// <summary>Interprets a <c>multiple.v1</c> summary entry (TM7b3).</summary>
        /// <param name="entry">the summary entry: a JSON object keyed by annotation name.</param>
        /// <returns>the typed view per annotation name, or <c>null</c> if the entry is absent or not an object.</returns>
        public static IDictionary<string, SummaryClientIdCounts> AsSummaryMultipleV1(JToken entry) => AsByName<SummaryClientIdCounts>(entry);

        /// <summary>Interprets a <c>flag.v1</c> summary entry (TM7b4).</summary>
        /// <param name="entry">the summary entry.</param>
        /// <returns>the typed view, or <c>null</c> if the entry is absent or not an object.</returns>
        public static SummaryClientIdList AsSummaryFlagV1(JToken entry) => As<SummaryClientIdList>(entry);

        /// <summary>Interprets a <c>total.v1</c> summary entry (TM7b5).</summary>
        /// <param name="entry">the summary entry.</param>
        /// <returns>the typed view, or <c>null</c> if the entry is absent or not an object.</returns>
        public static SummaryTotal AsSummaryTotalV1(JToken entry) => As<SummaryTotal>(entry);

        private static T As<T>(JToken entry)
            where T : class
        {
            return entry is JObject obj ? obj.ToObject<T>() : null;
        }

        private static IDictionary<string, T> AsByName<T>(JToken entry)
            where T : class
        {
            if (!(entry is JObject obj))
            {
                return null;
            }

            var result = new Dictionary<string, T>();
            foreach (var property in obj.Properties())
            {
                var value = As<T>(property.Value);
                if (value == null)
                {
                    return null;
                }

                result[property.Name] = value;
            }

            return result;
        }
    }

    /// <summary>A summary entry which lists the clients that have annotated a message (TM7c).</summary>
    public class SummaryClientIdList
    {
        /// <summary>The total number of distinct clients that have annotated the message.</summary>
        [JsonProperty("total")]
        public int Total { get; set; }

        /// <summary>The ids of the clients that have annotated the message.</summary>
        [JsonProperty("clientIds")]
        public IList<string> ClientIds { get; set; } = new List<string>();

        /// <summary>Whether <see cref="ClientIds"/> is incomplete because the list was clipped by the server.</summary>
        [JsonProperty("clipped")]
        public bool Clipped { get; set; }
    }

    /// <summary>A summary entry which counts the annotations made by each client (TM7d).</summary>
    public class SummaryClientIdCounts
    {
        /// <summary>The sum of the counts from all clients, including unidentified clients.</summary>
        [JsonProperty("total")]
        public int Total { get; set; }

        /// <summary>A map of client id to the count contributed by that client.</summary>
        [JsonProperty("clientIds")]
        public IDictionary<string, int> ClientIds { get; set; } = new Dictionary<string, int>();

        /// <summary>The sum of the counts from clients which did not identify themselves.</summary>
        [JsonProperty("totalUnidentified")]
        public int TotalUnidentified { get; set; }

        /// <summary>Whether <see cref="ClientIds"/> is incomplete because the map was clipped by the server.</summary>
        [JsonProperty("clipped")]
        public bool Clipped { get; set; }

        /// <summary>The total number of distinct client ids that have annotated the message.</summary>
        [JsonProperty("totalClientIds")]
        public int TotalClientIds { get; set; }
    }

    /// <summary>A summary entry which holds a single total (TM7e).</summary>
    public class SummaryTotal
    {
        /// <summary>The total for the annotation type.</summary>
        [JsonProperty("total")]
        public int Total { get; set; }
    }
}
