using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public abstract class MockHttpRealtimeSpecs : AblySpecs
    {
        internal virtual AblyResponse DefaultResponse { get; } = AblyResponse.EmptyResponse;

        internal AblyRequest LastRequest => Requests.LastOrDefault();

        internal List<AblyRequest> Requests { get; } = new List<AblyRequest>();

        internal AblyRealtime GetRealtimeClient(Func<AblyRequest, Task<AblyResponse>> handleRequestFunc = null, Action<ClientOptions> setOptionsAction = null)
        {
            var options = new ClientOptions(ValidKey) { UseBinaryProtocol = false };
            setOptionsAction?.Invoke(options);

            var client = new AblyRealtime(options);
            client.RestClient.ExecuteHttpRequest = request =>
            {
                Requests.Add(request);
                if (handleRequestFunc != null)
                {
                    return handleRequestFunc(request);
                }

                return DefaultResponse.ToTask();
            };
            return client;
        }

        protected MockHttpRealtimeSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
