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
    /// <c>extract_key_secret</c>, <c>generate_jwt</c>, <c>request_token_from_sandbox</c>), wall-clock state
    /// waits, and teardown that closes every realtime client it built.
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

        /// <summary>
        /// The specs' <c>random_id()</c> / <c>base64(random_bytes(n))</c>, url-safe (base64url, unpadded) so it
        /// is valid in a channel name.
        /// </summary>
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
        public static string UniqueChannelName(string prefix) => $"test-{prefix}-{RandomId()}";

        /// <summary>The specs' <c>extract_key_name(api_key)</c>: <c>appId.keyId</c>.</summary>
        public static string ExtractKeyName(string apiKey) => SplitKey(apiKey)[0];

        /// <summary>The specs' <c>extract_key_secret(api_key)</c>.</summary>
        public static string ExtractKeySecret(string apiKey) => SplitKey(apiKey)[1];

        /// <summary>
        /// The specs' <c>generate_jwt(key_name, key_secret, ttl, client_id, capability, expires_at)</c>: an Ably JWT
        /// signed locally with HS256 (header <c>kid</c> = the key name; claims <c>iat</c>, <c>exp</c>, and <c>x-ably-
        /// clientId</c> / <c>x-ably-capability</c> when given).
        /// </summary>
        /// <param name="expiresAt">
        /// The specs' <c>expires_at</c>: when set, <c>exp</c> is this instant and <c>iat</c> is <paramref name="ttl"/>
        /// (default 1 h) before it, so a past value gives an expired token (40142) rather than an invalid one. When
        /// null, <c>iat</c> is now and <c>exp</c> is <c>iat + ttl</c>.
        /// </param>
        public static string GenerateJwt(
            string keyName,
            string keySecret,
            TimeSpan? ttl = null,
            string clientId = null,
            string capability = null,
            DateTimeOffset? expiresAt = null)
        {
            var lifetime = ttl ?? TimeSpan.FromHours(1);
            var issuedAt = expiresAt.HasValue ? expiresAt.Value - lifetime : DateTimeOffset.UtcNow;
            var expires = expiresAt ?? issuedAt.Add(lifetime);

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
        /// The specs' <c>request_token_from_sandbox(api_key, params)</c>: a real token from the sandbox (<c>POST
        /// /keys/{keyName}/requestToken</c>), requested by a separate REST client that talks straight to the
        /// sandbox over TLS with Basic auth.
        /// </summary>
        public Task<TokenDetails> RequestTokenFromSandbox(string apiKey = null, TokenParams tokenParams = null)
        {
            var direct = BuildRest(new ClientOptions { Key = apiKey ?? ApiKey, Endpoint = SandboxEndpoint });
            return direct.Auth.RequestTokenAsync(tokenParams);
        }

        /// <summary>
        /// An <c>authCallback</c> that answers every token request with <see cref="RequestTokenFromSandbox"/>
        /// (the callback's token params are passed on), so the token is a real, renewable one.
        /// </summary>
        /// <param name="onInvoked">Called on every invocation, before the token is requested (the specs' <c>auth_callback_count++</c>).</param>
        public Func<TokenParams, Task<object>> SandboxTokenCallback(string apiKey = null, Action<TokenParams> onInvoked = null)
        {
            var key = apiKey ?? ApiKey;
            return async tokenParams =>
            {
                onInvoked?.Invoke(tokenParams);
                return await RequestTokenFromSandbox(key, tokenParams).ConfigureAwait(false);
            };
        }

        /// <summary>The specs' <c>Rest(options: …)</c> against the sandbox, wired as described in Infra/README.md.</summary>
        public PubSubHttpClient Rest(ClientOptions options = null)
        {
            options = options ?? new ClientOptions();
            TargetSandbox(options);
            return BuildRest(options);
        }

        /// <summary>
        /// The specs' <c>Realtime(options: …)</c> against the sandbox, wired as described in Infra/README.md, and
        /// torn down after the test.
        /// </summary>
        /// <param name="options">The spec's options; changed in place.</param>
        public PubSubRealtimeClient Realtime(ClientOptions options = null)
        {
            options = options ?? new ClientOptions();
            TargetSandbox(options);
            return BuildRealtime(options);
        }

        /// <summary>
        /// The specs' <c>AWAIT_STATE connection.state == state</c>, bounded by <see cref="StateTimeout"/> unless
        /// the spec gives a timeout.
        /// </summary>
        public Task AwaitConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => UtsWait.AwaitConnectionState(connection, state, timeout ?? StateTimeout, callerFilePath, callerLineNumber);

        /// <summary>
        /// The specs' <c>AWAIT_STATE channel.state == state</c>, bounded by <see cref="StateTimeout"/> unless the
        /// spec gives a timeout.
        /// </summary>
        public Task AwaitChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => UtsWait.AwaitChannelState(channel, state, timeout ?? StateTimeout, callerFilePath, callerLineNumber);

        /// <summary>Fails the test, attributed to the caller, unless an SDK operation's <see cref="Result"/> succeeded.</summary>
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
        /// <returns>Whether any of Key, Token, TokenDetails, AuthCallback or AuthUrl is set.</returns>
        internal static bool HasAuth(ClientOptions options)
            => !string.IsNullOrEmpty(options.Key)
                || !string.IsNullOrEmpty(options.Token)
                || options.TokenDetails != null
                || options.AuthCallback != null
                || options.AuthUrl != null;

        /// <summary>
        /// Builds a REST client from options that already say where to go and how to authenticate, adding only
        /// the per-client logger, device and network-monitoring defaults.
        /// </summary>
        protected PubSubHttpClient BuildRest(ClientOptions options)
        {
            ApplyClientDefaults(options);
            var mobileDevice = new FakeMobileDevice();
            var client = new PubSubHttpClient(options, mobileDevice);
            client.Device = LocalDevice.Create(mobileDevice: mobileDevice);
            return client;
        }

        /// <summary>The realtime counterpart of <see cref="BuildRest"/>; the client is torn down after the test.</summary>
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
