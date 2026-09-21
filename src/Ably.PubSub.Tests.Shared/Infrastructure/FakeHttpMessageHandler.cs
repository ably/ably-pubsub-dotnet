using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IO.Ably.Tests
{
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Action _sendAsyncAction;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _getResponse;

        public HttpRequestMessage LastRequest { get; private set; }

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public FakeHttpMessageHandler(HttpResponseMessage response, Action sendAsyncAction = null)
        {
            _getResponse = request => response;
            _sendAsyncAction = sendAsyncAction;
        }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> getResponse)
        {
            _getResponse = getResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            NumberOfRequests++;
            Requests.Add(request);
            LastRequest = request;
            var responseTask = Task.FromResult(_getResponse(request));
            _sendAsyncAction?.Invoke();
            return responseTask;
        }

        public int NumberOfRequests { get; private set; }
    }
}
