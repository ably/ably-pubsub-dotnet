using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IO.Ably.Push;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public abstract class MockHttpRestSpecs : AblySpecs
    {
        internal virtual AblyResponse DefaultResponse { get; } = AblyResponse.EmptyResponse;

        internal AblyRequest LastRequest => Requests.LastOrDefault();

        internal AblyRequest FirstRequest => Requests.FirstOrDefault();

        internal List<AblyRequest> Requests { get; } = new List<AblyRequest>();

        internal AblyRest GetRestClient(Func<AblyRequest, Task<AblyResponse>> handleRequestFunc, ClientOptions options, IMobileDevice mobileDevice = null)
        {
            var client = new AblyRest(options, mobileDevice);
            client.ExecuteHttpRequest = request =>
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

        internal AblyRest GetRestClient(Func<AblyRequest, Task<AblyResponse>> handleRequestFunc = null, Action<ClientOptions> setOptionsAction = null, IMobileDevice mobileDevice = null)
        {
            var options = new ClientOptions(ValidKey) { UseBinaryProtocol = false };
            setOptionsAction?.Invoke(options);

            return GetRestClient(handleRequestFunc, options, mobileDevice);
        }

        protected MockHttpRestSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
