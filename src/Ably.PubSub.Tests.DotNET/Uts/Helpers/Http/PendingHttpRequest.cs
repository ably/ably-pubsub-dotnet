using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>PendingRequest</c> (<c>uts/rest/unit/helpers/mock_http.md</c>): everything a spec
    /// asserts about an outgoing request, plus the methods it answers with.
    /// </summary>
    public sealed class PendingHttpRequest
    {
        private readonly TaskCompletionSource<MockHttpOutcome> _answer =
            new TaskCompletionSource<MockHttpOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal PendingHttpRequest(
            HttpRequestMessage message,
            byte[] body,
            IReadOnlyDictionary<string, string> headers,
            DateTimeOffset timestamp)
        {
            Message = message;
            Url = new RecordedUrl(message.RequestUri);
            Method = message.Method.Method;
            Body = body;
            Headers = headers;
            Timestamp = timestamp;
        }

        public RecordedUrl Url { get; }

        /// <summary>The specs' <c>request.path</c> shorthand for <c>request.url.path</c>.</summary>
        public string Path => Url.Path;

        public string Method { get; }

        /// <summary>
        /// Request and content headers merged into one case-insensitive map, because a spec reading
        /// <c>request.headers["Content-Type"]</c> does not know that .NET files that one separately.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] Body { get; }

        public DateTimeOffset Timestamp { get; }

        /// <summary>The underlying message, for the rare assertion the parsed view does not cover.</summary>
        public HttpRequestMessage Message { get; }

        public bool Answered => _answer.Task.IsCompleted;

        /// <summary>The specs' <c>parse_json(request.body)</c> input.</summary>
        public string BodyText => Body == null ? null : Encoding.UTF8.GetString(Body);

        internal Task<MockHttpOutcome> Answer => _answer.Task;

        public void RespondWith(int status, object body = null, IDictionary<string, string> headers = null)
            => _answer.TrySetResult(MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)));

        public void RespondWithDelay(
            TimeSpan delay,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => _answer.TrySetResult(
                MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers), delay));

        public void RespondWithTimeout() => _answer.TrySetResult(MockHttpOutcome.Timeout());

        internal void AnswerWith(MockHttpOutcome outcome) => _answer.TrySetResult(outcome);

        public override string ToString() => $"{Method} {Url}";
    }
}
