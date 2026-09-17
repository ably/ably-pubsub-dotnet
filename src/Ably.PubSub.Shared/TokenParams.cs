using System;
using System.Collections.Generic;
using System.Globalization;

namespace IO.Ably
{
    /// <summary>
    /// A class providing parameters of a token request.
    /// </summary>
    public class TokenParams
    {
        /// <summary>
        /// Requested time to live for the token. If the token request
        /// is successful, the TTL of the returned token will be less
        /// than or equal to this value depending on application settings
        /// and the attributes of the issuing key.
        /// </summary>
        public TimeSpan? Ttl { get; set; }

        /// <summary>
        /// <see cref="Capability"/> of the token. If the token request is successful,
        /// the capability of the returned token will be the intersection of
        /// this capability with the capability of the issuing key.
        /// </summary>
        public Capability Capability { get; set; }

        /// <summary>
        /// ClientId to associate with the current token. The generated token may be to authenticate as this tokenId.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// The timestamp  of this request. If not supplied the timestamp is automatically set to the current UTC time
        /// Timestamps, in conjunction with the nonce, are used to prevent
        /// token requests from being replayed.
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// An opaque nonce string of at least 16 characters to ensure
        /// uniqueness of this request. Any subsequent request using the
        /// same nonce will be rejected.
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// Merges two another instance of TokenParams with the current instance.
        /// </summary>
        /// <param name="otherParams">other instance.</param>
        /// <returns>a new instance of merged token params.</returns>
        public TokenParams Merge(TokenParams otherParams)
        {
            if (otherParams == null)
            {
                return this;
            }

            var result = new TokenParams
            {
                ClientId = ClientId.IsNotEmpty() ? ClientId : otherParams.ClientId,
                Capability = Capability ?? otherParams.Capability,
                Ttl = Ttl ?? otherParams.Ttl,
                Timestamp = Timestamp ?? otherParams.Timestamp,
                Nonce = Nonce ?? otherParams.Nonce,
            };
            return result;
        }

        /// <summary>
        /// Creates a new instance of token params and populates all the current values.
        /// </summary>
        /// <returns>a new instance of token params.</returns>
        public TokenParams Clone()
        {
            var result = new TokenParams
            {
                ClientId = ClientId,
                Nonce = Nonce,
                Ttl = Ttl,
                Timestamp = Timestamp,
            };

            if (Capability != null)
            {
                result.Capability = new Capability(Capability.ToJson());
            }

            return result;
        }

        /// <summary>
        /// Get a new instance of TokenParams and applies the
        /// default Capability and Ttl.
        /// </summary>
        /// <returns>instance of TokenParams.</returns>
        public static TokenParams WithDefaultsApplied()
        {
            var tokenParams = new TokenParams
            {
                Capability = Defaults.DefaultTokenCapability,
                Ttl = Defaults.DefaultTokenTtl,
            };
            return tokenParams;
        }

        /// <summary>
        /// Populates a dictionary of strings and optionally merges with
        /// an existing one. Internal method.
        /// </summary>
        /// <param name="mergeWith">optional, dictionary of strings to merge with.</param>
        /// <returns>returns a merge.</returns>
        public Dictionary<string, string> ToRequestParams(Dictionary<string, string> mergeWith = null)
        {
            var dictionary = new Dictionary<string, string>();
            if (Ttl.HasValue)
            {
                dictionary.Add("ttl", Ttl.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            }

            if (ClientId.IsNotEmpty())
            {
                dictionary.Add("clientId", ClientId);
            }

            if (Nonce.IsNotEmpty())
            {
                dictionary.Add("nonce", Nonce);
            }

            if (Capability != null)
            {
                dictionary.Add("capability", Capability.ToJson());
            }

            if (Timestamp.HasValue)
            {
                dictionary.Add("timestamp", Timestamp.Value.ToUnixTimeInMilliseconds().ToString());
            }

            if (mergeWith != null)
            {
                return dictionary.Merge(mergeWith);
            }

            return dictionary;
        }
    }
}
