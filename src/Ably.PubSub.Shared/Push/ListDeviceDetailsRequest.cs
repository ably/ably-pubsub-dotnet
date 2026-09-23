using System.Collections.Generic;

namespace IO.Ably.Push
{
    /// <summary>
    /// Encapsulates the List DeviceDetails filter and it prevents invalid states.
    /// </summary>
    public class ListDeviceDetailsRequest : PaginatedRequestParams
    {
        private ListDeviceDetailsRequest(string clientId = null, string deviceId = null, int? limit = null)
        {
            ClientId = clientId;
            DeviceId = deviceId;
            Limit = limit;
        }

        /// <summary>
        /// ClientId filter.
        /// </summary>
        public string ClientId { get; }

        /// <summary>
        /// DeviceId filter.
        /// </summary>
        public string DeviceId { get; }

        internal Dictionary<string, string> ToQueryParams()
        {
            var queryParams = new Dictionary<string, string>();
            if (ClientId.IsNotEmpty())
            {
                queryParams.Add("clientId", ClientId);
            }

            if (DeviceId.IsNotEmpty())
            {
                queryParams.Add("deviceId", DeviceId);
            }

            if (Limit.HasValue)
            {
                queryParams.Add("limit", Limit.Value.ToString());
            }

            return queryParams;
        }

        /// <summary>
        /// Creates a Request to filter devices by deviceId.
        /// </summary>
        /// <param name="deviceId">The deviceId used to filter devices.</param>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>returns an instance of <see cref="ListDeviceDetailsRequest"/> with specified values set.</returns>
        public static ListDeviceDetailsRequest WithDeviceId(string deviceId, int? limit = null) =>
            new ListDeviceDetailsRequest(deviceId: deviceId, limit: limit);

        /// <summary>
        /// Creates a Request to filter devices by clientId.
        /// </summary>
        /// <param name="clientId">The clientId used to filter devices.</param>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>returns an instance of <see cref="ListDeviceDetailsRequest"/> with specified values set.</returns>
        public static ListDeviceDetailsRequest WithClientId(string clientId, int? limit = null) =>
            new ListDeviceDetailsRequest(clientId: clientId, limit: limit);

        /// <summary>
        /// Empty filter.
        /// </summary>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>returns an instance of <see cref="ListDeviceDetailsRequest"/> with specified values set.</returns>
        public new static ListDeviceDetailsRequest Empty(int? limit) => new ListDeviceDetailsRequest(limit: limit);
    }
}
