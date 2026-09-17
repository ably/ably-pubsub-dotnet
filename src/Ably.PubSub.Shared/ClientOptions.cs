using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using IO.Ably.Transport;
using Newtonsoft.Json;

namespace IO.Ably
{
    /// <summary>
    /// Options: Ably library options for REST and Realtime APIs.
    /// </summary>
    public class ClientOptions : AuthOptions
    {
        private string _clientId;
        private string _realtimeHost;
        private string _restHost;
        private Func<DateTimeOffset> _nowFunc;
#if MSGPACK
        private bool _useBinaryProtocol;
#endif
        private string[] _fallbackHosts;

        /// <summary>
        /// If false, suppresses the automatic initiation of a connection when the library is instanced.
        /// Default: true.
        /// </summary>
        public bool AutoConnect { get; set; } = true;

        /// <summary>
        /// The id of the client represented by this instance. The clientId is relevant
        /// to presence operations, where the clientId is the principal identifier of the
        /// client in presence update messages. The clientId is also relevant to
        /// authentication; a token issued for a specific client may be used to authenticate
        /// the bearer of that token to the service.
        /// Default: null.
        /// </summary>
        public string ClientId
        {
            get => _clientId;

            set
            {
                if (value == "*")
                {
                    throw new InvalidOperationException("Wildcard clientIds are not support in ClientOptions");
                }

                _clientId = value;
            }
        }

        /// <summary>
        /// When a TokenParams object is provided, it will override
        /// the client library defaults described in TokenParams
        /// Spec: TO3j11.
        /// Default: null.
        /// </summary>
        public TokenParams DefaultTokenParams { get; set; }

        /// <summary>
        /// Used internally for getting clientId from options or DefaultTokenParams.
        /// </summary>
        /// <returns>clientId.</returns>
        internal string GetClientId()
        {
            if (ClientId.IsNotEmpty())
            {
                return ClientId;
            }

            return DefaultTokenParams?.ClientId;
        }

        /// <summary>
        /// If <c>false</c>, this disables the default behaviour whereby the library queues messages on a
        /// connection in the disconnected or connecting states. The default behaviour allows applications
        /// to submit messages immediately upon instancing the library, without having to wait for the
        /// connection to be established. Applications may use this option to disable that behaviour if they
        /// wish to have application-level control over the queueing under those conditions.
        /// Default: true.
        /// </summary>
        public bool QueueMessages { get; set; } = true;

        /// <summary>
        /// If <c>false</c>, prevents messages originating from this connection being echoed back on the same
        /// connection.
        /// Default: true.
        /// </summary>
        public bool EchoMessages { get; set; } = true;

        /// <summary>
        /// A connection recovery string, specified by a client when initializing the library
        /// with the intention of inheriting the state of an earlier connection. See the Ably
        /// Realtime API documentation for further information on connection state recovery. (RTN16i)
        /// Default: null.
        /// </summary>
        public string Recover { get; set; }

        /// <summary>
        /// Log level; controls the level of verbosity of log messages from the library.
        /// Default: Defaults.DefaultLogLevel. Which means the log level is set to Warning.
        /// </summary>
        public LogLevel LogLevel { get; set; } = Defaults.DefaultLogLevel;

        /// <summary>
        /// Log handler: allows the client to intercept log messages and handle them in a client-specific way.
        /// Default: null.
        /// </summary>
        [JsonIgnore]
        public ILoggerSink LogHandler { get; set; }

        /// <summary>
        /// For development environments only. Allows a non default host for the rest service.
        /// Default: null.
        /// </summary>
        public string RestHost
        {
            set => _restHost = value;
        }

        /// <summary>
        /// For development environments only. Allows a non default host for the realtime service.
        /// Default: null.
        /// </summary>
        public string RealtimeHost
        {
            set => _realtimeHost = value;
        }

