using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace IO.Ably
{
    /// <summary>
    /// Authentication options.
    /// </summary>
    public class AuthOptions
    {
        /// <summary>
        /// The callback used to get a new <see cref="IO.Ably.TokenDetails"/> or <see cref="IO.Ably.TokenRequest"/>.
        /// AuthCallback is used by internally by <see cref="IO.Ably.AblyAuth"/>.RequestTokenAsync.
        /// </summary>
        public Func<TokenParams, Task<object>> AuthCallback { get; set; }

        /// <summary>
        /// A URL to query to obtain either a signed token request (<see cref="TokenRequest"/>) or a valid <see cref="TokenDetails"/>
        /// This enables a client to obtain token requests from
        /// another entity, so tokens can be renewed without the
        /// client requiring access to keys.
        /// </summary>
        public Uri AuthUrl { get; set; }

        /// <summary>
        /// Used in conjunction with AuthUrl. Default is GET.
        /// </summary>
        public HttpMethod AuthMethod { get; set; }

        /// <summary>
        /// Used to authenticate the client using an Ably Key.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Used to authenticate the client using a provided token.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Used to authenticate the client using a provider Token Details instance.
        /// </summary>
        public TokenDetails TokenDetails { get; set; }

        /// <summary>
        /// Extra Auth headers.
        /// A set of key value pair headers to be added to any request made to the.
        /// Useful when an application requires these to be added to validate the request or implement the response.
        /// If the authHeaders object contains an authorization key,
        /// then withCredentials will be set on the xhr request.
        /// </summary>
        public Dictionary<string, string> AuthHeaders { get; set; }

        /// <summary>
        /// Extra Auth params.
        /// A set of key value pair params to be added to any request made to the authUrl.
        /// When the authMethod is GET, query params are added to the URL,
        /// whereas when authMethod is POST, the params are sent as URL encoded form data.
        /// Useful when an application require these to be added to validate the request or implement the response.
        /// </summary>
        public Dictionary<string, string> AuthParams { get; set; }

        /// <summary>
        /// Specify whether to query the server time when requesting a new token.
        /// </summary>
        public bool? QueryTime { get; set; }

        /// <summary>
        /// Specify whether to use Token authentication.
        /// </summary>
        public bool? UseTokenAuth { get; set; }

        /// <summary>
        /// Initializes a new instance of the AuthOptions class.
        /// </summary>
        public AuthOptions()
        {
            AuthHeaders = new Dictionary<string, string>();
            AuthParams = new Dictionary<string, string>();
            AuthMethod = HttpMethod.Get;
        }

        /// <summary>
        /// Initialized a new instance of AuthOptions by populating the KeyId and KeyValue properties from the full Key.
        /// </summary>
        /// <param name="key">Full ably key string.</param>
        public AuthOptions(string key)
            : this()
        {
            if (key.IsEmpty())
            {
                throw new AblyException(new ErrorInfo("Auth Key cannot be empty", ErrorCodes.UnableToObtainCredentialsFromGivenParameters));
            }

            if (key.Contains(":"))
            {
                var apiKey = ApiKey.Parse(key);

                Key = apiKey.ToString();
            }
            else
            {
                Token = key;
            }
        }

        /// <summary>
        /// Merges two AuthOptions objects.
        /// </summary>
        /// <param name="defaults">second AuthOptions object.</param>
        /// <returns>merged AuthOptions object.</returns>
        public AuthOptions Merge(AuthOptions defaults)
        {
            if (AuthCallback == null)
            {
                AuthCallback = defaults.AuthCallback;
            }

            if (AuthUrl == null)
            {
                AuthUrl = defaults.AuthUrl;
            }

            if (Token.IsEmpty())
            {
                Token = defaults.Token;
            }

            if (TokenDetails == null)
            {
                TokenDetails = defaults.TokenDetails;
            }

            if (AuthHeaders.Count == 0)
            {
                AuthHeaders = defaults.AuthHeaders;
            }

            if (AuthParams.Count == 0)
            {
                AuthParams = defaults.AuthParams;
            }

            if (Key.IsEmpty())
            {
                Key = defaults.Key;
            }

            if (UseTokenAuth.HasValue == false)
            {
                UseTokenAuth = defaults.UseTokenAuth;
            }

            if (QueryTime.HasValue == false)
            {
                QueryTime = defaults.QueryTime;
            }

            return this;
        }

        internal ApiKey ParseKey()
        {
            return ApiKey.Parse(Key);
        }

        /// <summary>
        /// Creates a new AuthOptions instance using values from an existing instance.
        /// </summary>
        /// <param name="existing">initial AuthOptions object.</param>
        /// <returns>copied AuthOptions object.</returns>
        internal static AuthOptions FromExisting(AuthOptions existing)
        {
            var newOpts = new AuthOptions();
            newOpts.Merge(existing);
            return newOpts;
        }
    }
}
