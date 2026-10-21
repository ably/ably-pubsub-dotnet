using System.Collections.Generic;
using System.Net;

namespace Ably.PubSub
{
    /// <summary>
    /// Parameters for <see cref="Http.RestAnnotations.GetAsync(string, AnnotationsRequestParams)"/> (RSAN3a).
    /// Every parameter is sent in the querystring (RSAN3b).
    /// Experimental: the annotations API may change.
    /// </summary>
    public class AnnotationsRequestParams
    {
        internal const string LimitParameter = "limit";

        /// <summary>
        /// Initializes a new instance of the <see cref="AnnotationsRequestParams"/> class.
        /// </summary>
        public AnnotationsRequestParams()
        {
            ExtraParameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// The maximum number of annotations returned in a page of the result, up to 1000.
        /// When neither this nor a <c>limit</c> entry in <see cref="ExtraParameters"/> is set, the default of 100 is used.
        /// When set, it takes precedence over a <c>limit</c> entry in <see cref="ExtraParameters"/>.
        /// </summary>
        public int? Limit { get; set; }

        /// <summary>
        /// Any other query parameters, sent in the querystring as given (RSAN3a, RSAN3b).
        /// </summary>
        public Dictionary<string, string> ExtraParameters { get; set; }

        internal void Validate()
        {
            if (Limit.HasValue && (Limit < 0 || Limit > 1000))
            {
                throw new AblyException(new ErrorInfo(
                    "Annotations query limit must be between 0 and 1000",
                    ErrorCodes.InvalidParameterValue,
                    HttpStatusCode.BadRequest));
            }
        }

        // The limit is always stated explicitly, defaulting to 100; the extra parameters follow it.
        internal IEnumerable<KeyValuePair<string, string>> GetParameters()
        {
            var extra = ExtraParameters ?? new Dictionary<string, string>();
            string limit;
            if (Limit.HasValue)
            {
                limit = Limit.Value.ToString();
            }
            else if (!extra.TryGetValue(LimitParameter, out limit))
            {
                limit = Defaults.QueryLimit.ToString();
            }

            var result = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(LimitParameter, limit),
            };
            foreach (var parameter in extra)
            {
                if (parameter.Key != LimitParameter)
                {
                    result.Add(parameter);
                }
            }

            return result;
        }
    }
}
