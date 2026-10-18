using System;
using System.Threading.Tasks;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>PendingConnection</c> for HTTP (<c>mock_http.md</c>): one connection attempt that the test,
    /// a handler or the mock's default answers. .NET has no observable TCP-connect step in front of an <see
    /// cref="System.Net.Http.HttpMessageHandler"/>, so <see cref="MockHttpClient"/> raises one connection attempt
    /// per request, before the request itself, which is how the spec's examples read.
    /// </summary>
    public sealed class PendingHttpConnection
    {
        private readonly TaskCompletionSource<MockHttpOutcome> _answer =
            new TaskCompletionSource<MockHttpOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal PendingHttpConnection(string host, int port, bool tls, DateTimeOffset timestamp)
        {
            Host = host;
            Port = port;
            Tls = tls;
            Timestamp = timestamp;
        }

        public string Host { get; }

        public int Port { get; }

        public bool Tls { get; }

        /// <summary>When the attempt was made: fake time when a fake clock is installed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>True once the test, a handler or the mock has answered this attempt.</summary>
        public bool Answered => _answer.Task.IsCompleted;

        /// <summary>Completes with null when the connection succeeds, or with the failure to raise.</summary>
        internal Task<MockHttpOutcome> Answer => _answer.Task;

        /// <summary>The connection succeeds; the request phase follows.</summary>
        public void RespondWithSuccess() => _answer.TrySetResult(null);

        /// <summary>TCP refusal: a retryable <c>HttpRequestException</c> (inner <c>WebException</c>, ConnectFailure).</summary>
        public void RespondWithRefused() => _answer.TrySetResult(MockHttpOutcome.ConnectionRefused(Host));

        /// <summary>
        /// The connection attempt hangs until the client's <c>HttpRequestTimeout</c> has passed on the fake
        /// clock, then fails with <see cref="TaskCanceledException"/> (what <c>HttpClient</c> raises on its own
        /// timeout).
        /// </summary>
        public void RespondWithTimeout() => _answer.TrySetResult(MockHttpOutcome.Timeout());

        /// <summary>
        /// DNS failure: a retryable <c>HttpRequestException</c> (inner <c>WebException</c>,
        /// NameResolutionFailure).
        /// </summary>
        public void RespondWithDnsError() => _answer.TrySetResult(MockHttpOutcome.DnsError(Host));

        public override string ToString() => $"{(Tls ? "https" : "http")}://{Host}:{Port}";
    }
}
