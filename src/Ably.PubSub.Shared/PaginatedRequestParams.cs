using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace IO.Ably
{
    /// <summary>
    ///     Data request query used for querying stats and history
    ///     It makes it easier to pass parameters to the ably service by encapsulating the query string parameters passed.
    /// </summary>
    public class PaginatedRequestParams
    {
        /// <summary>
        /// Instance of empty paginated request params.
        /// </summary>
        public static readonly PaginatedRequestParams Empty = new PaginatedRequestParams();

        /// <summary>
        /// Initializes a new instance of the <see cref="PaginatedRequestParams"/> class.
        /// </summary>
        public PaginatedRequestParams()
        {
            ExtraParameters = new Dictionary<string, string>();
            Direction = QueryDirection.Backwards;
        }

        /// <summary>
        /// HttpMethod that will be used for the query.
        /// </summary>
        public HttpMethod HttpMethod { get; set; }

        /// <summary>
        /// Additional headers passed.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Body of the query.
        /// </summary>
        public object Body { get; set; }

        /// <summary>
        /// The path component of the resource URI.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        ///     Start of the query interval as UTC Date.
        ///     Default: null.
        /// </summary>
        public DateTimeOffset? Start { get; set; }

        /// <summary>
        ///     End of the query interval as UTC Date
        ///     Default: null.
        /// </summary>
        public DateTimeOffset? End { get; set; }

        /// <summary>
        ///     The number of the results returned by the server. If there are more result the NextQuery on the PaginatedResource
        ///     will be populated
        ///     Default: 100.
        /// </summary>
        public int? Limit { get; set; }

        /// <summary>
        ///     Query directions. It determines the order in which results are returned. <see cref="QueryDirection" />.
        /// </summary>
        public QueryDirection Direction { get; set; }

        /// <summary>
        ///     Used mainly when parsing query strings to hold extra parameters that need to be passed back to the service.
        /// </summary>
        public Dictionary<string, string> ExtraParameters { get; set; }

        /// <summary>
        ///     If the datasource was created by parsing a query string it can be accessed from here.
        ///     It is mainly used for debugging purposes of Current and NextQueries of PaginatedResources.
        /// </summary>
        public string QueryString { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether <see cref="QueryString" /> is empty (or null).
        /// </summary>
        public bool IsEmpty => QueryString.IsEmpty();

        /// <summary>
        /// Implements PaginatedRequestParams specific equals.
        /// </summary>
        /// <param name="other">the other object we are comparing to.</param>
        /// <returns>true or false.</returns>
        protected bool Equals(PaginatedRequestParams other)
        {
            return Start.Equals(other.Start)
                   && End.Equals(other.End)
                   && Limit == other.Limit
                   && Direction == other.Direction
                   && ExtraParameters.SequenceEqual(other.ExtraParameters);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Start.GetHashCode();
                hashCode = (hashCode * 397) ^ End.GetHashCode();
                hashCode = (hashCode * 397) ^ Limit.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Direction;
                hashCode = (hashCode * 397) ^ (ExtraParameters != null ? ExtraParameters.GetHashCode() : 0);
                return hashCode;
            }
        }

        internal void Validate()
        {
            if (Limit.HasValue && (Limit < 0 || Limit > 1000))
            {
                throw new AblyException("History query limit must be between 0 and 1000");
            }

            if (Start.HasValue && Start.Value < DateExtensions.Epoch)
            {
                throw new AblyException("Start only supports dates after 1 January 1970");
            }

            if (End.HasValue && End.Value < DateExtensions.Epoch)
            {
                throw new AblyException("End only supports dates after 1 January 1970");
            }

            if (Start.HasValue && End.HasValue && End.Value < Start.Value)
            {
                throw new AblyException("End date should be after Start date");
            }
        }

        internal virtual IEnumerable<KeyValuePair<string, string>> GetParameters()
        {
            var result = new List<KeyValuePair<string, string>>();
            if (Start.HasValue)
            {
                result.Add(new KeyValuePair<string, string>("start", Start.Value.ToUnixTimeInMilliseconds().ToString()));
            }

            if (End.HasValue)
            {
                result.Add(new KeyValuePair<string, string>("end", End.Value.ToUnixTimeInMilliseconds().ToString()));
            }

            result.Add(new KeyValuePair<string, string>("direction", Direction.ToString().ToLower()));
            if (Limit.HasValue)
            {
                result.Add(new KeyValuePair<string, string>("limit", Limit.Value.ToString()));
            }
            else
            {
                result.Add(new KeyValuePair<string, string>("limit", "100"));
            }

            result.AddRange(ExtraParameters);
            return result;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((PaginatedRequestParams)obj);
        }

        internal static PaginatedRequestParams Parse(string querystring)
        {
            var query = new PaginatedRequestParams { QueryString = querystring };
            var queryParameters = HttpUtility.ParseQueryString(querystring);
            foreach (var key in queryParameters.AllKeys)
            {
                switch (key.ToLower())
                {
                    case "start":
                        query.Start = ToDateTime(queryParameters[key]);
                        break;
                    case "end":
                        query.End = ToDateTime(queryParameters[key]);
                        break;
                    case "direction":
                        if (Enum.TryParse(queryParameters[key], true, out QueryDirection direction))
                        {
                            query.Direction = direction;
                        }

                        break;
                    case "limit":
                        if (int.TryParse(queryParameters[key], out var limit))
                        {
                            query.Limit = limit;
                        }

                        break;
                    default:
                        query.ExtraParameters.Add(key, queryParameters[key]);
                        break;
                }
            }

            return query;
        }

        internal static PaginatedRequestParams GetLinkQuery(HttpHeaders headers, string link)
        {
            const string linkPattern = "\\s*<(.*)>;\\s*rel=\"(.*)\"";

            // There can be multiple headers for Link (first, next, current)
            if (headers.TryGetValues("Link", out var linkHeaders))
            {
                foreach (var linkHeaderValue in linkHeaders)
                {
                    // On Xamarin/Mono the the values are concatenated to appear as one header with the values as a comma separated string.
                    // This is valid per rfc2616 (https://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2),
                    // but is inconsistent across various .NET runtimes, hence a split is required here.
                    foreach (var headerValue in linkHeaderValue.Split(','))
                    {
                        var match = Regex.Match(headerValue, linkPattern);
                        if (match.Success && match.Groups[2].Value.Equals(link, StringComparison.OrdinalIgnoreCase))
                        {
                            var url = match.Groups[1].Value;
                            var queryString = url.Split('?')[1];
                            return Parse(queryString);
                        }
                    }
                }
            }

            return Empty;
        }

        private static DateTimeOffset? ToDateTime(object value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                var miliseconds = (long)Convert.ChangeType(value, typeof(long));
                return miliseconds.FromUnixTimeInMilliseconds();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    /// <summary>
    ///     Data request query used for querying history.
    ///     Functionally identical to <see cref="PaginatedRequestParams"/> and present for backwards compatibility with 0.8 release.
    /// </summary>
    [Obsolete("HistoryRequestParams may be removed in future versions, please use PaginatedRequestParams instead.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "No need.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1502:Element should not be on a single line", Justification = "No need.")]
    public class HistoryRequestParams
        : PaginatedRequestParams
    { }
}
