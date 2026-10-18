using System;
using System.Collections.Generic;
using System.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>request.url</c> and <c>conn.url</c>: a parsed view of the URI an HTTP request or a
    /// WebSocket connection attempt was made against. Query parameters are a map because that is how
    /// the specs read them (<c>request.url.query_params["limit"]</c>, <c>conn.url.queryParams</c>);
    /// <see cref="AllQueryParams"/> keeps a repeated key for the rare spec that cares.
    /// </summary>
    public sealed class RecordedUrl
    {
        public RecordedUrl(Uri uri)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            AllQueryParams = ParseQuery(uri.Query);

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in AllQueryParams)
            {
                map[pair.Key] = pair.Value;
            }

            QueryParams = map;
        }

        public Uri Uri { get; }

        public string Scheme => Uri.Scheme;

        public string Host => Uri.Host;

        public int Port => Uri.Port;

        /// <summary>True for <c>https</c> and <c>wss</c>.</summary>
        public bool Tls => string.Equals(Uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);

        public string Path => Uri.AbsolutePath;

        /// <summary>Query parameters, unescaped; the last value wins for a repeated key.</summary>
        public IReadOnlyDictionary<string, string> QueryParams { get; }

        /// <summary>Every query parameter in order, repeats included.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> AllQueryParams { get; }

        public override string ToString() => Uri.ToString();

        private static List<KeyValuePair<string, string>> ParseQuery(string query)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            foreach (var part in query.TrimStart('?').Split('&').Where(p => p.Length > 0))
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                {
                    result.Add(new KeyValuePair<string, string>(Unescape(part), string.Empty));
                }
                else
                {
                    var name = Unescape(part.Substring(0, separator));
                    var value = Unescape(part.Substring(separator + 1));
                    result.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            return result;
        }

        private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace("+", " "));
    }
}
