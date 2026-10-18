using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Transport.States.Connection;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The specs' <c>MockHttpClient</c> (<c>uts/rest/unit/helpers/mock_http.md</c>), an
    /// <see cref="HttpMessageHandler"/> installed through the public <c>ClientOptions.HttpClient</c> seam
    /// (the unit base wraps it in <c>new HttpClient(mock, disposeHandler: false)</c>). It sits below the
    /// SDK's host selection and retries: each request that reaches it is one attempt against the host
    /// <c>AblyHttpRequester</c> chose, which is what the fallback specs observe.
    ///
    /// <para><b>Per attempt</b> the mock raises a connection attempt (<see cref="PendingHttpConnection"/>),
    /// then, if that succeeds, a request (<see cref="PendingHttpRequest"/>). Each is answered by, in this
    /// order: a waiter already registered by <see cref="AwaitConnectionAttempt"/> /
    /// <see cref="AwaitRequest"/> (a waiter takes precedence and the handler isn't called); for a request,
    /// a queued response (the legacy <c>queue_*</c> API); the handler (<see cref="OnConnectionAttempt"/> /
    /// <see cref="OnRequest"/>). With no handler a connection succeeds by default. Anything the handler
    /// leaves unanswered is parked and handed to the next <c>Await*</c> call, so a test that starts the
    /// SDK call and only then awaits never races it. An attempt nothing answers fails loudly after
    /// <see cref="UnansweredTimeout"/> of real time instead of hanging to the test's timeout.</para>
    ///
    /// <para><b>Handlers</b> run synchronously on the SDK's calling thread (inside
    /// <c>HttpClient.SendAsync</c>), so they must not block or await. An exception a handler throws is
    /// recorded in <see cref="HandlerErrors"/> and rethrown into the SDK, so the attempt fails rather than
    /// hanging.</para>
    ///
    /// <para><b>Time.</b> With a <see cref="Clock"/> (set by the unit base's factory when fake timers are
    /// enabled) timestamps are fake time, <c>respond_with_delay</c> waits on the fake clock, and the mock
    /// enforces the client's <c>HttpRequestTimeout</c> (<see cref="RequestTimeout"/>, set by the factory)
    /// itself on the fake clock: the factory sets the client's internal <c>HttpClient.Timeout</c> to
    /// infinite so the real clock never cuts a request short. A request still unanswered, a
    /// <c>respond_with_timeout()</c>, or a delay longer than the timeout all fail with
    /// <see cref="TaskCanceledException"/> once the timeout has passed in fake time, which is the
    /// exception <c>HttpClient</c> raises on its own timeout and the SDK treats as retryable. Without a fake
    /// clock the real <c>HttpClient.Timeout</c> applies, and <c>respond_with_timeout()</c> fails at once
    /// (an infra stand-in, so no unit test waits for a real timeout).</para>
    /// </summary>
    public sealed class MockHttpClient : HttpMessageHandler
    {
        /// <summary>
        /// How long (real time) an attempt may wait for an answer before the mock fails it. Shorter than the
        /// SDK's default ten-second request timeout, so the mock's explanation wins.
        /// </summary>
        public static readonly TimeSpan DefaultUnansweredTimeout = TimeSpan.FromSeconds(5);

        private static readonly Uri InternetCheckUri = new Uri(Defaults.InternetCheckUrl);

        private readonly object _lock = new object();
        private readonly List<PendingHttpConnection> _connectionAttempts = new List<PendingHttpConnection>();
        private readonly List<PendingHttpRequest> _capturedRequests = new List<PendingHttpRequest>();
        private readonly List<Exception> _handlerErrors = new List<Exception>();
        private readonly List<QueuedResponse> _queued = new List<QueuedResponse>();
        private readonly List<InFlight> _inFlight = new List<InFlight>();
        private readonly CaptureList<PendingHttpRequest> _unexpected = new CaptureList<PendingHttpRequest>();
        private readonly Queue<TaskCompletionSource<PendingHttpConnection>> _connectionWaiters =
            new Queue<TaskCompletionSource<PendingHttpConnection>>();

        private readonly Queue<TaskCompletionSource<PendingHttpRequest>> _requestWaiters =
            new Queue<TaskCompletionSource<PendingHttpRequest>>();

        private readonly Queue<PendingHttpConnection> _unansweredConnections = new Queue<PendingHttpConnection>();
        private readonly Queue<PendingHttpRequest> _unansweredRequests = new Queue<PendingHttpRequest>();

        public MockHttpClient(
            Action<PendingHttpConnection> onConnectionAttempt = null,
            Action<PendingHttpRequest> onRequest = null)
        {
            OnConnectionAttempt = onConnectionAttempt;
            OnRequest = onRequest;
        }

        /// <summary>The specs' <c>onConnectionAttempt</c>; reassignable mid-test.</summary>
        public Action<PendingHttpConnection> OnConnectionAttempt { get; set; }

        /// <summary>The specs' <c>onRequest</c>; reassignable mid-test.</summary>
        public Action<PendingHttpRequest> OnRequest { get; set; }

        /// <summary>The fake clock, or null for the real one. Set by the unit base's factory.</summary>
        public FakeClock Clock { get; set; }

        /// <summary>
        /// The client's <c>HttpRequestTimeout</c>, enforced on the fake clock. Set by the unit base's factory
        /// from the options (the last client built wins if one mock serves several).
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = Defaults.MaxHttpRequestTimeout;

        /// <summary>See <see cref="DefaultUnansweredTimeout"/>; <c>Timeout.InfiniteTimeSpan</c> turns the guard off.</summary>
        public TimeSpan UnansweredTimeout { get; set; } = DefaultUnansweredTimeout;

        /// <summary>True for the strict default the unit base installs when a test installs none.</summary>
        public bool IsStrictDefault { get; private set; }

        /// <summary>Requests the strict default answered with 500 because no test expected them.</summary>
        public CaptureList<PendingHttpRequest> UnexpectedRequests => _unexpected;

        /// <summary>Every connection attempt, in order.</summary>
        public IReadOnlyList<PendingHttpConnection> ConnectionAttempts => Copy(_connectionAttempts);

        /// <summary>Every request that got past its connection attempt, in order: the legacy <c>mock_http.captured_requests</c>.</summary>
        public IReadOnlyList<PendingHttpRequest> CapturedRequests => Copy(_capturedRequests);

        /// <summary>Whatever a handler threw, in order (also rethrown into the SDK).</summary>
        public IReadOnlyList<Exception> HandlerErrors => Copy(_handlerErrors);

        /// <summary>
        /// True when no request is being processed: every in-flight request is waiting for the test (an
        /// answer) or for the fake clock (a delay or the timeout), and nothing it waits on has completed yet.
        /// An answer the test just gave makes its request busy at once (before the mock's continuation has even
        /// run), and it stays busy until <c>SendAsync</c> has returned the response to the SDK.
        /// <c>process_pending_events()</c> and <c>ADVANCE_TIME</c> drain until this holds, so a response the test
        /// just gave reaches the SDK. What the SDK does with the response after <c>SendAsync</c> returns is only
        /// visible to the drain if it runs inside a workflow command.
        /// </summary>
        public bool IsIdle
        {
            get
            {
                lock (_lock)
                {
                    return _inFlight.All(f => f.IsParked);
                }
            }
        }

        /// <summary>
        /// <see cref="IsIdle"/> together with the number of requests in flight, read under one lock, for the
        /// infra self-tests.
        /// </summary>
        internal bool IsIdleWithInFlight(out int inFlight)
        {
            lock (_lock)
            {
                inFlight = _inFlight.Count;
                return _inFlight.All(f => f.IsParked);
            }
        }

        /// <summary>
        /// The default the unit base installs when a test installs no mock: it answers the connectivity
        /// check (<c>Defaults.InternetCheckUrl</c>) with 200 <c>yes</c>, and every other request with a 500
        /// whose error message names the request, recording it in <see cref="UnexpectedRequests"/>. A unit
        /// test therefore never reaches the network, and a request nobody planned for is loud.
        /// </summary>
        public static MockHttpClient Strict()
        {
            var mock = new MockHttpClient { IsStrictDefault = true };
            mock.OnRequest = request =>
            {
                if (IsInternetCheck(request.Url.Uri))
                {
                    request.RespondWith(200, "yes", new Dictionary<string, string> { ["Content-Type"] = "text/plain" });
                    return;
                }

                mock._unexpected.Add(request);
                request.RespondWith(500, new
                {
                    error = new
                    {
                        code = 50000,
                        statusCode = 500,
                        message = $"UTS: unexpected HTTP request {request}: this unit test installed no MockHttpClient",
                    },
                });
            };
            return mock;
        }

        /// <summary>An <see cref="HttpClient"/> over this handler that doesn't dispose it.</summary>
        public HttpClient AsHttpClient() => new HttpClient(this, disposeHandler: false);

        /// <summary>
        /// The specs' <c>await_connection_attempt(timeout?)</c>. Returns a parked (unanswered) attempt if
        /// there is one, else registers a waiter for the next attempt at the call, so a test can set up
        /// the wait for the next attempt before answering the current one.
        /// </summary>
        public Task<PendingHttpConnection> AwaitConnectionAttempt(
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => AwaitNext(_connectionWaiters, _unansweredConnections, timeout, "an HTTP connection attempt", callerFilePath, callerLineNumber);

        /// <summary>The specs' <c>await_request(timeout?)</c>; the same rules as <see cref="AwaitConnectionAttempt"/>.</summary>
        public Task<PendingHttpRequest> AwaitRequest(
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => AwaitNext(_requestWaiters, _unansweredRequests, timeout, "an HTTP request", callerFilePath, callerLineNumber);

        /// <summary>The specs' <c>reset()</c>: clears the record, waiters, parked attempts and queue. Handlers stay.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _connectionAttempts.Clear();
                _capturedRequests.Clear();
                _handlerErrors.Clear();
                _queued.Clear();
                _connectionWaiters.Clear();
                _requestWaiters.Clear();
                _unansweredConnections.Clear();
                _unansweredRequests.Clear();
            }

            _unexpected.Clear();
        }

        /// <summary>Legacy <c>queue_response(status, body, headers?)</c>: answers the next request.</summary>
        public void QueueResponse(int status, object body = null, IDictionary<string, string> headers = null)
            => Enqueue(_ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)), null);

        /// <summary>Legacy <c>queue_responses</c>: the same response for the next <paramref name="count"/> requests.</summary>
        public void QueueResponses(int count, int status, object body = null, IDictionary<string, string> headers = null)
        {
            for (var i = 0; i < count; i++)
            {
                QueueResponse(status, body, headers);
            }
        }

        /// <summary>Legacy <c>queue_timeout()</c>: the next request times out (see <see cref="PendingHttpRequest.RespondWithTimeout"/>).</summary>
        public void QueueTimeout() => Enqueue(_ => MockHttpOutcome.Timeout(), null);

        /// <summary>Legacy <c>queue_delayed_response(delay, status, body)</c>.</summary>
        public void QueueDelayedResponse(TimeSpan delay, int status, object body = null, IDictionary<string, string> headers = null)
            => Enqueue(_ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers), delay), null);

        /// <summary>Legacy <c>queue_response_for_host(host, ...)</c>: answers the next request to that host.</summary>
        public void QueueResponseForHost(string host, int status, object body = null, IDictionary<string, string> headers = null)
            => Enqueue(
                _ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)),
                request => string.Equals(request.Url.Host, host, StringComparison.OrdinalIgnoreCase));

        /// <summary>Legacy <c>queue_response_for_url(fragment, ...)</c>: answers the next request whose URL contains it.</summary>
        public void QueueResponseForUrl(string urlContains, int status, object body = null, IDictionary<string, string> headers = null)
            => Enqueue(
                _ => MockHttpOutcome.Success(MockHttpOutcome.BuildResponse(status, body, headers)),
                request => request.Url.ToString().Contains(urlContains));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var flight = new InFlight();
            lock (_lock)
            {
                _inFlight.Add(flight);
            }

            try
            {
                using (var timer = new RequestTimer(Clock, RequestTimeout))
                {
                    var connection = RaiseConnectionAttempt(request);
                    var connectionFailure = await AwaitAnswer(flight, connection.Answer, timer, $"connection to {connection}", cancellationToken)
                        .ConfigureAwait(false);
                    if (connectionFailure != null)
                    {
                        // A failed connection records no request.
                        await Fail(flight, connectionFailure, timer).ConfigureAwait(false);
                    }

                    var body = request.Content == null
                        ? null
                        : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var pending = RaiseRequest(request, body);
                    var outcome = await AwaitAnswer(flight, pending.Answer, timer, $"request {pending}", cancellationToken)
                        .ConfigureAwait(false);

                    if (outcome.Delay > TimeSpan.Zero)
                    {
                        await Delay(flight, outcome.Delay, timer, cancellationToken).ConfigureAwait(false);
                    }

                    if (outcome.IsTimeout || outcome.Failure != null)
                    {
                        await Fail(flight, outcome, timer).ConfigureAwait(false);
                    }

                    return outcome.Response;
                }
            }
            finally
            {
                lock (_lock)
                {
                    _inFlight.Remove(flight);
                }
            }
        }

        private static bool IsInternetCheck(Uri uri)
            => string.Equals(uri.Host, InternetCheckUri.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.AbsolutePath, InternetCheckUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// Awaits whichever of <paramref name="wakeOn"/> completes first, with the flight parked meanwhile. The
        /// flight stops counting as parked the moment any of them completes, not when the await resumes: an
        /// answer is a <c>RunContinuationsAsynchronously</c> completion, so <c>WhenAny</c> (and this method's
        /// continuation) runs later on the pool, and until the SDK has the response the mock must read as busy.
        /// </summary>
        private static async Task<Task> ParkUntilAny(InFlight flight, params Task[] wakeOn)
        {
            var any = Task.WhenAny(wakeOn);
            flight.Park(wakeOn);
            try
            {
                return await any.ConfigureAwait(false);
            }
            finally
            {
                flight.Unpark();
            }
        }

        private TaskCanceledException TimedOut()
            => new TaskCanceledException(
                $"MockHttpClient: the request timed out: HttpRequestTimeout ({RequestTimeout.TotalMilliseconds} ms) passed on the fake clock");

        private async Task<MockHttpOutcome> AwaitAnswer(
            InFlight flight,
            Task<MockHttpOutcome> answer,
            RequestTimer timer,
            string what,
            CancellationToken cancellationToken)
        {
            if (answer.IsCompleted)
            {
                return await answer.ConfigureAwait(false);
            }

            using (var guardSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var guard = Task.Delay(UnansweredTimeout, guardSource.Token);
                var first = await ParkUntilAny(flight, answer, timer.Elapsed, guard).ConfigureAwait(false);
                guardSource.Cancel();

                if (first == answer)
                {
                    return await answer.ConfigureAwait(false);
                }

                if (first == timer.Elapsed)
                {
                    throw TimedOut();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // The HttpClient's own Timeout (no fake clock) or a cancellation: surface it the way
                    // HttpClient does, as a timeout the SDK can retry.
                    throw new TaskCanceledException($"MockHttpClient: the HttpClient cancelled the {what} before it was answered");
                }

                throw new InvalidOperationException(
                    $"MockHttpClient: nothing answered the {what} within {UnansweredTimeout.TotalMilliseconds} ms (real time). " +
                    "Set OnRequest/OnConnectionAttempt, queue a response, or answer what AwaitRequest()/AwaitConnectionAttempt() returned.");
            }
        }

        private async Task Fail(InFlight flight, MockHttpOutcome outcome, RequestTimer timer)
        {
            if (!outcome.IsTimeout)
            {
                throw outcome.Failure;
            }

            if (!timer.OnFakeClock)
            {
                throw new TaskCanceledException(
                    "MockHttpClient: respond_with_timeout() without a fake clock fails at once (an infra stand-in for HttpRequestTimeout)");
            }

            await ParkUntilAny(flight, timer.Elapsed).ConfigureAwait(false);
            throw TimedOut();
        }

        private async Task Delay(InFlight flight, TimeSpan delay, RequestTimer timer, CancellationToken cancellationToken)
        {
            var clock = Clock;
            if (clock != null)
            {
                using (var delaySource = new CancellationTokenSource())
                {
                    var delayed = ((ITimerScheduler)clock).Delay(delay, delaySource.Token);
                    var first = await ParkUntilAny(flight, delayed, timer.Elapsed).ConfigureAwait(false);
                    if (first != delayed)
                    {
                        delaySource.Cancel();
                        throw TimedOut();
                    }
                }

                return;
            }

            try
            {
                var delayed = Task.Delay(delay, cancellationToken);
                await ParkUntilAny(flight, delayed).ConfigureAwait(false);
                await delayed.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException("MockHttpClient: the HttpClient's Timeout (HttpRequestTimeout) cut a delayed response short");
            }
        }

        private PendingHttpConnection RaiseConnectionAttempt(HttpRequestMessage request)
        {
            var uri = request.RequestUri;
            var connection = new PendingHttpConnection(
                uri.Host,
                uri.Port,
                string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase),
                Now());

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
                connection.RespondWithSuccess();
                return connection;
            }

            Invoke(() => handler(connection));
            if (!connection.Answered)
            {
                ParkUnanswered(connection, _connectionWaiters, _unansweredConnections);
            }

            return connection;
        }

        private PendingHttpRequest RaiseRequest(HttpRequestMessage request, byte[] body)
        {
            var pending = new PendingHttpRequest(request, body, CollectHeaders(request), Now());

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
                ParkUnanswered(pending, _requestWaiters, _unansweredRequests);
            }

            return pending;
        }

        /// <summary>
        /// Parks an attempt nothing answered for the next Await call, or hands it straight to a waiter
        /// that registered while the handler was running (it found the parked queue empty).
        /// </summary>
        private void ParkUnanswered<T>(T item, Queue<TaskCompletionSource<T>> waiters, Queue<T> parked)
        {
            TaskCompletionSource<T> lateWaiter = null;
            lock (_lock)
            {
                if (waiters.Count > 0)
                {
                    lateWaiter = waiters.Dequeue();
                }
                else
                {
                    parked.Enqueue(item);
                }
            }

            lateWaiter?.TrySetResult(item);
        }

        private Task<T> AwaitNext<T>(
            Queue<TaskCompletionSource<T>> waiters,
            Queue<T> parked,
            TimeSpan? timeout,
            string what,
            string callerFilePath,
            int callerLineNumber)
            where T : class
        {
            TaskCompletionSource<T> waiter;
            lock (_lock)
            {
                if (parked.Count > 0)
                {
                    return Task.FromResult(parked.Dequeue());
                }

                waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Enqueue(waiter);
            }

            return WithTimeout(waiter.Task, timeout ?? UtsWait.DefaultTimeout, what, callerFilePath, callerLineNumber);
        }

        private async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what, string callerFilePath, int callerLineNumber)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task)
            {
                string record;
                lock (_lock)
                {
                    record = $"{_connectionAttempts.Count} connection attempt(s), {_capturedRequests.Count} request(s) so far";
                }

                throw UtsAssert.Failure(
                    $"MockHttpClient: timed out after {timeout.TotalMilliseconds} ms waiting for {what} ({record})",
                    callerFilePath,
                    callerLineNumber,
                    null);
            }

            return await task.ConfigureAwait(false);
        }

        private DateTimeOffset Now() => Clock?.Now ?? DateTimeOffset.UtcNow;

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

        private void Enqueue(Func<PendingHttpRequest, MockHttpOutcome> build, Func<PendingHttpRequest, bool> predicate)
        {
            lock (_lock)
            {
                _queued.Add(new QueuedResponse(build, predicate));
            }
        }

        private List<T> Copy<T>(List<T> source)
        {
            lock (_lock)
            {
                return new List<T>(source);
            }
        }

        /// <summary>
        /// One SendAsync call; parked while it waits on something only the test or the fake clock completes
        /// (an answer, a fake-clock delay or timeout). Busy otherwise: running, or woken but not yet resumed.
        /// </summary>
        private sealed class InFlight
        {
            private volatile Task[] _wakeOn;

            internal bool IsParked
            {
                get
                {
                    var wakeOn = _wakeOn;
                    return wakeOn != null && wakeOn.All(t => !t.IsCompleted);
                }
            }

            internal void Park(Task[] wakeOn) => _wakeOn = wakeOn;

            internal void Unpark() => _wakeOn = null;
        }

        /// <summary>The client's request timeout on the fake clock; never elapses without one.</summary>
        private sealed class RequestTimer : IDisposable
        {
            private readonly CancellationTokenSource _source;

            internal RequestTimer(FakeClock clock, TimeSpan timeout)
            {
                if (clock != null && timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                {
                    _source = new CancellationTokenSource();
                    Elapsed = ((ITimerScheduler)clock).Delay(timeout, _source.Token);
                    OnFakeClock = true;
                }
                else
                {
                    Elapsed = new TaskCompletionSource<bool>().Task;
                }
            }

            internal Task Elapsed { get; }

            internal bool OnFakeClock { get; }

            public void Dispose()
            {
                if (_source != null)
                {
                    _source.Cancel();
                    _source.Dispose();
                }
            }
        }

        private sealed class QueuedResponse
        {
            private readonly Func<PendingHttpRequest, MockHttpOutcome> _build;
            private readonly Func<PendingHttpRequest, bool> _predicate;

            internal QueuedResponse(Func<PendingHttpRequest, MockHttpOutcome> build, Func<PendingHttpRequest, bool> predicate)
            {
                _build = build;
                _predicate = predicate;
            }

            internal bool Matches(PendingHttpRequest request) => _predicate == null || _predicate(request);

            internal MockHttpOutcome Build(PendingHttpRequest request) => _build(request);
        }
    }
}
