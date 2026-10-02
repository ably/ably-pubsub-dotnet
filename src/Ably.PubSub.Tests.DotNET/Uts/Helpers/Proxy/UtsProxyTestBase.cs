using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// A <c>[Fact]</c> for the proxy tier, skipped when no proxy is reachable.
    ///
    /// The tier has its own CI leg which does have one, so this is a convenience for a local
    /// integration run rather than the mechanism that keeps the tier out of the default legs — that is
    /// the <c>requires=proxy</c> trait on <see cref="UtsProxyTestBase"/> and the Cake filters.
    /// </summary>
    public sealed class ProxyFactAttribute : FactAttribute
    {
        public ProxyFactAttribute()
        {
            if (!UtsProxyControl.IsAvailable)
            {
                Skip =
                    "uts-proxy is not available. Set UTS_PROXY_PATH or UTS_PROXY_CONTROL_URL, or put " +
                    "uts-proxy on PATH. On Windows, build it from ably/uts-proxy with " +
                    "`go build -o uts-proxy.exe .`.";
            }
        }
    }

    /// <summary>
    /// A <see cref="ProxyFactAttribute"/> that is also deviation-gated, for a proxy test whose
    /// spec-correct assertion the SDK fails. Both gates apply: no proxy skips it, and so does a run
    /// without <c>RUN_DEVIATIONS=1</c>.
    /// </summary>
    public sealed class ProxyDeviationFactAttribute : FactAttribute
    {
        public ProxyDeviationFactAttribute()
        {
            if (!UtsProxyControl.IsAvailable)
            {
                Skip = "uts-proxy is not available. See ProxyFactAttribute.";
            }
            else if (!Deviations.Enabled)
            {
                Skip = Deviations.SkipReason;
            }
        }
    }

    /// <summary>
    /// The base every UTS proxy test inherits.
    ///
    /// It carries <strong>two</strong> traits. <c>type=integration</c> keeps these out of the unit leg,
    /// whose filter is <c>type!=integration</c> — any other value would let a proxy test run there,
    /// with no proxy and no network. <c>requires=proxy</c> is what the integration leg excludes and the
    /// proxy leg selects.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    [Trait("type", "integration")]
    [Trait("requires", "proxy")]
    public abstract class UtsProxyTestBase : IDisposable
    {
        private const string SandboxRealtimeHost = "sandbox-realtime.ably.io";
        private const string SandboxRestHost = "sandbox-rest.ably.io";

        private readonly AblySandboxFixture _fixture;
        private readonly List<UtsProxySession> _sessions = new List<UtsProxySession>();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private UtsSandbox _sandbox;
        private bool _disposed;

        protected UtsProxyTestBase(AblySandboxFixture fixture, ITestOutputHelper output)
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

        protected async Task<UtsSandbox> Sandbox()
            => _sandbox ?? (_sandbox = await UtsSandbox.Create(_fixture));

        /// <summary>
        /// The specs' <c>create_proxy_session(...)</c> together with their
        /// <c>AFTER EACH TEST: session.close()</c> — call it as many times as a test needs; every
        /// session is closed afterwards.
        /// </summary>
        protected async Task<UtsProxySession> ProxySession(
            JArray rules = null,
            int timeoutMs = UtsProxySession.DefaultSessionTimeoutMs)
        {
            var control = await UtsProxyControl.Instance().ConfigureAwait(false);
            var session = await control
                .CreateSession(SandboxRealtimeHost, SandboxRestHost, rules, timeoutMs: timeoutMs)
                .ConfigureAwait(false);

            _sessions.Add(session);
            return session;
        }

        /// <summary>
        /// A REST client pointed at the session. The specs write <c>endpoint: "localhost"</c>, which
        /// REC1b2 defines as setting both hosts; .NET has no single <c>Endpoint</c> option, so both
        /// <c>RestHost</c> and <c>RealtimeHost</c> are set here. Setting either also empties the
        /// fallback hosts (<c>ClientOptions.GetFallbackHosts</c>), which is REC2c2's
        /// "an explicit hostname auto-disables fallbacks".
        /// </summary>
        protected PubSubHttpClient ProxyRestClient(
            UtsProxySession session,
            Func<TokenParams, Task<object>> authCallback,
            Action<ClientOptions> configure = null)
            => new PubSubHttpClient(ProxyOptions(session, authCallback, configure));

        protected PubSubRealtimeClient ProxyRealtimeClient(
            UtsProxySession session,
            Func<TokenParams, Task<object>> authCallback,
            Action<ClientOptions> configure = null)
            => Track(new PubSubRealtimeClient(ProxyOptions(session, authCallback, options =>
            {
                // A state recorder has to be registered before the connection opens, so the tier never
                // auto-connects.
                options.AutoConnect = false;
                configure?.Invoke(options);
            })));

        protected ClientOptions ProxyOptions(
            UtsProxySession session,
            Func<TokenParams, Task<object>> authCallback,
            Action<ClientOptions> configure = null)
        {
            var options = new ClientOptions
            {
                RestHost = session.ProxyHost,
                RealtimeHost = session.ProxyHost,
                Port = session.ProxyPort,
                TlsPort = session.ProxyPort,
                Tls = false,

                // Basic auth cannot be used through the proxy at all: the session speaks plain HTTP,
                // and RSC18 makes the SDK refuse basic auth over non-TLS before a request is written,
                // so a client built with Key = ... never reaches a rule. Every test authenticates with
                // a callback, including the /time ones where it looks unnecessary.
                AuthCallback = authCallback,
            };

            configure?.Invoke(options);
            return options;
        }

        /// <summary>
        /// An auth callback that signs an Ably JWT locally.
        ///
        /// Local signing costs no round trip, which matters here: a callback that fetched a token
        /// through a client pointed at the session would put a request in front of the waiting rule and
        /// an extra <c>http_request</c> in the log, breaking every assertion that counts requests
        /// exactly.
        ///
        /// The JWT is wrapped in a <c>TokenDetails</c> rather than returned as the bare string RSA8d
        /// allows, because of D13: a string from an <c>authCallback</c> is only ever read as a
        /// <c>TokenRequest</c> here, so a bare JWT fails to parse as JSON and the request dies with
        /// 80019. Returning it as a <c>TokenDetails</c> is the shape the SDK does accept, and keeps
        /// every proxy test measuring its own subject rather than D13.
        /// </summary>
        protected async Task<Func<TokenParams, Task<object>>> JwtAuthCallback(
            string key = null,
            string clientId = null)
        {
            var sandbox = await Sandbox().ConfigureAwait(false);
            var apiKey = key ?? sandbox.KeyStr;
            var keyName = UtsSandbox.ExtractKeyName(apiKey);
            var keySecret = UtsSandbox.ExtractKeySecret(apiKey);

            return tokenParams => Task.FromResult<object>(
                new TokenDetails(
                    UtsSandbox.GenerateJwt(
                        keyName,
                        keySecret,
                        clientId: clientId ?? tokenParams?.ClientId)));
        }

        /// <summary>
        /// The specs' <c>token_auth_callback(api_key)</c>: a callback that asks the sandbox for a
        /// real token, through a client pointed <strong>directly</strong> at the sandbox rather
        /// than at the session. Nothing it does appears in the proxy log, so a test can keep
        /// counting its own requests exactly, and the token is a genuine one the server will
        /// renew - which is what RSC10 needs and a locally-signed JWT cannot give it.
        /// </summary>
        protected async Task<Func<TokenParams, Task<object>>> TokenAuthCallback(
            string key = null,
            Action onInvoked = null)
        {
            var sandbox = await Sandbox().ConfigureAwait(false);
            var apiKey = key ?? sandbox.KeyStr;

            return async tokenParams =>
            {
                onInvoked?.Invoke();

                var direct = new PubSubHttpClient(new ClientOptions
                {
                    Key = apiKey,
                    Environment = "sandbox",
                    Tls = true,
                });

                return await direct.Auth.RequestTokenAsync(tokenParams).ConfigureAwait(false);
            };
        }

        protected T Track<T>(T disposable)
            where T : IDisposable
        {
            _disposables.Add(disposable);
            return disposable;
        }

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
                    Output?.WriteLine($"UTS proxy teardown: disposing {disposable.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var session in _sessions)
            {
                try
                {
                    session.Close().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Output?.WriteLine($"UTS proxy teardown: closing {session}: {ex.Message}");
                }
            }

            _disposables.Clear();
            _sessions.Clear();
        }
    }
}
