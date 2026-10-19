using Newtonsoft.Json;

namespace Ably.PubSub
{
    /// <summary>
    /// Contains the result of an update, delete or append message operation (UDR1).
    /// </summary>
    public class UpdateDeleteResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateDeleteResult"/> class with no version serial.
        /// </summary>
        public UpdateDeleteResult()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateDeleteResult"/> class.
        /// </summary>
        /// <param name="versionSerial">the version serial of the operation, or null if the message was superseded.</param>
        public UpdateDeleteResult(string versionSerial)
        {
            VersionSerial = versionSerial;
        }

        /// <summary>
        /// The new version serial of the updated or deleted message (UDR2a). Null if the message was superseded by a
        /// subsequent update before it could be published.
        /// </summary>
        [JsonProperty("versionSerial")]
        public string VersionSerial { get; set; }
    }
}
