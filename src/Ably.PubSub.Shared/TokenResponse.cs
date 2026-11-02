using Newtonsoft.Json;

namespace Ably.PubSub
{
    internal class TokenResponse
    {
        [JsonProperty("access_token")]
        public TokenDetails AccessToken { get; set; }
    }
}
