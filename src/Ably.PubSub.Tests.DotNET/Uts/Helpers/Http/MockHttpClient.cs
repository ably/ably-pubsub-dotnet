using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The UTS specs' <c>MockHttpClient</c> (<c>uts/rest/unit/helpers/mock_http.md</c>).
    ///
    /// Installed through the public <c>ClientOptions.HttpClient</c> seam, so it sits *below* the SDK's
    /// host selection: by the time a request reaches here, <c>AblyHttpRequester</c> has already decided
    /// which host this attempt goes to, which is what the fallback specs need to observe.
    ///
    /// Answering precedence for each attempt is: a registered <see cref="AwaitRequest"/> waiter, then a
    /// queued response, then <see cref="OnRequest"/>. An attempt nothing answers fails loudly rather than
    /// hanging to the test's timeout.
    /// </summary>
    public sealed class MockHttpClient : HttpMessageHandler
    {
        /// <summary>
        /// The deadline every wait here uses unless a test asks for another.
        ///
        /// Fifteen seconds, not five. Nothing in the unit tier legitimately takes anywhere near
        /// that - these are mocks - so the figure is not an expectation, it is headroom. The tier
        /// is ~900 tests running in parallel in Release, and a five-second deadline was measured
        /// losing to scheduling under that load about once every three full runs, on a test with
        /// nothing wrong with it. A genuinely stuck test still fails, ten seconds later.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How long the mock waits before declaring that nothing answered a request.
        ///
        /// Deliberately shorter than <see cref="DefaultTimeout"/>, and shorter than the SDK's own
        /// ten-second <c>HttpRequestTimeout</c>: the point of the message is to beat both and say
        /// what is actually wrong. Raised past the client's timeout, the client's own error wins
        /// and the explanation never appears - measured, when DefaultTimeout went to fifteen.
        /// </summary>
        public static readonly TimeSpan UnansweredRequestTimeout = TimeSpan.FromSeconds(5);

        private readonly object _lock = new object();
        private readonly List<PendingHttpConnection> _connectionAttempts = new List<PendingHttpConnection>();
        private readonly List<PendingHttpRequest> _capturedRequests = new List<PendingHttpRequest>();
        private readonly Queue<TaskCompletionSource<PendingHttpConnection>> _connectionWaiters =
            new Queue<TaskCompletionSource<PendingHttpConnection>>();

        private readonly Queue<TaskCompletionSource<PendingHttpRequest>> _requestWaiters =
            new Queue<TaskCompletionSource<PendingHttpRequest>>();

        private readonly List<QueuedResponse> _queued = new List<QueuedResponse>();
        private readonly List<Exception> _handlerErrors = new List<Exception>();

        // Attempts that reached the mock with nothing to answer them. In .NET an SDK call starts its
        // request eagerly, so a test that calls AwaitRequest() *after* triggering the call would
        // otherwise register its waiter behind a request already sitting here. Draining these first
        // closes that window.
        private readonly Queue<PendingHttpConnection> _unansweredConnections = new Queue<PendingHttpConnection>();
        private readonly Queue<PendingHttpRequest> _unansweredRequests = new Queue<PendingHttpRequest>();

        public MockHttpClient(
            Action<PendingHttpConnection> onConnectionAttempt = null,
            Action<PendingHttpRequest> onRequest = null)
        {
            OnConnectionAttempt = onConnectionAttempt;
            OnRequest = onRequest;
        }

        /// <summary>Reassignable, as the specs expect.</summary>
        public Action<PendingHttpConnection> OnConnectionAttempt { get; set; }

        /// <summary>Reassignable, as the specs expect.</summary>
        public Action<PendingHttpRequest> OnRequest { get; set; }

        /// <summary>The clock the mock stamps attempts with. Point it at a TestClock to control timestamps.</summary>
        public Func<DateTimeOffset> NowFunc { get; set; } = () => DateTimeOffset.UtcNow;

        public IReadOnlyList<PendingHttpConnection> ConnectionAttempts
        {
            get
            {
                lock (_lock)
                {
                    return _connectionAttempts.ToList();
                }
            }
        }

        public IReadOnlyList<PendingHttpRequest> CapturedRequests
        {
            get
            {
                lock (_lock)
                {
                    return _capturedRequests.ToList();
                }
            }
        }

        /// <summary>
        /// Whatever a test's handler threw, in order. The exception is also rethrown into the SDK so that
        /// the attempt fails rather than hanging to the deadline — <c>AblyHttpRequester</c> wraps it and
        /// preserves the message, so an assertion that fails inside a handler still names itself. Read
        /// this when the wrapping gets in the way and you want the original.
        /// </summary>
        public IReadOnlyList<Exception> HandlerErrors
        {
            get
            {
                lock (_lock)
                {
                    return _handlerErrors.ToList();
                }
            }
        }

        /// <summary>An <see cref="HttpClient"/> over this handler, for <c>ClientOptions.HttpClient</c>.</summary>
        public HttpClient AsHttpClient() => new HttpClient(this, disposeHandler: false);

        public Task<PendingHttpConnection> AwaitConnectionAttempt(TimeSpan? timeout = null)
            => AwaitNext(_connectionWaiters, _unansweredConnections, timeout, "connection attempt");

        public Task<PendingHttpRequest> AwaitRequest(TimeSpan? timeout = null)
            => AwaitNext(_requestWaiters, _unansweredRequests, timeout, "request");

        /// <summary>Clears the timeline, the waiters and the queue. Handlers are left alone.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _connectionAttempts.Clear();
                _capturedRequests.Clear();
                _connectionWaiters.Clear();
                _requestWaiters.Clear();
                _queued.Clear();
                _handlerErrors.Clear();
                _unansweredConnections.Clear();
                _unansweredRequests.Clear();
            }
        }

        public void QueueResponse(int status, object body = null, IDictionary<string, string> headers = null)
            => Enqueue(new QueuedResponse(_ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers))));

        public void QueueResponses(int count, int status, object body = null, IDictionary<string, string> headers = null)
        {
            for (var i = 0; i < count; i++)
            {
                QueueResponse(status, body, headers);
            }
        }

        public void QueueTimeout() => Enqueue(new QueuedResponse(_ => MockHttpOutcome.Timeout()));

        public void QueueDelayedResponse(
            TimeSpan delay,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => Enqueue(new QueuedResponse(_ =>
                MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers), delay)));

        public void QueueResponseForHost(
            string host,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => Enqueue(new QueuedResponse(
                _ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)),
                request => string.Equals(request.Url.Host, host, StringComparison.OrdinalIgnoreCase)));

        public void QueueResponseForUrl(
            string pathContains,
            int status,
            object body = null,
            IDictionary<string, string> headers = null)
            => Enqueue(new QueuedResponse(
                _ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)),
                request => request.Url.ToString().Contains(pathContains)));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var connection = RaiseConnectionAttempt(request);
            var connectionFailure = await WaitForAnswer(
                connection.Answer,
                $"connection to {connection}",
                cancellationToken).ConfigureAwait(false);

            if (connectionFailure != null)
            {
                // A failed connection records no request, per the spec.
                throw connectionFailure.Failure;
            }

            var pending = await RaiseRequest(request).ConfigureAwait(false);
            var outcome = await WaitForAnswer(pending.Answer, $"request {pending}", cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Delay > TimeSpan.Zero)
            {
                // A deliberate delay the spec asked for. The client's own timeout cancels the token, so a
                // delay longer than HttpRequestTimeout surfaces as the timeout it is meant to provoke.
                await Task.Delay(outcome.Delay, cancellationToken).ConfigureAwait(false);
            }

            if (outcome.Failure != null)
            {
                throw outcome.Failure;
            }

            return outcome.Response;
        }

        private static IReadOnlyDictionary<string, string> CollectHeaders(HttpRequestMessage request)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            return headers;
        }

        private Task<T> AwaitNext<T>(
            Queue<TaskCompletionSource<T>> waiters,
            Queue<T> unanswered,
            TimeSpan? timeout,
            string what)
            where T : class
        {
            TaskCompletionSource<T> waiter;
            lock (_lock)
            {
                if (unanswered.Count > 0)
                {
                    return Task.FromResult(unanswered.Dequeue());
                }

                // Register synchronously so that a test can set up the wait for the *next* event before
                // answering the current one — the sequencing the mock_websocket spec calls out explicitly.
                waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Enqueue(waiter);
            }

            return WithTimeout(waiter.Task, timeout ?? DefaultTimeout, $"Timed out waiting for a {what}.");
        }

        private async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string message)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task)
            {
                throw new TimeoutException($"{message} ({Describe()})");
            }

            return await task.ConfigureAwait(false);
        }

        private string Describe()
        {
            lock (_lock)
            {
                return $"{_connectionAttempts.Count} connection attempt(s), {_capturedRequests.Count} request(s)";
            }
        }

        private PendingHttpConnection RaiseConnectionAttempt(HttpRequestMessage request)
        {
            var uri = request.RequestUri;
            var connection = new PendingHttpConnection(
                uri.Host,
                uri.Port,
                string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase),
                NowFunc());

            TaskCompletionSource<PendingHttpConnection> waiter = null;
            lock (_lock)
            {
                _connectionAttempts.Add(connection);
                if (_connectionWaiters.Count > 0)
                {
                    waiter = _connectionWaiters.Dequeue();
                }
            }

            if (waiter != null)
            {
                waiter.TrySetResult(connection);
                return connection;
            }

            var handler = OnConnectionAttempt;
            if (handler == null)
            {
                // No handler and no waiter: the common case where a spec only cares about the request.
                connection.RespondWithSuccess();
                return connection;
            }

            Invoke(() => handler(connection));
            if (!connection.Answered)
            {
                // A handler that deliberately withholds a response. Park the attempt so a later
                // AwaitConnectionAttempt() can claim it - or hand it straight over if a waiter
                // registered while the handler was running, which would have found the queue empty
                // and parked itself.
                TaskCompletionSource<PendingHttpConnection> lateWaiter = null;
                lock (_lock)
                {
                    if (_connectionWaiters.Count > 0)
                    {
                        lateWaiter = _connectionWaiters.Dequeue();
                    }
                    else
                    {
                        _unansweredConnections.Enqueue(connection);
                    }
                }

                lateWaiter?.TrySetResult(connection);
            }

            return connection;
        }

        private async Task<PendingHttpRequest> RaiseRequest(HttpRequestMessage request)
        {
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            var pending = new PendingHttpRequest(request, body, CollectHeaders(request), NowFunc());

            TaskCompletionSource<PendingHttpRequest> waiter = null;
            QueuedResponse queued = null;
            lock (_lock)
            {
                _capturedRequests.Add(pending);
                if (_requestWaiters.Count > 0)
                {
                    waiter = _requestWaiters.Dequeue();
                }
                else
                {
                    queued = _queued.FirstOrDefault(q => q.Matches(pending));
                    if (queued != null)
                    {
                        _queued.Remove(queued);
                    }
                }
            }

            if (waiter != null)
            {
                waiter.TrySetResult(pending);
                return pending;
            }

            if (queued != null)
            {
                pending.AnswerWith(queued.Build(pending));
                return pending;
            }

            var handler = OnRequest;
            if (handler != null)
            {
                Invoke(() => handler(pending));
            }

            if (!pending.Answered)
            {
                TaskCompletionSource<PendingHttpRequest> lateWaiter = null;
                lock (_lock)
                {
                    // A waiter can have registered while the handler above was still running: it would
                // have found this queue empty and parked itself, so parking the attempt here too
                // would leave the two never meeting. Hand it over if anyone is waiting.
                    if (_requestWaiters.Count > 0)
                    {
                        lateWaiter = _requestWaiters.Dequeue();
                    }
                    else
                    {
                        _unansweredRequests.Enqueue(pending);
                    }
                }

                lateWaiter?.TrySetResult(pending);
            }

            return pending;
        }

        private async Task<MockHttpOutcome> WaitForAnswer(
            Task<MockHttpOutcome> answer,
            string what,
            CancellationToken cancellationToken)
        {
            if (answer.IsCompleted)
            {
                return await answer.ConfigureAwait(false);
            }

            var completed = await Task.WhenAny(
                answer,
                Task.Delay(UnansweredRequestTimeout, cancellationToken)).ConfigureAwait(false);

            if (completed != answer)
            {
                // The client's own HttpRequestTimeout cancels this token, and that is a real
                // timeout rather than an unanswered attempt — so let it surface as one. Reporting
                // "nothing answered" here made every test of a *short* client timeout racy: it
                // passed whenever the test managed to answer inside the timeout window and failed
                // under load when it did not, which is the opposite of what such a test asserts.
                cancellationToken.ThrowIfCancellationRequested();

                throw new InvalidOperationException(
                    $"MockHttpClient: nothing answered the {what}. " +
                    "Configure OnRequest/OnConnectionAttempt, queue a response, or answer the pending " +
                    "object returned by AwaitRequest/AwaitConnectionAttempt.");
            }

            return await answer.ConfigureAwait(false);
        }

        private void Invoke(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _handlerErrors.Add(ex);
                }

                throw;
            }
        }

        private void Enqueue(QueuedResponse response)
        {
            lock (_lock)
            {
                _queued.Add(response);
            }
        }

        private sealed class QueuedResponse
        {
            private readonly Func<PendingHttpRequest, MockHttpOutcome> _build;
            private readonly Func<PendingHttpRequest, bool> _predicate;

            internal QueuedResponse(
                Func<PendingHttpRequest, MockHttpOutcome> build,
                Func<PendingHttpRequest, bool> predicate = null)
            {
                _build = build;
                _predicate = predicate;
            }

            internal bool Matches(PendingHttpRequest request) => _predicate == null || _predicate(request);

            internal MockHttpOutcome Build(PendingHttpRequest request) => _build(request);
        }
    }
}
