using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Push;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Infra.Integration
{
    /// <summary>
    /// The base of every UTS direct-sandbox test (and, through <c>UtsProxyTestBase</c>, every proxy test): the
    /// specs' <c>Rest(options: …)</c> / <c>Realtime(options: …)</c> pointed at the collection's sandbox app, the
    /// specs' integration helpers (<c>random_id()</c>, unique channel names, <c>extract_key_name</c>,
    /// <c>extract_key_secret</c>, <c>generate_jwt</c>, <c>request_token_from_sandbox</c>), wall-clock state waits,
    /// and teardown that closes every realtime client it built.
    ///
    /// <para>A derived class takes <c>(SandboxAppFixture fixture, ITestOutputHelper output)</c> and carries, itself
    /// (xUnit doesn't reliably read them off a base class):
    /// <c>[Collection(UtsSandboxCollection.Name)]</c>, <c>[Trait("uts", "integration")]</c> and
    /// <c>[Trait("type", "integration")]</c>.</para>
    ///
    /// <para><b>The factories</b> change the options in place:</para>
    /// <list type="bullet">
    /// <item><c>Endpoint</c> = the app's endpoint (<c>ABLY_ENDPOINT</c> or <c>nonprod:sandbox</c>) unless the test set one;</item>
    /// <item><c>Key</c> = the app's first key (the specs' <c>api_key</c>) unless the test set any auth
    /// (<c>Key</c>, <c>Token</c>, <c>TokenDetails</c>, <c>AuthCallback</c> or <c>AuthUrl</c>);</item>
    /// <item><c>AutomaticNetworkStateMonitoring = false</c>, and each realtime client's connection is taken off the SDK's
    /// static OS network-state list right after construction (<see cref="UtsTestBase.DetachFromOsNetworkEvents"/>), so
    /// neither the host's network changes nor another test's <c>Connection.NotifyOperatingSystemNetworkState</c> reach it;</item>
    /// <item>its own logger (<c>InternalLogger</c> with the options' <c>LogLevel</c> and <c>LogHandler</c>), because the SDK
    /// writes the level and sink into whatever logger it is given and the default is process-wide;</item>
    /// <item>its own <c>LocalDevice</c> over a <c>FakeMobileDevice</c>, so it never reads the process-wide device
    /// that native push tests change (the same reason as the unit base).</item>
    /// </list>
    /// <para>Nothing else is defaulted: <c>AutoConnect</c>, <c>Tls</c> and fallback hosts are used exactly as the
    /// spec's options give them, so the pseudocode's <c>autoConnect: false</c> must be translated. JSON only:
    /// <c>UseBinaryProtocol</c> is a no-op in this build (decision log #6).</para>
    ///
    /// <para><b>Time.</b> Every wait here is wall-clock (<see cref="UtsWait"/> measures with a monotonic real clock):
    /// state waits default to <see cref="StateTimeout"/>; a poll must pass its own timeout (10-30 s).</para>
    /// </summary>
    public abstract class UtsIntegrationTestBase : UtsTestBase
    {
        /// <summary>The default bound on an integration state wait (<c>integration-testing.md</c>: 10-15 s).</summary>
        public static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);

        protected UtsIntegrationTestBase(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        }

        /// <summary>The collection's fixture.</summary>
        public SandboxAppFixture Fixture { get; }

        /// <summary>The specs' <c>app_config</c>: the collection's sandbox app.</summary>
        public SandboxApp App
        {
            get
            {
                var app = Fixture.App;
                if (app == null)
                {
                    throw new InvalidOperationException("The sandbox app isn't provisioned: SandboxAppFixture.InitializeAsync didn't run or failed.");
                }

                return app;
            }
        }

        /// <summary>The specs' <c>api_key</c> (<c>app_config.keys[0].key_str</c>).</summary>
        public string ApiKey => App.KeyStr;

        /// <summary>The specs' <c>endpoint: "nonprod:sandbox"</c> (or <c>ABLY_ENDPOINT</c>).</summary>
        public string SandboxEndpoint => App.Endpoint;

        /// <summary>The sandbox answers CLOSE, so teardown closes cleanly before disposing.</summary>
        protected override bool CloseClientsOnTeardown => true;

        /// <summary>A real round trip: longer than the unit tier's bound.</summary>
        protected override TimeSpan CloseTimeout => TimeSpan.FromSeconds(10);

        /// <summary>The specs' <c>random_id()</c> / <c>base64(random_bytes(n))</c>, url-safe (base64url, unpadded) so it is valid in a channel name.</summary>
        /// <param name="byteCount">How many random bytes to encode.</param>
        /// <returns>The encoded bytes.</returns>
        public static string RandomId(int byteCount = 6)
        {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Base64Url(bytes);
        }

        /// <summary>The specs' unique channel name: <c>test-&lt;prefix&gt;-&lt;random&gt;</c>.</summary>
        /// <param name="prefix">The descriptive part, for example the spec point.</param>
        /// <returns>The channel name.</returns>
        public static string UniqueChannelName(string prefix) => $"test-{prefix}-{RandomId()}";

        /// <summary>The specs' <c>extract_key_name(api_key)</c>: <c>appId.keyId</c>.</summary>
        /// <param name="apiKey">A key of the form <c>appId.keyId:secret</c>.</param>
        /// <returns>The part before the colon.</returns>
        public static string ExtractKeyName(string apiKey) => SplitKey(apiKey)[0];

        /// <summary>The specs' <c>extract_key_secret(api_key)</c>.</summary>
        /// <param name="apiKey">A key of the form <c>appId.keyId:secret</c>.</param>
        /// <returns>The part after the colon.</returns>
        public static string ExtractKeySecret(string apiKey) => SplitKey(apiKey)[1];

        /// <summary>
        /// The specs' <c>generate_jwt(key_name, key_secret, ttl, client_id, capability)</c>: an Ably JWT signed
        /// locally with HS256 (header <c>kid</c> = the key name; claims <c>iat</c>, <c>exp</c>, and
        /// <c>x-ably-clientId</c> / <c>x-ably-capability</c> when given). The spec's <c>ttl</c> is in milliseconds:
        /// pass <c>TimeSpan.FromMilliseconds(ttl)</c>.
        /// </summary>
        /// <param name="keyName"><c>appId.keyId</c>.</param>
        /// <param name="keySecret">The key secret, the HMAC key.</param>
        /// <param name="ttl">The token lifetime; 1 hour when null.</param>
        /// <param name="clientId">The <c>x-ably-clientId</c> claim, if any.</param>
        /// <param name="capability">The <c>x-ably-capability</c> claim (a JSON string), if any.</param>
        /// <returns>The JWT.</returns>
        public static string GenerateJwt(
            string keyName,
            string keySecret,
            TimeSpan? ttl = null,
            string clientId = null,
            string capability = null)
        {
            var issuedAt = DateTimeOffset.UtcNow;
            var expires = issuedAt.Add(ttl ?? TimeSpan.FromHours(1));

            var header = new JObject
            {
                ["typ"] = "JWT",
                ["alg"] = "HS256",
                ["kid"] = keyName,
            };

            var claims = new JObject
            {
                ["iat"] = issuedAt.ToUnixTimeSeconds(),
                ["exp"] = expires.ToUnixTimeSeconds(),
            };

            if (clientId != null)
            {
                claims["x-ably-clientId"] = clientId;
            }

            if (capability != null)
            {
                claims["x-ably-capability"] = capability;
            }

            var signingInput = Base64Url(Encoding.UTF8.GetBytes(header.ToString(Formatting.None))) + "." +
                Base64Url(Encoding.UTF8.GetBytes(claims.ToString(Formatting.None)));
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret)))
            {
                var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
                return signingInput + "." + Base64Url(signature);
            }
        }

        /// <summary>
        /// The specs' <c>request_token_from_sandbox(api_key, params)</c>: a real token from the sandbox
        /// (<c>POST /keys/{keyName}/requestToken</c>), requested by a separate REST client that talks straight to
        /// the sandbox over TLS with Basic auth. Nothing it sends goes through a proxy session.
        /// </summary>
        /// <param name="apiKey">The key to request the token with; the app's first key when null.</param>
        /// <param name="tokenParams">The token params, or null.</param>
        /// <returns>The token.</returns>
        public Task<TokenDetails> RequestTokenFromSandbox(string apiKey = null, TokenParams tokenParams = null)
        {
            var direct = BuildRest(new ClientOptions { Key = apiKey ?? ApiKey, Endpoint = SandboxEndpoint });
            return direct.Auth.RequestTokenAsync(tokenParams);
        }

        /// <summary>
        /// An <c>authCallback</c> that answers every token request with <see cref="RequestTokenFromSandbox"/>
        /// (the callback's token params are passed on), so the token is a real, renewable one.
        /// </summary>
        /// <param name="apiKey">The key the tokens are requested with; the app's first key when null.</param>
        /// <param name="onInvoked">Called on every invocation, before the token is requested (the specs' <c>auth_callback_count++</c>).</param>
        /// <returns>The callback.</returns>
        public Func<TokenParams, Task<object>> SandboxTokenCallback(string apiKey = null, Action<TokenParams> onInvoked = null)
        {
            var key = apiKey ?? ApiKey;
            return async tokenParams =>
            {
                onInvoked?.Invoke(tokenParams);
                return await RequestTokenFromSandbox(key, tokenParams).ConfigureAwait(false);
            };
        }

        /// <summary>The specs' <c>Rest(options: …)</c> against the sandbox, wired as the class summary describes.</summary>
        /// <param name="options">The spec's options; changed in place. Null means none.</param>
        /// <returns>The client.</returns>
        public PubSubHttpClient Rest(ClientOptions options = null)
        {
            options = options ?? new ClientOptions();
            TargetSandbox(options);
            return BuildRest(options);
        }

        /// <summary>The specs' <c>Realtime(options: …)</c> against the sandbox, wired as the class summary describes, and torn down after the test.</summary>
        /// <param name="options">The spec's options; changed in place. Null means none (then the client auto-connects, as the SDK's default).</param>
        /// <returns>The client.</returns>
        public PubSubRealtimeClient Realtime(ClientOptions options = null)
        {
            options = options ?? new ClientOptions();
            TargetSandbox(options);
            return BuildRealtime(options);
        }

        /// <summary>The specs' <c>AWAIT_STATE connection.state == state</c>, bounded by <see cref="StateTimeout"/> unless the spec gives a timeout.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="state">The state to wait for.</param>
        /// <param name="timeout">The spec's timeout, if any.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        /// <returns>A task that completes when the state is reached.</returns>
        public Task AwaitConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => UtsWait.AwaitConnectionState(connection, state, timeout ?? StateTimeout, callerFilePath, callerLineNumber);

        /// <summary>The specs' <c>AWAIT_STATE channel.state == state</c>, bounded by <see cref="StateTimeout"/> unless the spec gives a timeout.</summary>
        /// <param name="channel">The channel.</param>
        /// <param name="state">The state to wait for.</param>
        /// <param name="timeout">The spec's timeout, if any.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        /// <returns>A task that completes when the state is reached.</returns>
        public Task AwaitChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => UtsWait.AwaitChannelState(channel, state, timeout ?? StateTimeout, callerFilePath, callerLineNumber);

        /// <summary>Fails the test, attributed to the caller, unless an SDK operation's <see cref="Result"/> succeeded.</summary>
        /// <param name="result">The result of, for example, <c>AttachAsync</c> or <c>PublishAsync</c>.</param>
        /// <param name="what">What the operation was, for the failure message.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        public static void AssertSucceeded(
            Result result,
            string what,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            if (result == null || !result.IsSuccess)
            {
                var error = result?.Error;
                var detail = error == null ? "no error given" : error.Code + " " + error.Message;
                throw UtsAssert.Failure(
                    $"{what} failed: {detail}",
                    callerFilePath,
                    callerLineNumber);
            }
        }

        /// <summary>True when the options already say how to authenticate.</summary>
        /// <param name="options">The options.</param>
        /// <returns>Whether any of Key, Token, TokenDetails, AuthCallback or AuthUrl is set.</returns>
        internal static bool HasAuth(ClientOptions options)
            => !string.IsNullOrEmpty(options.Key)
                || !string.IsNullOrEmpty(options.Token)
                || options.TokenDetails != null
                || options.AuthCallback != null
                || options.AuthUrl != null;

        /// <summary>
        /// Builds a REST client from options that already say where to go and how to authenticate, adding only
        /// the per-client logger, device and network-monitoring defaults. Not tracked: a REST client holds no
        /// connection to close.
        /// </summary>
        /// <param name="options">The finished options; changed in place.</param>
        /// <returns>The client.</returns>
        protected PubSubHttpClient BuildRest(ClientOptions options)
        {
            ApplyClientDefaults(options);
            var mobileDevice = new FakeMobileDevice();
            var client = new PubSubHttpClient(options, mobileDevice);
            client.Device = LocalDevice.Create(mobileDevice: mobileDevice);
            return client;
        }

        /// <summary>The realtime counterpart of <see cref="BuildRest"/>; the client is torn down after the test.</summary>
        /// <param name="options">The finished options; changed in place.</param>
        /// <returns>The client.</returns>
        protected PubSubRealtimeClient BuildRealtime(ClientOptions options)
        {
            ApplyClientDefaults(options);
            var mobileDevice = new FakeMobileDevice();

            // The REST client is built through the constructor's factory argument so its device is set before
            // the realtime constructor can connect (AutoConnect).
            var client = new PubSubRealtimeClient(
                options,
                (restOptions, device) =>
                {
                    var rest = new PubSubHttpClient(restOptions, device);
                    rest.Device = LocalDevice.Create(mobileDevice: device);
                    return rest;
                },
                mobileDevice);
            DetachFromOsNetworkEvents(client);
            return Track(client);
        }

        private static void ApplyClientDefaults(ClientOptions options)
        {
            options.AutomaticNetworkStateMonitoring = false;
            if (options.Logger == null || ReferenceEquals(options.Logger, DefaultLogger.LoggerInstance))
            {
                options.Logger = InternalLogger.Create(options.LogLevel, options.LogHandler ?? new DefaultLoggerSink());
            }
        }

        private static string[] SplitKey(string apiKey)
        {
            var colon = apiKey == null ? -1 : apiKey.IndexOf(':');
            if (colon <= 0 || colon == apiKey.Length - 1)
            {
                throw new ArgumentException($"'{apiKey}' isn't a key of the form appId.keyId:secret", nameof(apiKey));
            }

            return new[] { apiKey.Substring(0, colon), apiKey.Substring(colon + 1) };
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        private void TargetSandbox(ClientOptions options)
        {
            if (options.Endpoint == null)
            {
                options.Endpoint = SandboxEndpoint;
            }

            if (!HasAuth(options))
            {
                options.Key = ApiKey;
            }
        }
    }
}
