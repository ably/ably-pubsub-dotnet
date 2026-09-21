using Newtonsoft.Json;

namespace IO.Ably
{
    internal class TokenResponse
    {
        [JsonProperty("access_token")]
        public TokenDetails AccessToken { get; set; }
    }
}
