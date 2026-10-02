using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The base for the <em>realtime</em> integration tier.
    ///
    /// <para>
    /// It exists to carry <c>[Trait("tier", "realtime")]</c>, which lets the build run the realtime
    /// half in its own process. That split is not cosmetic: each group is green on its own and fails
    /// in numbers with "timed out waiting for Connected" when two share a process. It is not socket
    /// exhaustion — measured at 32 sockets in TIME_WAIT against a 16384-port range — so it is
    /// contention on the sandbox app they all reach. See the three passes in
    /// <c>cake-build/tasks/test.cake</c>.
    /// </para>
    /// <para>
    /// Both traits are restated here rather than inherited. xUnit does not walk past the immediate
    /// base when collecting traits, so an intermediate class that declares only <c>tier</c> loses
    /// the <c>type</c> trait its own base declares — measured: <c>type=integration</c> matched none
    /// of these tests while <c>tier=realtime</c> matched all 33, which would have put every realtime
    /// integration test into the unit leg and out of the integration leg entirely.
    /// </para>
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    [Trait("type", "integration")]
    [Trait("tier", "realtime")]
    public abstract class UtsRealtimeIntegrationTestBase : UtsIntegrationTestBase
    {
        protected UtsRealtimeIntegrationTestBase(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }
    }

    /// <summary>
    /// The base every UTS integration test inherits, and the single place the specs' sandbox setup
    /// is translated — so a derived file does not repeat it.
    ///
    /// <para>
    /// A spec's <c>endpoint: "nonprod:sandbox"</c> is <c>Environment = "sandbox"</c>, applied by
    /// <c>SandboxRestClient</c> and <c>SandboxRealtimeClient</c>. Its <c>app_config.keys[i]</c> is
    /// <c>UtsSandbox.Key(i)</c>. Its per-file app provisioning and teardown is the shared
    /// <c>AblySandboxFixture</c>: one app for the whole assembly, which is why a derived test gives
    /// every channel name, client id and device id a <c>UtsSandbox.RandomId()</c> suffix and removes
    /// anything it registers in a <c>finally</c>.
    /// </para>
    /// <para>
    /// It also carries <c>[Trait("type", "integration")]</c> so the test lands in the right CI leg.
    /// The unit legs filter on <c>type!=integration</c>, so a missing trait silently puts a network
    /// test into the unit run.
    /// </para>
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    [Trait("type", "integration")]
    [Trait("tier", "uts-rest")]
    public abstract class UtsIntegrationTestBase : IDisposable
    {
        private readonly AblySandboxFixture _fixture;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private UtsSandbox _sandbox;
        private bool _disposed;

        protected UtsIntegrationTestBase(AblySandboxFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            Output = output;
        }

        protected ITestOutputHelper Output { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>The specs' <c>app_config</c>. Provisioned once per run and cached by the fixture.</summary>
        protected async Task<UtsSandbox> Sandbox()
            => _sandbox ?? (_sandbox = await UtsSandbox.Create(_fixture));

        /// <summary>
        /// The specs' <c>Rest(ClientOptions(key: api_key, environment: "sandbox"))</c>.
        ///
        /// Unlike the unit tier's factory this keeps the library's own defaults for fallback hosts and
        /// the connectivity check — an integration test is meant to reach the network, and overriding
        /// those would stop it testing the thing it is pointed at.
        /// </summary>
        protected async Task<PubSubHttpClient> SandboxRestClient(
            string key = null,
            Action<ClientOptions> configure = null)
        {
            var options = await SandboxOptions(key, configure).ConfigureAwait(false);
            return new PubSubHttpClient(options);
        }

        /// <summary>The realtime equivalent, registered for teardown so its connection is closed.</summary>
        protected async Task<PubSubRealtimeClient> SandboxRealtimeClient(
            string key = null,
            Action<ClientOptions> configure = null)
        {
            var options = await SandboxOptions(key, configure).ConfigureAwait(false);
            return Track(new PubSubRealtimeClient(options));
        }

        protected async Task<ClientOptions> SandboxOptions(
            string key = null,
            Action<ClientOptions> configure = null)
        {
            var sandbox = await Sandbox().ConfigureAwait(false);
            var options = new ClientOptions
            {
                Key = key ?? sandbox.KeyStr,
                Environment = "sandbox",
                Tls = true,
            };

            configure?.Invoke(options);
            return options;
        }

        protected T Track<T>(T disposable)
            where T : IDisposable
        {
            _disposables.Add(disposable);
            return disposable;
        }

        /// <summary>
        /// This tier's <c>AWAIT_STATE</c>. The default is ten seconds rather than the unit tier's five,
        /// because a connect here opens a real socket.
        /// </summary>
        protected static Task AwaitConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null)
            => UtsClients.AwaitConnectionState(connection, state, timeout ?? TimeSpan.FromSeconds(10));

        protected static Task AwaitChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null)
            => UtsClients.AwaitChannelState(channel, state, timeout ?? TimeSpan.FromSeconds(10));

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
