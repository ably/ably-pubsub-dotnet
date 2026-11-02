using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime.Workflow;
using Ably.PubSub.Tests.Push;
using Ably.PubSub.Transport.States.Connection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The base of every UTS unit test: the pseudocode's <c>install_mock(...)</c>, <c>enable_fake_timers()</c>,
    /// <c>ADVANCE_TIME(ms)</c>, <c>process_pending_events()</c> and the <c>Rest(options: ...)</c> /
    /// <c>Realtime(options: ...)</c> constructors, with teardown of every realtime client it built.
    /// </summary>
    public abstract class UtsUnitTestBase : UtsTestBase
    {
        /// <summary>The specs' <c>key: "appId.keyId:keySecret"</c>.</summary>
        public const string ValidKey = "appId.keyId:keySecret";

        private readonly object _lock = new object();
        private readonly List<Func<bool>> _idleChecks = new List<Func<bool>>();
        private readonly List<object> _registeredMocks = new List<object>();
        private readonly List<MockHttpClient> _httpMocks = new List<MockHttpClient>();
        private readonly List<MockWebSocket> _webSocketMocks = new List<MockWebSocket>();
        private readonly List<PubSubRealtimeClient> _realtimeClients = new List<PubSubRealtimeClient>();
        private readonly List<KeyValuePair<PubSubHttpClient, MockHttpClient>> _restClients =
            new List<KeyValuePair<PubSubHttpClient, MockHttpClient>>();

        private readonly SwitchingScheduler _scheduler;
        private MockHttpClient _installedHttp;
        private MockWebSocket _installedWebSocket;
        private MockNetworkListener _installedNetworkListener;
        private Func<byte[], byte[], byte[]> _installedDeltaDecoder;

        protected UtsUnitTestBase(ITestOutputHelper output)
            : base(output)
        {
            _scheduler = new SwitchingScheduler(this);
            OnVerify(VerifyMocks);
        }

        /// <summary>The fake clock once <see cref="EnableFakeTimers"/> has run, else null.</summary>
        public FakeClock Clock { get; private set; }

        /// <summary>The strict HTTP default, once a client was built without an installed <see cref="MockHttpClient"/>.</summary>
        public MockHttpClient DefaultHttp { get; private set; }

        /// <summary>
        /// The refusing WebSocket default, once a realtime client was built without an installed <see
        /// cref="MockWebSocket"/>.
        /// </summary>
        public MockWebSocket DefaultWebSocket { get; private set; }

        /// <summary>Requests the strict HTTP default answered with a 500 because the test installed no mock.</summary>
        public IReadOnlyList<PendingHttpRequest> UnexpectedRequests
            => DefaultHttp == null ? new List<PendingHttpRequest>() : DefaultHttp.UnexpectedRequests.Snapshot();

        /// <summary>Options with the specs' valid key and nothing else.</summary>
        public static ClientOptions ValidOptions() => new ClientOptions { Key = ValidKey };

        /// <summary>The specs' <c>install_mock(mock_http)</c>: used by every client built afterwards.</summary>
        public void InstallMock(MockHttpClient mock) => _installedHttp = mock;

        /// <summary>The specs' <c>install_mock(mock_ws)</c>: used by every realtime client built afterwards.</summary>
        public void InstallMock(MockWebSocket mock) => _installedWebSocket = mock;

        /// <summary>The specs' <c>install_mock(listener)</c>: binds it to every realtime client built afterwards.</summary>
        public void InstallMock(MockNetworkListener listener) => _installedNetworkListener = listener;

        /// <summary>
        /// The specs' <c>plugins: {vcdiff: decoder}</c>: a mock or failing decoder for every client built
        /// afterwards.
        /// </summary>
        public void InstallMock(IMockDeltaDecoder decoder) => InstallDeltaDecoder(decoder.AsDeltaDecoder());

        /// <summary>Any <c>(base, delta) =&gt; result</c> delta decoder, through hook H2.</summary>
        public void InstallDeltaDecoder(Func<byte[], byte[], byte[]> decoder) => _installedDeltaDecoder = decoder;

        /// <summary>
        /// The specs' <c>uninstall_mock()</c>: clients built afterwards get the defaults. Built clients keep
        /// theirs.
        /// </summary>
        public void UninstallMocks()
        {
            _installedHttp = null;
            _installedWebSocket = null;
            _installedNetworkListener = null;
            _installedDeltaDecoder = null;
        }

        /// <summary>The specs' <c>enable_fake_timers()</c>.</summary>
        public FakeClock EnableFakeTimers(DateTimeOffset? start = null)
        {
            List<Func<bool>> checks;
            List<KeyValuePair<PubSubHttpClient, MockHttpClient>> restClients;
            lock (_lock)
            {
                if (Clock != null)
                {
                    return Clock;
                }

                Clock = new FakeClock(start);
                checks = _idleChecks.ToList();
                restClients = _restClients.ToList();
            }

            foreach (var check in checks)
            {
                Clock.AddIdleCheck(check);
            }

            foreach (var mock in Mocks<MockHttpClient>(_httpMocks))
            {
                mock.Clock = Clock;
            }

            foreach (var mock in Mocks<MockWebSocket>(_webSocketMocks))
            {
                mock.Clock = Clock;
            }

            foreach (var pair in restClients)
            {
                UseFakeHttpTimeouts(pair.Key, pair.Value);
            }

            return Clock;
        }

        /// <summary>The specs' <c>ADVANCE_TIME(ms)</c>; await it before asserting.</summary>
        public Task AdvanceTime(
            long milliseconds,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => RequireClock(callerFilePath, callerLineNumber).AdvanceAsync(milliseconds, callerFilePath, callerLineNumber);

        /// <inheritdoc cref="AdvanceTime(long, string, int)"/>
        public Task AdvanceTime(
            TimeSpan by,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => RequireClock(callerFilePath, callerLineNumber).AdvanceAsync(by, callerFilePath, callerLineNumber);

        /// <summary>
        /// The specs' <c>process_pending_events()</c>: queues an empty command on every live realtime client's
        /// workflow, then yields (no real delay) until every workflow is idle and every mock has handed its
        /// posted deliveries and answered requests to the SDK, on two consecutive checks.
        /// </summary>
        public Task ProcessPendingEvents(
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            List<Func<bool>> checks;
            List<PubSubRealtimeClient> clients;
            FakeClock clock;
            lock (_lock)
            {
                checks = _idleChecks.ToList();
                clients = _realtimeClients.ToList();
                clock = Clock;
            }

            foreach (var client in clients.Where(c => !c.Disposed))
            {
                client.Workflow.QueueCommand(EmptyCommand.Instance);
            }

            return clock != null
                ? clock.DrainAsync("process_pending_events", callerFilePath, callerLineNumber)
                : UtsWait.Drain(checks, null, callerFilePath, callerLineNumber);
        }

        /// <summary>
        /// The specs' <c>Rest(options: ...)</c>, wired as described in Infra/README.md. The options are changed in
        /// place.
        /// </summary>
        public PubSubHttpClient Rest(ClientOptions options)
        {
            var http = Prepare(options, realtime: false);
            var mobileDevice = new FakeMobileDevice();
            var client = new PubSubHttpClient(options, mobileDevice);
            FinishRest(client, mobileDevice, http);
            return client;
        }

        /// <summary>
        /// The specs' <c>Realtime(options: ...)</c>, wired as described in Infra/README.md, and torn down after
        /// the test. The options are changed in place.
        /// </summary>
        public PubSubRealtimeClient Realtime(ClientOptions options)
        {
            var http = Prepare(options, realtime: true);
            var mobileDevice = new FakeMobileDevice();

            // The REST client is built through the constructor's factory argument so its device and HTTP
            // timeout are set before the realtime constructor can connect (AutoConnect).
            var client = new PubSubRealtimeClient(
                options,
                (restOptions, device) =>
                {
                    var rest = new PubSubHttpClient(restOptions, device);
                    FinishRest(rest, device, http);
                    return rest;
                },
                mobileDevice);

            DetachFromOsNetworkEvents(client);
            Track(client);
            lock (_lock)
            {
                _realtimeClients.Add(client);
            }

            // A disposed client is idle only once its workflow has run the dispose (Dispose() just queues it).
            AddIdleCheck(() => client.Disposed ? IsWorkflowStopped(client) : !client.Workflow.IsProcessingCommands());
            _installedNetworkListener?.Bind(client);
            return client;
        }

        private static List<T> Mocks<T>(List<T> mocks)
        {
            lock (mocks)
            {
                return mocks.ToList();
            }
        }

        /// <summary>
        /// Makes the client's internal HttpClient never time out on the real clock, so the mock's fake-clock
        /// timeout is the only one.
        /// </summary>
        private static void UseFakeHttpTimeouts(PubSubHttpClient rest, MockHttpClient handler)
        {
            var requester = rest.HttpClient;
            try
            {
                requester.Client.Timeout = Timeout.InfiniteTimeSpan;
            }
            catch (InvalidOperationException)
            {
                if (handler != null)
                {
                    requester.CreateInternalHttpClient(Timeout.InfiniteTimeSpan, new HttpClient(handler, disposeHandler: false));
                }
            }
        }

        private MockHttpClient Prepare(ClientOptions options, bool realtime)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var http = _installedHttp;
            if (http == null && options.HttpClient == null)
            {
                http = DefaultHttp ?? (DefaultHttp = MockHttpClient.Strict());
            }

            if (http != null)
            {
                options.HttpClient = new HttpClient(http, disposeHandler: false);
                http.Clock = Clock;
                http.RequestTimeout = options.HttpRequestTimeout;
                RegisterMock(http, _httpMocks, () => http.IsIdle);
            }

            if (realtime)
            {
                var webSocket = _installedWebSocket;
                if (webSocket == null && options.TransportFactory == null)
                {
                    webSocket = DefaultWebSocket ?? (DefaultWebSocket = new MockWebSocket(connection => connection.RespondWithRefused()));
                }

                if (webSocket != null)
                {
                    options.TransportFactory = webSocket;
                    webSocket.Clock = Clock;
                    RegisterMock(webSocket, _webSocketMocks, () => webSocket.IsIdle);
                }
            }

            var originalNow = options.NowFunc;
            options.NowFunc = () =>
            {
                var clock = Clock;
                return clock != null ? clock.Now : originalNow();
            };

            if (ReferenceEquals(options.TimerScheduler, SystemTimerScheduler.Instance) || options.TimerScheduler == null)
            {
                options.TimerScheduler = _scheduler;
            }

            if (_installedDeltaDecoder != null)
            {
                options.DeltaDecoder = _installedDeltaDecoder;
            }

            options.AutomaticNetworkStateMonitoring = false;

            if (options.Logger == null || ReferenceEquals(options.Logger, DefaultLogger.LoggerInstance))
            {
                options.Logger = InternalLogger.Create(options.LogLevel, options.LogHandler ?? new DefaultLoggerSink());
            }

            return http;
        }

        private void FinishRest(PubSubHttpClient rest, IMobileDevice mobileDevice, MockHttpClient http)
        {
            rest.Device = LocalDevice.Create(mobileDevice: mobileDevice);
            lock (_lock)
            {
                _restClients.Add(new KeyValuePair<PubSubHttpClient, MockHttpClient>(rest, http));
            }

            if (Clock != null)
            {
                UseFakeHttpTimeouts(rest, http);
            }
        }

        private void RegisterMock<T>(T mock, List<T> mocks, Func<bool> isIdle)
        {
            lock (_lock)
            {
                if (_registeredMocks.Any(m => ReferenceEquals(m, mock)))
                {
                    return;
                }

                _registeredMocks.Add(mock);
            }

            lock (mocks)
            {
                mocks.Add(mock);
            }

            AddIdleCheck(isIdle);
        }

        private void AddIdleCheck(Func<bool> isIdle)
        {
            FakeClock clock;
            lock (_lock)
            {
                _idleChecks.Add(isIdle);
                clock = Clock;
            }

            clock?.AddIdleCheck(isIdle);
        }

        private FakeClock RequireClock(string callerFilePath, int callerLineNumber)
        {
            var clock = Clock;
            if (clock == null)
            {
                throw UtsAssert.Failure("ADVANCE_TIME needs enable_fake_timers() (EnableFakeTimers()) first", callerFilePath, callerLineNumber, null);
            }

            return clock;
        }

        /// <summary>
        /// The teardown verification: fails the test if the strict HTTP default answered a request nobody
        /// expected, or any mock recorded a handler error (a handler that threw, or a delivery the SDK threw on).
        /// </summary>
        private void VerifyMocks()
        {
            var problems = new List<string>();
            foreach (var request in UnexpectedRequests)
            {
                problems.Add($"unexpected HTTP request answered with 500 by the strict default (install a MockHttpClient): {request}");
            }

            foreach (var mock in Mocks<MockHttpClient>(_httpMocks))
            {
                foreach (var error in mock.HandlerErrors)
                {
                    problems.Add($"a MockHttpClient handler threw {error.GetType().Name}: {error.Message}");
                }
            }

            foreach (var mock in Mocks<MockWebSocket>(_webSocketMocks))
            {
                foreach (var error in mock.HandlerErrors)
                {
                    problems.Add($"a MockWebSocket handler (or a delivery to the SDK) threw {error.GetType().Name}: {error.Message}");
                }
            }

            foreach (var problem in problems)
            {
                Output?.WriteLine("UTS: " + problem);
            }

            if (problems.Count > 0)
            {
                throw new XunitException(
                    $"UTS unit teardown: the mocks recorded {problems.Count} problem(s):" + Environment.NewLine + string.Join(Environment.NewLine, problems));
            }
        }

        /// <summary>
        /// The <c>TimerScheduler</c> every unit client gets: the fake clock while one is enabled, the real one
        /// otherwise, decided per timer, so <c>enable_fake_timers()</c> works after the client was built.
        /// </summary>
        private sealed class SwitchingScheduler : ITimerScheduler
        {
            private readonly UtsUnitTestBase _owner;

            internal SwitchingScheduler(UtsUnitTestBase owner)
            {
                _owner = owner;
            }

            private ITimerScheduler Current => (ITimerScheduler)_owner.Clock ?? SystemTimerScheduler.Instance;

            public IDisposable Schedule(TimeSpan delay, Action callback) => Current.Schedule(delay, callback);

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default) => Current.Delay(delay, cancellationToken);
        }
    }
}
