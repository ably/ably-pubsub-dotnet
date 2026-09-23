using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kvp = System.Collections.Generic.KeyValuePair<string, string>;

namespace IO.Ably
{
#pragma warning disable CS1591 // Only used internally
    /// <summary>
    /// Provides Http helper methods.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "Only used internally")]
    public static class HttpUtility
    {
        public static HttpValueCollection ParseQueryString(string query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if ((query.Length > 0) && (query[0] == '?'))
            {
                query = query.Substring(1);
            }

            return new HttpValueCollection(query, true);
        }
    }

    /// <summary>
    /// Class to a querystring as a list of name / value pairs.
    /// </summary>
    public class HttpValueCollection
    {
        private readonly List<Kvp> _data = new List<Kvp>();

        /// <summary>
        /// All the keys.
        /// </summary>
        public IEnumerable<string> AllKeys
        {
            get { return _data.Select(i => i.Key); }
        }

        /// <summary>
        /// Returns the value for a specific name.
        /// </summary>
        /// <param name="name">name of the requested value.</param>
        /// <returns>the value for the requested name or null if it doesn't exist.</returns>
        public string this[string name]
        {
            get
            {
                string[] items = GetValues(name);
                if (items == null)
                {
                    return null;
                }

                return string.Join(",", items);
            }

            set
            {
                // If the specified key already exists in the collection, setting this property overwrites the existing list of values with the specified value.
                _data.RemoveAll(i => i.Key == name);
                _data.Add(new Kvp(name, value));
            }
        }

        /// <summary>
        /// Returns a list of all the values for a specific name.
        /// </summary>
        /// <param name="name">name of the requested value(s).</param>
        /// <returns>the value(s) as an array.</returns>
        public string[] GetValues(string name)
        {
            string[] res = _data.Where(i => i.Key == name).Select(i => i.Value).ToArray();
            if (res.Length <= 0)
            {
                return null;
            }

            return res;
        }

        /// <summary>
        /// Adds a name / value pair.
        /// </summary>
        /// <param name="name">name of value.</param>
        /// <param name="value">value.</param>
        public void Add(string name, string value)
        {
            _data.Add(new Kvp(name, value));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpValueCollection"/> class.
        /// </summary>
        /// <param name="query">initialise with a query string.</param>
        /// <param name="urlencoded">is the query url encoded.</param>
        public HttpValueCollection(string query, bool urlencoded)
        {
            if (!string.IsNullOrEmpty(query))
            {
                FillFromString(query, urlencoded);
            }
        }

        private void FillFromString(string query, bool urlEncoded)
        {
            if (query is null)
            {
                return;
            }

            // http://stackoverflow.com/a/20284635/126995
            int num = query.Length;
            for (int i = 0; i < num; i++)
            {
                int startIndex = i;
                int num4 = -1;
                while (i < num)
                {
                    char ch = query[i];
                    if (ch == '=')
                    {
                        if (num4 < 0)
                        {
                            num4 = i;
                        }
                    }
                    else if (ch == '&')
                    {
                        break;
                    }

                    i++;
                }

                string str = null;
                string str2;

                if (num4 >= 0)
                {
                    str = query.Substring(startIndex, num4 - startIndex);
                    str2 = query.Substring(num4 + 1, (i - num4) - 1);
                }
                else
                {
                    str2 = query.Substring(startIndex, i - startIndex);
                }

                if (urlEncoded)
                {
                    Add(Uri.UnescapeDataString(str), Uri.UnescapeDataString(str2));
                }
                else
                {
                    Add(str, str2);
                }

                if ((i == (num - 1)) && (query[i] == '&'))
                {
                    Add(null, string.Empty);
                }
            }
        }

        /// <summary>
        /// For internal testing only.
        /// This method does not URL encode and should be considered unsafe for general use.
        /// </summary>
        /// <returns>returns query string value based on the contents of the object.</returns>
        internal string ToQueryString()
        {
            var n = _data.Count;
            if (n == 0)
            {
                return string.Empty;
            }

            var s = new StringBuilder();

            foreach (var k in AllKeys)
            {
                var key = k;
                var keyPrefix = (key != null) ? (key + "=") : string.Empty;

                var values = GetValues(key);
                if (s.Length > 0)
                {
                    s.Append('&');
                }

                if (values == null || values.Length == 0)
                {
                    s.Append(keyPrefix);
                }
                else
                {
                    string item;
                    if (values.Length == 1)
                    {
                        s.Append(keyPrefix);
                        item = values[0];
                        s.Append(item);
                    }
                    else
                    {
                        for (var j = 0; j < values.Length; j++)
                        {
                            if (j > 0)
                            {
                                s.Append('&');
                            }

                            s.Append(keyPrefix);
                            item = values[j];

                            s.Append(item);
                        }
                    }
                }
            }

            return s.ToString();
        }
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
