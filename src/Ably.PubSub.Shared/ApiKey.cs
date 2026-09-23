using System;
using System.Net;
using System.Text.RegularExpressions;

namespace IO.Ably
{
    /// <summary>
    /// Internal class used to parse ApiKeys. The api key has the following parts {keyName}:{KeySecret}
    /// The app and key parts form the KeyId.
    /// </summary>
    public class ApiKey
    {
        private static readonly Regex KeyRegex = new Regex(@"^[\w-]{2,}\.[\w-]{2,}:[\w-]{2,}$");

        private ApiKey()
        {
        }

        internal string AppId { get; private set; }

        /// <summary>
        /// First part of the key is also called the key name.
        /// </summary>
        public string KeyName { get; private set; }

        /// <summary>
        /// The second part of the key is called the key secret.
        /// </summary>
        public string KeySecret { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{KeyName}:{KeySecret}";
        }

        /// <summary>
        /// Parses a string to produce a key object.
        /// </summary>
        /// <param name="key">a valid ably key.</param>
        /// <exception cref="AblyException">throws an exception when the key string is invalid.</exception>
        /// <returns>ApiKey object representing the parsed key.</returns>
        public static ApiKey Parse(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new AblyException("Ably key was empty. Ably key must be in the following format [AppId].[keyId]:[keyValue]", ErrorCodes.InvalidCredentials, HttpStatusCode.Unauthorized);
            }

            var trimmedKey = key.Trim();

            if (IsValidFormat(trimmedKey))
            {
                var parts = trimmedKey.Trim().Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 2)
                {
                    var keyParts = parts[0].Split(".".ToCharArray());
                    return new ApiKey
                    {
                        AppId = keyParts[0],
                        KeyName = keyParts[0] + "." + keyParts[1],
                        KeySecret = parts[1],
                    };
                }
            }

            throw new AblyException("Invalid Ably key. Ably key must be in the following format [AppId].[keyId]:[keyValue]", ErrorCodes.InvalidCredentials, HttpStatusCode.Unauthorized);
        }

        internal static bool IsValidFormat(string key)
        {
            return KeyRegex.Match(key.Trim()).Success;
        }
    }
}
