using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>
    /// A type that represents a page of results from a paginated http query.
    /// The response is accompanied by response details and metadata that
    /// indicates the relative queries available.
    /// </summary>
    public class HttpPaginatedResponse : PaginatedResult<JToken>
    {
        private const string AblyErrorCodeHeader = "X-Ably-Errorcode";

        /// <summary>
        /// Response headers.
        /// </summary>
        public HttpHeaders Headers { get; private set; }

        /// <summary>
        /// Response Status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; private set; }

        /// <summary>
        /// Is the response successful.
        /// </summary>
        public bool Success => (int)StatusCode >= 200 && (int)StatusCode < 300;

        /// <summary>
        /// Error code if any.
        /// </summary>
        public int ErrorCode { get; set; }

        /// <summary>
        /// Error message if any.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Method attached to the Response so we can provide Next and Prev convenience methods.
        /// </summary>
        protected new Func<PaginatedRequestParams, Task<HttpPaginatedResponse>> ExecuteDataQueryFunc { get; }

        /// <summary>
        /// Initialised a new <see cref="HttpPaginatedResponse"/> instance.
        /// </summary>
        public HttpPaginatedResponse()
        {
        }

        internal HttpPaginatedResponse(AblyResponse response, int limit, PaginatedRequestParams requestParams, Func<PaginatedRequestParams, Task<HttpPaginatedResponse>> executeDataQueryFunc)
            : base(response, limit, null)
        {
            ExecuteDataQueryFunc = executeDataQueryFunc;
            StatusCode = Response.StatusCode;
            Headers = response.Headers;

            if (Response.Headers.TryGetValues(AblyErrorCodeHeader, out var errorCodeHeaderValues))
            {
                string errCodeStr = errorCodeHeaderValues.FirstOrDefault();
                if (int.TryParse(errCodeStr, out var errCode))
                {
                    ErrorCode = errCode;
                }
            }

            if (Response.Headers.TryGetValues(AblyErrorCodeHeader, out var errorMessageHeaderValues))
            {
                ErrorMessage = errorMessageHeaderValues.FirstOrDefault();
            }

            ExecuteDataQueryFunc = executeDataQueryFunc;

            if (response.TextResponse.IsNotEmpty())
            {
                var data = JToken.Parse(response.TextResponse);
                if (data is JArray arr)
                {
                    foreach (var token in arr)
                    {
                        Items.Add(token);
                    }
                }
                else
                {
                    Items.Add(data);
                }
            }

            InitializeQuery(CurrentQueryParams, requestParams);
            InitializeQuery(NextQueryParams, requestParams);
        }

        private static void InitializeQuery(PaginatedRequestParams queryParams, PaginatedRequestParams requestParams)
        {
            queryParams.Path = requestParams.Path;
            queryParams.HttpMethod = requestParams.HttpMethod;
            queryParams.Body = requestParams.Body;
            queryParams.Headers = requestParams.Headers;
        }

        /// <summary>
        /// If there is a next result it will make a call to retrieve it. Otherwise it will return an empty response.
        /// </summary>
        /// <returns>returns the next response.</returns>
        public new Task<HttpPaginatedResponse> NextAsync()
        {
            if (HasNext && ExecuteDataQueryFunc != null)
            {
                return ExecuteDataQueryFunc(NextQueryParams);
            }

            return Task.FromResult(new HttpPaginatedResponse());
        }

        /// <summary>
        /// If there is a first result it will make a call to retrieve it. Otherwise it will return an empty response.
        /// </summary>
        /// <returns>returns the first response in the sequence.</returns>
        public Task<HttpPaginatedResponse> FirstAsync()
        {
            if (FirstQueryParams != null && FirstQueryParams.IsEmpty == false && ExecuteDataQueryFunc != null)
            {
                return ExecuteDataQueryFunc(FirstQueryParams);
            }

            return Task.FromResult(new HttpPaginatedResponse());
        }

        /// <summary>
        /// Sync version of NextAsync().
        /// </summary>
        /// <returns>returns the next response.</returns>
        public new HttpPaginatedResponse Next()
        {
            return AsyncHelper.RunSync(NextAsync);
        }

        /// <summary>
        /// Sync version of FirstAsync().
        /// </summary>
        /// <returns>returns the next response.</returns>
        public HttpPaginatedResponse First()
        {
            return AsyncHelper.RunSync(FirstAsync);
        }
    }
}
