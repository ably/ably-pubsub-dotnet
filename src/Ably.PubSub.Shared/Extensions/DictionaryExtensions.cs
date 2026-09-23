using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace IO.Ably
{
    /// <summary>
    /// Extension methods for Dictionary objects.
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Converts a dictionary of strings to a querystring.
        /// </summary>
        /// <param name="params">dictionary to convert.</param>
        /// <returns>returns a url encoded query string.</returns>
        public static string ToQueryString(this Dictionary<string, string> @params)
        {
            if (@params == null || @params.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("&", @params.Select(x => $"{WebUtility.UrlEncode(x.Key)}={WebUtility.UrlEncode(x.Value)}"));
        }

        /// <summary>
        /// Merges two dictionaries of strings without overwriting keys if they exist.
        /// </summary>
        /// <param name="first">first dictionary.</param>
        /// <param name="second">second dictionary.</param>
        /// <returns>returns merged dictionary.</returns>
        public static Dictionary<string, string> Merge(this Dictionary<string, string> first, Dictionary<string, string> second)
        {
            if (second == null)
            {
                return first;
            }

            if (first == null)
            {
                first = new Dictionary<string, string>();
            }

            var result = first.ToDictionary(x => x.Key, x => x.Value);
            foreach (var item in second)
            {
                if (result.Keys.Any(x => string.Equals(x, item.Key, StringComparison.OrdinalIgnoreCase)) ==
                    false)
                {
                    result.Add(item.Key, item.Value);
                }
            }

            return result;
        }
    }
}
