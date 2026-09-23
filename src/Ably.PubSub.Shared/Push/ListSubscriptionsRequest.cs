using System.Collections.Generic;

namespace IO.Ably.Push
{
    /// <summary>
    /// Encapsulates the List DeviceDetails filter and it prevents invalid states.
    /// </summary>
    public class ListSubscriptionsRequest : PaginatedRequestParams
    {
        private ListSubscriptionsRequest(string channel = null, string clientId = null, string deviceId = null, int? limit = null)
        {
            Channel = channel;
            ClientId = clientId;
            DeviceId = deviceId;
            Limit = limit;
        }

        /// <summary>
        /// Channel filter.
        /// </summary>
        public string Channel { get; set; }

        /// <summary>
        /// ClientId filter.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// DeviceId filter.
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Creates a Request to filter channel subscriptions by deviceId and channel.
        /// </summary>
        /// <param name="channel">Optional channel filter.</param>
        /// <param name="deviceId">DeviceId filter.</param>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>Returns an instance of <see cref="ListSubscriptionsRequest"/> with specified values set.</returns>
        public static ListSubscriptionsRequest WithDeviceId(string channel = null, string deviceId = null, int? limit = null) =>
            new ListSubscriptionsRequest(channel: channel, deviceId: deviceId, limit: limit);

        /// <summary>
        /// Creates a Request to filter channel subscriptions by clientId and channel.
        /// </summary>
        /// <param name="channel">Optional channel filter.</param>
        /// <param name="clientId">The clientId used to filter devices.</param>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>Returns an instance of <see cref="ListSubscriptionsRequest"/> with specified values set.</returns>
        public static ListSubscriptionsRequest WithClientId(string channel = null, string clientId = null, int? limit = null) =>
            new ListSubscriptionsRequest(channel: channel, clientId: clientId, limit: limit);

        /// <summary>
        /// Empty filter.
        /// </summary>
        /// <param name="limit">The number of results to return. Default is 100 and Max is 1000.</param>
        /// <returns>Returns an instance of <see cref="ListSubscriptionsRequest"/> with specified values set.</returns>
        public new static ListSubscriptionsRequest Empty(int? limit = null) => new ListSubscriptionsRequest(limit: limit);

        internal Dictionary<string, string> ToQueryParams()
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>();
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

            if (Channel.IsNotEmpty())
            {
                queryParams.Add("channel", Channel);
            }

            return queryParams.Merge(ExtraParameters);
        }
    }
}
