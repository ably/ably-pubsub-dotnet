using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>
    /// Describes the version of a <see cref="Message"/> (TM2s). Every message has a version; for a message that
    /// has never been updated or deleted it is derived from the message itself (TM2s1, TM2s2).
    /// </summary>
    public class MessageVersion
    {
        /// <summary>A unique identifier for the latest version of this message (TM2s1).</summary>
        [JsonProperty("serial")]
        public string Serial { get; set; }

        /// <summary>The time at which the latest version of this message was created (TM2s2).</summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>The id of the client that updated or deleted the message (TM2s3).</summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        /// <summary>The description provided when the message was updated or deleted (TM2s4).</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>The metadata provided when the message was updated or deleted (TM2s5).</summary>
        [JsonProperty("metadata")]
        public IDictionary<string, string> Metadata { get; set; }

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

            var other = (MessageVersion)obj;
            return string.Equals(Serial, other.Serial)
                   && Timestamp.Equals(other.Timestamp)
                   && string.Equals(ClientId, other.ClientId)
                   && string.Equals(Description, other.Description)
                   && MetadataEquals(Metadata, other.Metadata);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Serial != null ? Serial.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                hashCode = (hashCode * 397) ^ (ClientId != null ? ClientId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Description != null ? Description.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Metadata != null ? Metadata.Count : 0);
                return hashCode;
            }
        }

        private static bool MetadataEquals(IDictionary<string, string> left, IDictionary<string, string> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            return left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value));
        }
    }
}
