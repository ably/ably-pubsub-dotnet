using System;
using System.Collections.Generic;
using System.Net.Http;

namespace IO.Ably
{
    internal class AblyRequest
    {
        private ChannelOptions _channelOptions;

        public AblyRequest(string path, HttpMethod method, Protocol protocol = Defaults.Protocol)
        {
            Url = path;
            QueryParameters = new Dictionary<string, string>();
            Headers = new Dictionary<string, string>();
            PostParameters = new Dictionary<string, string>();
            Method = method;
            Protocol = protocol;
            ChannelOptions = new ChannelOptions();
            RequestBody = new byte[] { };
            ResponseDataType = typeof(object);
        }

        public string Url { get; set; }

        public bool SkipRetry { get; set; }

        public HttpMethod Method { get; private set; }

        public Dictionary<string, string> Headers { get; set; }

        public Dictionary<string, string> QueryParameters { get; set; }

        public Protocol Protocol { get; set; }

        public object PostData { get; set; }

        public Type ResponseDataType { get; set; }

        public ChannelOptions ChannelOptions
        {
            get => _channelOptions;
            set => _channelOptions = value ?? new ChannelOptions();
        }

        public Dictionary<string, string> PostParameters { get; set; }

        public byte[] RequestBody { get; set; }

        public bool SkipAuthentication { get; set; }

        /// <summary>
        /// Tell the HTTP client to not raise an exception when a non 2XX status is returned.
        /// Set to 'true' when 4XX or 5XX HTTP status codes should not cause an exception.
        /// Add to support AblyRest.Request(...).
        /// </summary>
        public bool NoExceptionOnHttpError { get; set; }

        public void AddHeaders(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            if (parameters == null)
            {
                return;
            }

            foreach (var keyValuePair in parameters)
            {
                Headers.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }

        public void AddQueryParameters(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            if (parameters == null)
            {
                return;
            }

            foreach (var keyValuePair in parameters)
            {
                QueryParameters.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }
    }
}