        /// <summary>
        /// Gets or sets an array of custom Fallback hosts to be (optionally) used in place of the defaults.
        /// If an empty array is specified, then fallback host functionality is disabled.
        /// </summary>
        public string[] FallbackHosts
        {
            set => _fallbackHosts = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to use default FallbackHosts even when overriding
        /// environment or restHost/realtimeHost.
        /// It will be removed in the next version of the library.
        /// Default: false.
        /// </summary>
        [Obsolete("We will no longer support the FallbackHostsUseDefault in the library. This property will be removed in future versions")]
        public bool FallbackHostsUseDefault { get; set; }

        internal bool IsProductionEnvironment => Environment.IsEmpty() || Environment.Equals("production", StringComparison.OrdinalIgnoreCase);

        internal bool IsDefaultPort => Tls ? TlsPort == Defaults.TlsPort : Port == Defaults.Port;

        /// <summary>
        /// Used for getting default/provided RestHost.
        /// </summary>
        /// <returns>RestHost.</returns>
        public string FullRestHost()
        {
            if (_restHost.IsNotEmpty())
            {
                return _restHost;
            }

            if (!IsProductionEnvironment)
            {
                return $"{Environment}-{Defaults.RestHost}";
            }

            return Defaults.RestHost;
        }

        /// <summary>
        /// Used for getting default/provided RealtimeHost.
        /// </summary>
        /// <returns>RealtimeHost.</returns>
        public string FullRealtimeHost()
        {
            if (_realtimeHost.IsNotEmpty())
            {
                return _realtimeHost;
            }

            if (_restHost.IsNotEmpty())
            {
                Logger.Warning(
                    $@"restHost is set to {_restHost} but realtimeHost is not set,
                    so setting realtimeHost to {_restHost} too. If this is not what you want,
                    please set realtimeHost explicitly.");
                return _restHost;
            }

            if (!IsProductionEnvironment)
            {
                return $"{Environment}-{Defaults.RealtimeHost}";
            }

            return Defaults.RealtimeHost;
        }

        /// <summary>
        /// Used for getting default/provided FallbackHosts.
        /// </summary>
        /// <returns>FallbackHosts.</returns>
        public string[] GetFallbackHosts()
        {
#pragma warning disable 618
            if (FallbackHostsUseDefault)
#pragma warning restore 618
            {
                if (_fallbackHosts != null)
                {
                    const string msg = "fallbackHosts and fallbackHostsUseDefault cannot both be set";
                    throw new AblyException(new ErrorInfo(msg, ErrorCodes.BadRequest));
                }

                if (!IsDefaultPort)
                {
                    const string msg = "fallbackHostsUseDefault cannot be set when port or tlsPort are set";
                    throw new AblyException(new ErrorInfo(msg, ErrorCodes.BadRequest));
                }

                if (Environment.IsNotEmpty())
                {
                    Logger.Warning("Deprecated fallbackHostsUseDefault : There is no longer a need to set this when the environment option is also set since the library will now generate the correct fallback hosts using the environment option.");
                }

                Logger.Warning("Deprecated fallbackHostsUseDefault : fallbackHosts: Ably.Defaults.FALLBACK_HOSTS");
                return Defaults.FallbackHosts;
            }

            if (_fallbackHosts is null && _restHost.IsEmpty() && _realtimeHost.IsEmpty() && IsDefaultPort)
            {
                return IsProductionEnvironment
                    ? Defaults.FallbackHosts
                    : Defaults.GetEnvironmentFallbackHosts(Environment);
            }

            return _fallbackHosts ?? new string[] { };
        }

        /// <summary>
        /// For development environments only; allows a non-default Ably port to be specified.
        /// Default: 80.
        /// </summary>
        public int Port { get; set; } = Defaults.Port;

        /// <summary>
        /// Encrypted transport: if true, TLS will be used for all connections (whether REST/HTTP
        /// or Realtime WebSocket or Comet connections).
        /// Default: true.
        /// </summary>
        public bool Tls { get; set; } = true;

        /// <summary>
        /// Allows non-default Tls port to be specified.
        /// Default: 443.
        /// </summary>
        public int TlsPort { get; set; } = Defaults.TlsPort;

        /// <summary>
        /// If false, forces the library to use the JSON encoding for REST and Realtime operations,
        /// If true, the MsgPack binary format is used (if available in the current build
        /// Default: false.
        /// </summary>
        public bool UseBinaryProtocol
        {
#if MSGPACK
            get { return _useBinaryProtocol; }
#else
            get { return false; }
#endif

#if MSGPACK
            set { _useBinaryProtocol = value; }
#else
            set { _ = value; }
#endif
        }

        /// <summary>
        /// Number of seconds before the library will retry after a disconnect.
        /// Default: 15s.
        /// </summary>
        public TimeSpan DisconnectedRetryTimeout { get; set; } = Defaults.DisconnectedRetryTimeout;

        /// <summary>
        /// Number of seconds before the library will retry after reaching Suspended state.
        /// Default: 30s.
        /// </summary>
        public TimeSpan SuspendedRetryTimeout { get; set; } = Defaults.SuspendedRetryTimeout;

        /// <summary>
        /// Number of seconds before a channel will try to reattach itself after becoming suspended.
        /// Default: 15s.
        /// </summary>
        public TimeSpan ChannelRetryTimeout { get; set; } = Defaults.ChannelRetryTimeout;

        /// <summary>
        /// Timeout for opening an http request.
        /// Default: 4s.
        /// </summary>
        public TimeSpan HttpOpenTimeout { get; set; } = Defaults.MaxHttpOpenTimeout;

        /// <summary>
        /// Timeout for completing an http request.
        /// Default: 10s.
        /// </summary>
        public TimeSpan HttpRequestTimeout { get; set; } = Defaults.MaxHttpRequestTimeout;

        /// <summary>
        /// After a failed request to the default endpoint, followed by a successful request to a fallback endpoint),
        /// the period in milliseconds before HTTP requests are retried against the default endpoint.
        /// Default: 10 minutes.
        /// </summary>
        public TimeSpan FallbackRetryTimeout { get; set; } = Defaults.FallbackRetryTimeout;

        /// <summary>
        /// Maximum number of Http retries the library will attempt.
        /// Default: 3.
        /// </summary>
        public int HttpMaxRetryCount { get; set; } = Defaults.HttpMaxRetryCount;

        /// <summary>
        /// Duration for which the library will retry an http request.
        /// Default: 15s.
        /// </summary>
        public TimeSpan HttpMaxRetryDuration { get; set; } = Defaults.HttpMaxRetryDuration;

        /// <summary>
        /// Provides Channels Setting for all Channels created. For more information see <see cref="ChannelOptions"/>.
        /// </summary>
        public ChannelOptions ChannelDefaults { get; internal set; } = new ChannelOptions();

        /// <summary>
        /// For development; allows to specify a non production environment.
        /// Default: null.
        /// </summary>
        public string Environment { get; set; }

        /// <summary>
        /// Allows the user to override the Websocket Transport Factory and provide their own implementation.
        /// </summary>
        [JsonIgnore]
        public ITransportFactory TransportFactory { get; set; }

        /// <summary>
        /// Switched on IdempotentRest publishing. Currently switched off by default in libraries with version less than 1.2.
        /// Default before 1.2: false.
        /// Default from 1.2: true.
        /// </summary>
        public bool IdempotentRestPublishing { get; set; } = true;

        /// <summary>
        /// Additional parameters to be sent in the querystring when initiating a realtime connection.
        /// </summary>
        public Dictionary<string, object> TransportParams { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Useful where you want to execute callbacks on the main/UI thread instead of background thread in UI based apps.
        /// Allows developers to capture their Current SynchronizationContext and trigger handlers and emitters on the same.
        /// Default: false.
        /// </summary>
        [Obsolete("Use CustomContext property instead, CaptureCurrentSynchronizationContext property will be removed in future versions")]
        public bool CaptureCurrentSynchronizationContext { get; set; } = false;

        /// <summary>
        /// Useful where you want to execute callbacks on the main/UI thread instead of background thread in UI based apps.
        /// Allows developers to provide their Thread Synchronization Context to be used when triggering handlers and emitters.
        /// This is majorly useful in unity, xamarin and MAUI apps.
        /// Default: null.
        /// </summary>
        public SynchronizationContext CustomContext { get; set; }

        /// <summary>
        /// Allows developers to control Automatic network state monitoring. When set to `false` the library will not subscribe to
        /// `NetworkChange.NetworkAvailabilityChanged` events. Developers can manually notify the Connection for changes by calling
        /// `Connection.NotifyOperatingSystemNetworkState(NetworkState.Online or NetworkState.Offline)`.
        /// Unity and some Mono environments don't have `NetworkChange.NetworkAvailabilityChanged` event implemented and
        /// which used to prevent the library from initialising.
        /// Default: true.
        /// </summary>
#if NETSTANDARD2_0_OR_GREATER && UNITY_PACKAGE
        public bool AutomaticNetworkStateMonitoring { get; set; } = false;
#else
        public bool AutomaticNetworkStateMonitoring { get; set; } = true;
#endif

        /// <summary>
        /// Allows developers to control how often (in milliseconds) the heartbeat is checked to determine if the server
        /// connection has been lost.
        /// Defaults: 1000.
        /// </summary>
        public int HeartbeatMonitorDelay { get; set; } = 1000;

        /// <summary>
        /// If enabled, every REST request to Ably includes a `request_id` query string parameter.
        /// This request ID remains the same if a request is retried to a fallback host.
        /// </summary>
        public bool AddRequestIds { get; set; }

        /// <summary>
        /// It tells Ably push REST requests to fully wait for all their effects before responding.
        /// Default: false.
        /// </summary>
        public bool PushAdminFullWait { get; set; }

        /// <summary>
        /// Map of agents that will be appended to the internal ably agent headers.
        /// This should only be used by Ably-authored SDKs.
        /// If you need to use this then you have to add the agent to the agents.json file:
        /// https://github.com/ably/ably-common/blob/main/protocol/agents.json
        /// The keys represent agent names and its corresponding values represent agent versions.
        /// RSC7d6.
        /// </summary>
        public Dictionary<string, string> Agents { get; set; }

        /// <summary>
        /// Allows injection of an externally managed HttpClient instance.
        /// When provided, the library will use this HttpClient instead of creating its own.
        /// This enables proper HttpClient lifecycle management and connection pooling.
        /// The injected HttpClient should be configured with appropriate timeouts and handlers.
        /// Default: null (library creates its own HttpClient).
        /// </summary>
        [JsonIgnore]
        public HttpClient HttpClient { get; set; }

        [JsonIgnore]
        internal Func<DateTimeOffset> NowFunc
        {
            get => _nowFunc ?? (_nowFunc = Defaults.NowFunc());
            set => _nowFunc = value;
        }

        [JsonIgnore]
        internal ILogger Logger { get; set; } = DefaultLogger.LoggerInstance;

        internal bool SkipInternetCheck { get; set; }

        internal TimeSpan RealtimeRequestTimeout { get; set; } = Defaults.RealtimeRequestTimeout;

        /// <summary>
        /// Default constructor for ClientOptions.
        /// </summary>
        public ClientOptions()
        {
        }

        /// <summary>
        /// Construct ClientOptions class and set the Key
        /// It automatically parses the key to ensure the correct format is used and sets the KeyId and KeyValue properties.
        /// </summary>
        /// <param name="key">Ably authentication key.</param>
        public ClientOptions(string key)
            : base(key)
        {
        }
    }
}
