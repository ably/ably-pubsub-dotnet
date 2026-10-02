using System;
using System.Threading.Tasks;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>PendingConnection</c> for the HTTP tier (<c>uts/rest/unit/helpers/mock_http.md</c>).
    ///
    /// .NET has no observable TCP-connect step in front of <see cref="System.Net.Http.HttpMessageHandler"/>,
    /// so the mock raises one connection attempt per request — which is how the spec's own examples read,
    /// and how ably-python models it. A refused connection records no request.
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

        public DateTimeOffset Timestamp { get; }

        /// <summary>True once the test, or a handler, has answered this attempt.</summary>
        public bool Answered => _answer.Task.IsCompleted;

        /// <summary>Completes with null when the connection succeeds, or with the failure to raise.</summary>
        internal Task<MockHttpOutcome> Answer => _answer.Task;

        public void RespondWithSuccess() => _answer.TrySetResult(null);

        public void RespondWithRefused() => _answer.TrySetResult(MockHttpOutcome.ConnectionRefused(Host));

        public void RespondWithTimeout() => _answer.TrySetResult(MockHttpOutcome.Timeout());

        public void RespondWithDnsError() => _answer.TrySetResult(MockHttpOutcome.DnsError(Host));

        public override string ToString() => $"{(Tls ? "https" : "http")}://{Host}:{Port}";
    }
}
