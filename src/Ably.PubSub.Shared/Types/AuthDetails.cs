using Newtonsoft.Json;

namespace IO.Ably.Types
{
    /// <summary>
    /// AuthDetails is a type used with an AUTH protocol messages to send authentication details.
    /// </summary>
    public class AuthDetails
    {
        /// <summary>
        /// Gets or sets the accessToken.
        /// </summary>
        [JsonProperty("accessToken")]
        public string AccessToken { get; set; }
    }
}
