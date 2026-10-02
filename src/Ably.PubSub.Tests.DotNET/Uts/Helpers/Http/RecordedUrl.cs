using System;
using System.Collections.Generic;
using System.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>request.url</c>: a parsed view of the URI an attempt was made against.
    /// Query parameters are exposed as a map because that is how the specs read them
    /// (<c>request.url.queryParams["limit"]</c>), with <see cref="AllQueryParams"/> for the rare
    /// spec that cares about a repeated key.
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

        public bool Tls => string.Equals(Uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

        public string Path => Uri.AbsolutePath;

        public IReadOnlyDictionary<string, string> QueryParams { get; }

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
                    result.Add(new KeyValuePair<string, string>(Uri.UnescapeDataString(part), string.Empty));
                }
                else
                {
                    var name = Uri.UnescapeDataString(part.Substring(0, separator));
                    var value = Uri.UnescapeDataString(part.Substring(separator + 1).Replace("+", " "));
                    result.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            return result;
        }
    }
}
