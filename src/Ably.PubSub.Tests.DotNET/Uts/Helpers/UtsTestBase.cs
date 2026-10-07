using System;
using System.Collections.Generic;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The collection the UTS <em>realtime</em> unit tier shares, so that its tests run serially.
    ///
    /// This is not tidiness. <c>Connection.NotifyOperatingSystemNetworkState</c> is <strong>static</strong>
    /// (Realtime/Connection.cs:49), so the RTN20 network-state tests do not act on their own client —
    /// they act on every live client in the process. Run in parallel with the rest of the tier, an
    /// "offline" notification reaches unrelated clients and
    /// <c>ConnectionManager.HandleNetworkStateChange</c> gives each one RTN20a's 80017 DISCONNECTED
    /// with <c>retryInstantly</c> (ConnectionManager.cs:370-379). Measured: that inflated connection
    /// attempt counts across the tier, put a DISCONNECTED into tests that never asked for one, and
    /// made several tests pass in isolation while failing in the suite.
    ///
    /// Declared with no fixture — the collection exists purely to serialise.
    /// </summary>
    [CollectionDefinition(UtsTestBase.RealtimeUnitCollection)]
    public class UtsRealtimeUnitCollection
    {
    }

    /// <summary>
    /// The base every derived UTS test inherits. It exists to make the specs'
    /// <c>AFTER EACH TEST: client.close(); uninstall_mock()</c> automatic: a test that throws before its
    /// own cleanup would otherwise leave a client — and its timers — running into the next test.
    /// </summary>
    public abstract class UtsTestBase : IDisposable
    {
        /// <summary>
        /// The name every realtime unit test class must put on its own <c>[Collection]</c> attribute.
        /// xUnit does not inherit <c>[Collection]</c> from a base class, so it cannot live here.
        /// </summary>
        public const string RealtimeUnitCollection = "UTS Realtime Unit";

        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed;

        protected UtsTestBase(ITestOutputHelper output)
        {
            Output = output;
        }

        protected ITestOutputHelper Output { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Registers anything that must be shut down when the test ends.</summary>
        protected T Track<T>(T disposable)
            where T : IDisposable
        {
            _disposables.Add(disposable);
            return disposable;
        }

        protected PubSubHttpClient RestClient(
            MockHttpClient mockHttp,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
            => UtsClients.RestClient(mockHttp, clock, configure);

        protected PubSubHttpClient RestClientWithDevice(
            MockHttpClient mockHttp,
            LocalDevice device = null,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
            => UtsClients.RestClientWithDevice(mockHttp, device, clock, configure);

        protected PubSubRealtimeClient RealtimeClient(
            MockWebSocket mockWebSocket,
            MockHttpClient mockHttp = null,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
            => Track(UtsClients.RealtimeClient(mockWebSocket, mockHttp, clock, configure));

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
            {
                return;
            }

            _disposed = true;
            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Output?.WriteLine($"UTS teardown: error disposing {disposable.GetType().Name}: {ex.Message}");
                }
            }

            _disposables.Clear();
        }
    }
}
