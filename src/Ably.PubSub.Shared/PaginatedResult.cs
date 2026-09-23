using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IO.Ably
{
    /// <summary>
    /// Wraps any Ably HTTP response that supports paging and provides methods to iterate through the pages
    /// using <see cref="FirstAsync()"/>, <see cref="NextAsync()"/>, <see cref="HasNext"/> and <see cref="IsLast"/>.
    /// All items in the HTTP response are available in the IEnumerable <see cref="Items"/>
    /// Paging information is provided by Ably in the LINK HTTP headers.
    /// </summary>
    /// <typeparam name="T">Type of items contained in the result.</typeparam>
    public class PaginatedResult<T>
        where T : class
    {
        internal AblyResponse Response { get; set; }

        /// <summary>
        /// Limit of how many items should be returned.
        /// </summary>
        protected int Limit { get; set; }

        /// <summary>
        /// Executes the next request.
        /// </summary>
        protected Func<PaginatedRequestParams, Task<PaginatedResult<T>>> ExecuteDataQueryFunc { get; }

        /// <summary>
        /// List that holds the actual items returned from the Ably Api.
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// The <see cref="PaginatedRequestParams"/> for the Next query.
        /// </summary>
        public PaginatedRequestParams NextQueryParams { get; protected set; }

        /// <summary>
        /// The <see cref="PaginatedRequestParams"/> for the First query.
        /// </summary>
        public PaginatedRequestParams FirstQueryParams { get; protected set; }

        /// <summary>
        /// The <see cref="PaginatedRequestParams"/> for the Current page.
        /// </summary>
        public PaginatedRequestParams CurrentQueryParams { get; protected set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaginatedResult{T}"/> class.
        /// </summary>
        protected PaginatedResult()
        {
        }

        internal PaginatedResult(AblyResponse response, int limit, Func<PaginatedRequestParams, Task<PaginatedResult<T>>> executeDataQueryFunc)
        {
            Response = response;
            Limit = limit;
            ExecuteDataQueryFunc = executeDataQueryFunc;
            if (response.Headers != null)
            {
                CurrentQueryParams = PaginatedRequestParams.GetLinkQuery(response.Headers, DataRequestLinkType.Current);
                NextQueryParams = PaginatedRequestParams.GetLinkQuery(response.Headers, DataRequestLinkType.Next);
                FirstQueryParams = PaginatedRequestParams.GetLinkQuery(response.Headers, DataRequestLinkType.First);
            }
        }

        /// <summary>
        /// Gets a value indicating whether there are further pages.
        /// </summary>
        public bool HasNext => NextQueryParams != null && NextQueryParams.IsEmpty == false;

        /// <summary>
        /// Gets a value indicating whether the current page is the last one available.
        /// </summary>
        public bool IsLast => HasNext == false;

        /// <summary>
        /// Calls the api with the <see cref="NextQueryParams"/> and returns the next page of result.
        /// </summary>
        /// <returns>returns the next page of results.</returns>
        public Task<PaginatedResult<T>> NextAsync()
        {
            if (HasNext && ExecuteDataQueryFunc != null)
            {
                return ExecuteDataQueryFunc(NextQueryParams);
            }

            return Task.FromResult(new PaginatedResult<T>());
        }

        /// <summary>
        /// Sync version of <see cref="NextAsync()"/>.
        /// Prefer the async version of the method where possible.
        /// </summary>
        /// <returns>returns the next page of results.</returns>
        public PaginatedResult<T> Next()
        {
            return AsyncHelper.RunSync(NextAsync);
        }
    }
}
