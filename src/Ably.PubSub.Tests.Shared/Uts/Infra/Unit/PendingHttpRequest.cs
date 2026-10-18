using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>PendingRequest</c> (<c>mock_http.md</c>): everything a spec asserts about an outgoing
    /// request (<c>url</c>, <c>method</c>, <c>headers</c>, <c>body</c>, <c>timestamp</c>), plus the methods
    /// that answer it. The first answer wins; later ones are ignored.
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

        /// <summary>The specs' <c>request.query_params</c> shorthand.</summary>
        public IReadOnlyDictionary<string, string> QueryParams => Url.QueryParams;

        /// <summary>GET, POST, ...</summary>
        public string Method { get; }

        /// <summary>
        /// Request and content headers merged into one case-insensitive map (multiple values joined with
        /// ", "), because a spec reading <c>request.headers["Content-Type"]</c> doesn't know .NET files that
        /// one on the content. Includes the client's default headers (<c>X-Ably-Version</c>, <c>Ably-Agent</c>).
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>The raw body, or null for a request without content.</summary>
        public byte[] Body { get; }

        /// <summary>The body as UTF-8 text: the specs' <c>parse_json(request.body)</c> input.</summary>
        public string BodyText => Body == null ? null : Encoding.UTF8.GetString(Body);

        /// <summary>When the request was made: fake time when a fake clock is installed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>The underlying message, for an assertion the parsed view doesn't cover.</summary>
        public HttpRequestMessage Message { get; }

        public bool Answered => _answer.Task.IsCompleted;

        internal Task<MockHttpOutcome> Answer => _answer.Task;

        /// <summary>
        /// The specs' <c>respond_with(status, body, headers?)</c>. A <c>byte[]</c> body is sent as is, a
        /// string as UTF-8 (default content type <c>application/json</c>), anything else (an anonymous
        /// object, a JObject, a dictionary) as JSON.
        /// </summary>
        public void RespondWith(int status, object body = null, IDictionary<string, string> headers = null)
            => _answer.TrySetResult(MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)));

        /// <summary>
        /// The specs' <c>respond_with_delay(delay, status, body, headers?)</c>. The delay runs on the fake
        /// clock when one is installed (it completes when <c>ADVANCE_TIME</c> passes it), else on the real
        /// clock. A delay longer than the client's <c>HttpRequestTimeout</c> surfaces as that timeout.
        /// </summary>
        public void RespondWithDelay(
            TimeSpan delay,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => _answer.TrySetResult(MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers), delay));

        /// <inheritdoc cref="RespondWithDelay(TimeSpan, int, object, IDictionary{string, string})"/>
        public void RespondWithDelay(
            long delayMilliseconds,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => RespondWithDelay(TimeSpan.FromMilliseconds(delayMilliseconds), status, body, headers);

        /// <summary>
        /// The specs' <c>respond_with_timeout()</c>: the request hangs until the client's
        /// <c>HttpRequestTimeout</c> has passed on the fake clock, then fails with
        /// <see cref="TaskCanceledException"/>, as <c>HttpClient</c> does on its own timeout. Without a fake
        /// clock it fails at once (an infra stand-in, so no unit test waits for a real timeout).
        /// </summary>
        public void RespondWithTimeout() => _answer.TrySetResult(MockHttpOutcome.Timeout());

        internal void AnswerWith(MockHttpOutcome outcome) => _answer.TrySetResult(outcome);

        public override string ToString() => $"{Method} {Url}";
    }
}
