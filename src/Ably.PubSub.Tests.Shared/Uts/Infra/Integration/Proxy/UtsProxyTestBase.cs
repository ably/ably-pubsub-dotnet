using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>
    /// The base of every UTS proxy test: the specs' <c>create_proxy_session(…)</c> with its <c>AFTER EACH TEST:
    /// session.close()</c>, and <c>Realtime(options: …)</c> / <c>Rest(options: …)</c> pointed at a session
    /// (<c>proxy.md</c> "SDK ClientOptions for Proxy Tests").
    /// </summary>
    public abstract class UtsProxyTestBase : UtsIntegrationTestBase
    {
        protected UtsProxyTestBase(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        /// <summary>
        /// The specs' <c>create_proxy_session(endpoint:, rules:, port:, timeoutMs:)</c> (or <c>rules:</c> only:
        /// the endpoint defaults to the sandbox), closed after the test whether it passed or failed.
        /// </summary>
        /// <param name="timeoutMs">The session timeout, or null for <see cref="ProxySession.DefaultTimeoutMs"/>.</param>
        public async Task<ProxySession> CreateProxySession(
            IEnumerable<JObject> rules = null,
            string endpoint = null,
            int? port = null,
            int? timeoutMs = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var session = await ProxySession.CreateAsync(endpoint, rules, port, timeoutMs, callerFilePath, callerLineNumber).ConfigureAwait(false);

            // Registered before any client is built on it; teardown closes the clients first, then the sessions.
            OnTeardown(session.CloseAsync);
            return session;
        }

        /// <summary>
        /// The specs' <c>Realtime(options: ClientOptions(endpoint: "localhost", port: session.proxy_port, tls:
        /// false, …))</c>, torn down after the test.
        /// </summary>
        public PubSubRealtimeClient ProxyRealtime(ProxySession session, ClientOptions options = null)
        {
            options = options ?? new ClientOptions { AutoConnect = false };
            TargetProxy(session, options);
            return BuildRealtime(options);
        }

        /// <summary>The specs' <c>Rest(options: …)</c> pointed at a session. The options are changed in place.</summary>
        public PubSubHttpClient ProxyRest(ProxySession session, ClientOptions options = null)
        {
            options = options ?? new ClientOptions();
            TargetProxy(session, options);
            return BuildRest(options);
        }

        private void TargetProxy(ProxySession session, ClientOptions options)
        {
            options.Endpoint = session.ProxyHost;
            options.Port = session.ProxyPort;
            options.TlsPort = session.ProxyPort;
            options.Tls = false;

            var tokenAuthSet = options.AuthCallback != null
                || options.AuthUrl != null
                || !string.IsNullOrEmpty(options.Token)
                || options.TokenDetails != null;
            if (!tokenAuthSet)
            {
                // RSC18 refuses Basic auth over the plain hop, so the key is used only by
                // the direct token client, never sent to the proxy.
                options.AuthCallback = SandboxTokenCallback(string.IsNullOrEmpty(options.Key) ? ApiKey : options.Key);
                options.Key = null;
            }
        }
    }

    /// <summary>The proxy collection: every UTS proxy class joins it with <c>[Collection("UTS Proxy")]</c>.</summary>
    [CollectionDefinition(Name)]
    public class UtsProxyCollection : ICollectionFixture<SandboxAppFixture>
    {
        public const string Name = "UTS Proxy";
    }
}
