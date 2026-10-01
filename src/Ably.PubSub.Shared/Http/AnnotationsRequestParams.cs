using System;

namespace Ably.PubSub
{
    /// <summary>
    /// Parameters for <see cref="Http.RestAnnotations.GetAsync(string, AnnotationsRequestParams)"/> (RSAN3a).
    /// Experimental: the annotations API may change.
    /// </summary>
    public class AnnotationsRequestParams
    {
        /// <summary>
        /// The maximum number of annotations returned in a page of the result, up to 1000.
        /// When not set, the default of 100 is used.
        /// </summary>
        public int? Limit { get; set; }

        internal void Validate()
        {
            if (Limit.HasValue && (Limit < 0 || Limit > 1000))
            {
                throw new ArgumentException("Annotations query limit must be between 0 and 1000", nameof(Limit));
            }
        }
    }
}
