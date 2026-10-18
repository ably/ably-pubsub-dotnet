using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>
    /// The base of every UTS proxy test: the specs' <c>create_proxy_session(…)</c> with its
    /// <c>AFTER EACH TEST: session.close()</c>, and <c>Realtime(options: …)</c> / <c>Rest(options: …)</c> pointed at a
    /// session (<c>proxy.md</c> "SDK ClientOptions for Proxy Tests"). The direct-sandbox factories of
    /// <see cref="UtsIntegrationTestBase"/> stay available for clients that must bypass the proxy.
    ///
    /// <para>A derived class takes <c>(SandboxAppFixture fixture, ITestOutputHelper output)</c> and carries, itself:
    /// <c>[Collection(UtsProxyCollection.Name)]</c> (one proxy process and one control port per test process, so the
    /// proxy classes run one after another), <c>[Trait("uts", "proxy")]</c> and <c>[Trait("type", "integration")]</c>.</para>
    ///
    /// <para><b>Proxied clients</b> (<see cref="ProxyRealtime"/>, <see cref="ProxyRest"/>) get <c>Endpoint = "localhost"</c>
    /// (REC1b2: both hosts; an explicit hostname also disables fallback hosts, REC2c2), <c>Port</c> and <c>TlsPort</c> =
    /// the session's port, <c>Tls = false</c> (the proxy serves plain HTTP/WS and uses TLS only upstream), plus the
    /// per-client defaults of the integration base. JSON only: the proxy relays text frames only.</para>
    ///
    /// <para><b>Auth through the proxy.</b> Basic auth can't cross the plain hop: the SDK refuses Basic auth without
    /// TLS before sending anything (RSC18; <c>AblyAuth.EnsureSecureConnection</c>). Unless the test set token auth
    /// itself (<c>AuthCallback</c>, <c>AuthUrl</c>, <c>Token</c> or <c>TokenDetails</c>), a proxied client gets an
    /// <c>AuthCallback</c> that returns a real <c>TokenDetails</c> requested by a separate REST client straight from the
    /// sandbox over TLS, with the test's <c>Key</c> (the specs' <c>key: api_key</c>) or the app key. The token request
    /// deliberately bypasses the session, so it adds nothing to the session's event log that a test counts.</para>
    /// </summary>
    public abstract class UtsProxyTestBase : UtsIntegrationTestBase
    {
        protected UtsProxyTestBase(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        /// <summary>
        /// The specs' <c>create_proxy_session(endpoint:, rules:, port:, timeoutMs:)</c> (or <c>rules:</c> only: the
        /// endpoint defaults to the sandbox), closed after the test whether it passed or failed.
        /// </summary>
        /// <param name="rules">The initial rules, or null.</param>
        /// <param name="endpoint">The spec's endpoint; the sandbox when null.</param>
        /// <param name="port">A fixed proxy port, or null.</param>
        /// <param name="timeoutMs">The session timeout, or null for <see cref="ProxySession.DefaultTimeoutMs"/>.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        /// <returns>The session.</returns>
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
        /// The specs' <c>Realtime(options: ClientOptions(endpoint: "localhost", port: session.proxy_port, tls: false, …))</c>,
        /// torn down after the test. The options are changed in place. Every proxied realtime client in the corpus
        /// sets <c>autoConnect: false</c>, which the translation keeps; with no options at all the client is built
        /// with <c>AutoConnect = false</c>, so a recorder can subscribe before the connection opens.
        /// </summary>
        /// <param name="session">The session to connect through.</param>
        /// <param name="options">The spec's options, or null.</param>
        /// <returns>The client.</returns>
        public PubSubRealtimeClient ProxyRealtime(ProxySession session, ClientOptions options = null)
        {
            options = options ?? new ClientOptions { AutoConnect = false };
            TargetProxy(session, options);
            return BuildRealtime(options);
        }

        /// <summary>The specs' <c>Rest(options: …)</c> pointed at a session. The options are changed in place.</summary>
        /// <param name="session">The session to send through.</param>
        /// <param name="options">The spec's options, or null.</param>
        /// <returns>The client.</returns>
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
                // See the class summary: RSC18 refuses Basic auth over the plain hop, so the key is used only by
                // the direct token client, never sent to the proxy.
                options.AuthCallback = SandboxTokenCallback(string.IsNullOrEmpty(options.Key) ? ApiKey : options.Key);
                options.Key = null;
            }
        }
    }

    /// <summary>
    /// The proxy collection: every UTS proxy class joins it with <c>[Collection("UTS Proxy")]</c>. It provisions its
    /// own sandbox app (one per collection), and its classes run one after another because they share the proxy's
    /// fixed control port.
    /// </summary>
    [CollectionDefinition(Name)]
    public class UtsProxyCollection : ICollectionFixture<SandboxAppFixture>
    {
        public const string Name = "UTS Proxy";
    }
}
