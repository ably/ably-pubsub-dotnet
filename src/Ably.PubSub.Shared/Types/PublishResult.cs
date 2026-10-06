using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>
    /// Contains the result of a publish operation (PBR1).
    /// </summary>
    public class PublishResult
    {
        private IReadOnlyList<string> _serials;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishResult"/> class with no serials.
        /// </summary>
        public PublishResult()
        {
            Serials = Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishResult"/> class.
        /// </summary>
        /// <param name="serials">The serials of the published messages. A null list is treated as empty.</param>
        public PublishResult(IReadOnlyList<string> serials)
        {
            Serials = serials ?? Array.Empty<string>();
        }

        /// <summary>
        /// An array of message serials corresponding 1:1 to the messages that were published.
        /// An individual serial is null if the message was discarded due to a configured
        /// conflation rule (PBR2a). The list itself is never null.
        /// </summary>
        [JsonProperty("serials")]
        public IReadOnlyList<string> Serials
        {
            get => _serials;
            internal set => _serials = value ?? Array.Empty<string>();
        }
    }
}
