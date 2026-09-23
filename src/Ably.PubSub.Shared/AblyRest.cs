using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IO.Ably.MessageEncoders;
using IO.Ably.Push;
using IO.Ably.Rest;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>Client for the Ably rest API.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "Needed properties to be internal for testing.")]
    public sealed class AblyRest : IRestClient
    {
        private readonly object _deviceLock = new object();

        internal Func<AblyRequest, Task<AblyResponse>> ExecuteHttpRequest;
        private LocalDevice _device;

        /// <summary>
        /// Initializes a new instance of the <see cref="AblyRest"/> class using an api key.
        /// Not public: application code obtains a client from <c>PubSubServer.CreateHttpClient</c>,
        /// which stamps the device/server classification the platform and MAU-based billing depend
        /// on. The door assemblies (and this SDK's own tests) reach this constructor via
        /// <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="apiKey">Full api key.</param>
        internal AblyRest(string apiKey)
            : this(new ClientOptions(apiKey))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AblyRest"/> class from an
        /// Action{ClientOptions}. Not public: application code obtains a client from
        /// <c>PubSubServer.CreateHttpClient</c>, which stamps the device/server classification the
        /// platform and MAU-based billing depend on. The door assemblies (and this SDK's own tests)
        /// reach this constructor via <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="init">Action delegate which receives a empty options object.</param>
        internal AblyRest(Action<ClientOptions> init)
        {
            Options = new ClientOptions();
            init(Options);
            InitializeAbly(IoC.MobileDevice);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AblyRest"/> class with a custom set of
        /// options. Not public: application code obtains a client from
        /// <c>PubSubServer.CreateHttpClient</c>, which stamps the device/server classification the
        /// platform and MAU-based billing depend on. The door assemblies (and this SDK's own tests)
        /// reach this constructor via <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="clientOptions">instance of clientOptions.</param>
        internal AblyRest(ClientOptions clientOptions)
            : this(clientOptions, IoC.MobileDevice)
        {
        }

        internal AblyRest(ClientOptions clientOptions, IMobileDevice mobileDevice)
        {
            Options = clientOptions;
            InitializeAbly(mobileDevice);
        }

        internal IMobileDevice MobileDevice { get; private set; }

        internal AblyHttpClient HttpClient { get; private set; }

        internal MessageHandler MessageHandler { get; private set; }

        internal void SetRealtimeFallbackHost(string host)
            => HttpClient.RealtimeConnectedFallbackHost = host;

        internal void ClearRealtimeFallbackHost()
            => HttpClient.RealtimeConnectedFallbackHost = null;

        internal AblyAuth AblyAuth { get; private set; }

        /// <summary>
        /// A collection of Channels associated with an Ably instance.
        /// </summary>
        public RestChannels Channels { get; private set; }

        /// <summary>
        /// Authentication methods.
        /// </summary>
        public IAblyAuth Auth => AblyAuth;

        /// <summary>
        /// Expose Push Admin Rest APIs.
        /// Rest API documentation: https://ably.com/docs/rest-api#push.
        /// </summary>
        public PushRest Push { get; private set; }

        /// <summary>
        /// The local device instance represents the current state of the device in respect of it being a target for push notifications.
        /// </summary>
        public LocalDevice Device
        {
            get
            {
                if (_device != null)
                {
                    return _device;
                }

                if (MobileDevice != null)
                {
                    lock (_deviceLock)
                    {
                        return LocalDevice.GetInstance(MobileDevice, Auth.ClientId);
                    }
                }

                return null;
            }
            internal set => _device = value; // The setting is only for testing purposes
        }

        internal Protocol Protocol => Options.UseBinaryProtocol == false ? Protocol.Json : Defaults.Protocol;

        internal ClientOptions Options { get; }

        internal ILogger Logger { get; set; } = DefaultLogger.LoggerInstance;

        /// <summary>Initializes the rest client and validates the passed in options.</summary>
        private void InitializeAbly(IMobileDevice mobileDevice)
        {
            if (Options == null)
            {
                Logger.Error("No options provider to Ably rest");
                throw new AblyException("Invalid options");
            }

            if (Options.Logger != null)
            {
                Logger = Options.Logger;
            }

            Logger.LogLevel = Options.LogLevel;

            if (Options.LogHandler != null)
            {
                Logger.LoggerSink = Options.LogHandler;
            }

            Logger.Debug("Protocol set to: " + Protocol);

            MessageHandler = new MessageHandler(Logger, Protocol);

            HttpClient = new AblyHttpClient(new AblyHttpOptions(Options));
            ExecuteHttpRequest = HttpClient.Execute;
            AblyAuth = new AblyAuth(Options, this);
            Channels = new RestChannels(this, mobileDevice);
            Push = new PushRest(this, Logger);
            MobileDevice = mobileDevice;
            AblyAuth.OnClientIdChanged = OnAuthClientIdChanged;
        }

        private void OnAuthClientIdChanged((string oldClientId, string newClientId) clientIdArgs)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"ClientId changed from '{clientIdArgs.oldClientId}' to '{clientIdArgs.newClientId}'");
            }

            // If the global LocalDevice instance hasn't been initialized we don't need to worry about setting the client id
            // as it will pick up the correct one when it is requested for the first time.
            if (LocalDevice.IsLocalDeviceInitialized)
            {
                Device.UpdateClientId(clientIdArgs.newClientId, MobileDevice);
            }
        }

        internal async Task<AblyResponse> ExecuteRequest(AblyRequest request)
        {
            Logger.Debug("Sending {0} request to {1}", request.Method, request.Url);
            string requestId = null;
            if (Options.AddRequestIds)
            {
                requestId = Guid.NewGuid().ToByteArray().ToBase64();
                var dict = new Dictionary<string, string> { { "request_id", requestId } };
                request.AddHeaders(dict);
            }

            Logger.Debug(WrapWithRequestId($"Sending {request.Method} request to {request.Url}"));

            if (request.SkipAuthentication == false)
            {
                await AblyAuth.AddAuthHeader(request);
            }

            try
            {
                MessageHandler.SetRequestBody(request);
                return await ExecuteHttpRequest(request);
            }
            catch (AblyException ex)
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug(WrapWithRequestId("Error Executing request. Message: " + ex.Message));
                }

                if (ex.ErrorInfo.IsUnAuthorizedError
                    && ex.ErrorInfo.IsTokenError)
                {
                    if (AblyAuth.TokenRenewable == false)
                    {
                        throw new AblyException(ErrorInfo.NonRenewableToken, ex); // RSA4a2
                    }

                    if (Logger.IsDebug)
                    {
                        Logger.Debug(WrapWithRequestId("Handling UnAuthorized Error, attempting to Re-authorize and repeat request."));
                    }

                    try
                    {
                        await AblyAuth.AuthorizeAsync();
                        await AblyAuth.AddAuthHeader(request);
                        return await ExecuteHttpRequest(request);
                    }
                    catch (AblyException ex2)
                    {
                        throw new AblyException(ex2.ErrorInfo, ex);
                    }
                }

                throw;
            }
            catch (Exception ex)
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug(WrapWithRequestId("Error Executing request. Message: " + ex.Message));
                }

                throw new AblyException(ex);
            }

            string WrapWithRequestId(string message) => requestId != null ? $"RequestId {requestId} : {message}" : message;
        }

        internal async Task<T> ExecuteRequest<T>(AblyRequest request)
            where T : class
        {
            var response = await ExecuteRequest(request);
            if (Logger.IsDebug)
            {
                Logger.Debug("Response received. Status: " + response.StatusCode);
                Logger.Debug("Content type: " + response.ContentType);
                Logger.Debug("Encoding: " + response.Encoding);
                if (response.Body != null)
                {
                    Logger.Debug("Raw response (base64):" + response.Body.ToBase64());
                }
            }

            return MessageHandler.ParseResponse<T>(request, response);
        }

        internal async Task<PaginatedResult<T>> ExecutePaginatedRequest<T>(AblyRequest request, Func<PaginatedRequestParams, Task<PaginatedResult<T>>> executeDataQueryRequest)
            where T : class
        {
            var response = await ExecuteRequest(request);
            if (Logger.IsDebug)
            {
                Logger.Debug("Response received. Status: " + response.StatusCode);
                Logger.Debug("Content type: " + response.ContentType);
                Logger.Debug("Encoding: " + response.Encoding);
                if (response.Body != null)
                {
                    Logger.Debug("Raw response (base64):" + response.Body.ToBase64());
                }
            }

            return MessageHandler.ParsePaginatedResponse(request, response, executeDataQueryRequest);
        }

        internal async Task<HttpPaginatedResponse> ExecuteHttpPaginatedRequest(AblyRequest request, PaginatedRequestParams requestParams, Func<PaginatedRequestParams, Task<HttpPaginatedResponse>> executeDataQueryRequest)
        {
            var response = await ExecuteRequest(request);

            if (Logger.IsDebug)
            {
                Logger.Debug("Response received. Status: " + response.StatusCode);
                Logger.Debug("Content type: " + response.ContentType);
                Logger.Debug("Encoding: " + response.Encoding);
                if (response.Body != null)
                {
                    Logger.Debug("Raw response (base64):" + response.Body.ToBase64());
                }
            }

            return MessageHandler.ParseHttpPaginatedResponse(request, response, requestParams, executeDataQueryRequest);
        }

        internal async Task<HttpPaginatedResponse> HttpPaginatedRequestInternal(PaginatedRequestParams requestParams)
        {
            var request = CreateRequest(requestParams.Path, requestParams.HttpMethod);
            request.NoExceptionOnHttpError = true;
            request.AddQueryParameters(requestParams.ExtraParameters);
            request.AddHeaders(requestParams.Headers);
            if (requestParams.Body != null)
            {
                request.PostData = requestParams.Body;
            }

            return await ExecuteHttpPaginatedRequest(request, requestParams, HttpPaginatedRequestInternal);
        }

        /// <summary>
        /// Make a generic HTTP request against an endpoint representing a collection
        /// of some type; this is to provide a forward compatibility path for new APIs.
        /// </summary>
        /// <param name="method">http method.</param>
        /// <param name="path">the path component of the resource URI.</param>
        /// <param name="requestParams">(optional; may be null): any parameters to send with the request; see API-specific documentation.</param>
        /// <param name="body">(optional; may be null): a json string RequestBody. It will be sent as a json object.</param>
        /// <param name="headers">(optional; may be null): any additional headers to send; see API-specific documentation.</param>
        /// <returns>a page of results.</returns>
        public async Task<HttpPaginatedResponse> RequestV2(string method, string path, Dictionary<string, string> requestParams = null, string body = null, Dictionary<string, string> headers = null)
        {
            var httpMethod = new HttpMethod(method);
            JToken requestBody = null;
            if (body != null)
            {
                requestBody = JToken.Parse(body);
            }

            return await Request(httpMethod, path, requestParams, requestBody, headers);
        }

        /// <summary>
        /// Make a generic HTTP request against an endpoint representing a collection
        /// of some type; this is to provide a forward compatibility path for new APIs.
        /// </summary>
        /// <param name="method">http method.</param>
        /// <param name="path">the path component of the resource URI.</param>
        /// <param name="requestParams">(optional; may be null): any parameters to send with the request; see API-specific documentation.</param>
        /// <param name="body">(optional; may be null): an instance of RequestBody. It will be sent as a json object.</param>
        /// <param name="headers">(optional; may be null): any additional headers to send; see API-specific documentation.</param>
        /// <returns>a page of results.</returns>
        public async Task<HttpPaginatedResponse> Request(HttpMethod method, string path, Dictionary<string, string> requestParams = null, JToken body = null, Dictionary<string, string> headers = null)
        {
            var p = new PaginatedRequestParams
            {
                Headers = headers,
                ExtraParameters = requestParams,
                Body = body,
                HttpMethod = method,
                Path = path,
            };

            return await HttpPaginatedRequestInternal(p);
        }

        /// <summary>Retrieves the ably service time.</summary>
        /// <returns>server time as DateTimeOffset.</returns>
        public async Task<DateTimeOffset> TimeAsync()
        {
            AblyRequest request = CreateGetRequest("/time");
            request.SkipAuthentication = true;
            List<long> response = await ExecuteRequest<List<long>>(request);
            return response.First().FromUnixTimeInMilliseconds();
        }

        /// <summary>
        /// Retrieves the stats for the application. Passed default <see cref="StatsRequestParams"/> for the request.
        /// </summary>
        /// <returns>returns PaginatedResult of Stats.</returns>
        public Task<PaginatedResult<Stats>> StatsAsync()
        {
            return StatsAsync(new StatsRequestParams());
        }

        /// <summary>
        /// Retrieves the stats for the application using a more specific stats query. Check <see cref="StatsRequestParams"/> for more information.
        /// </summary>
        /// <param name="query">stats query.</param>
        /// <returns>returns a PaginatedResult of Stats.</returns>
        public Task<PaginatedResult<Stats>> StatsAsync(StatsRequestParams query)
        {
            return StatsAsync(query as PaginatedRequestParams);
        }

        /// <summary>
        /// Retrieves the stats for the application based on a custom query. It should be used with <see cref="PaginatedRequestParams"/>.
        /// It is mainly because of the way a PaginatedResource defines its queries. For retrieving Stats with special parameters use <see cref="StatsAsync(StatsRequestParams)"/>.
        /// </summary>
        /// <example>
        /// var client = new AblyRest("validkey");
        /// var stats = client..StatsAsync();
        /// var nextPage = client..StatsAsync(stats.NextQuery);.
        /// </example>
        /// <param name="query"><see cref="PaginatedRequestParams"/> and <see cref="StatsRequestParams"/>.</param>
        /// <returns>returns a PaginatedResult of Stats.</returns>
        public Task<PaginatedResult<Stats>> StatsAsync(PaginatedRequestParams query)
        {
            query.Validate();

            var request = CreateGetRequest("/stats");

            request.AddQueryParameters(query.GetParameters());

            return ExecutePaginatedRequest(request, StatsAsync);
        }

        internal AblyRequest CreateRequest(string path, HttpMethod method, ChannelOptions options = null)
        {
            return new AblyRequest(path, method, Protocol) { ChannelOptions = options };
        }

        internal AblyRequest CreateGetRequest(string path, ChannelOptions options = null)
        {
            return new AblyRequest(path, HttpMethod.Get, Protocol) { ChannelOptions = options };
        }

        internal AblyRequest CreatePostRequest(string path, ChannelOptions options = null)
        {
            return new AblyRequest(path, HttpMethod.Post, Protocol) { ChannelOptions = options };
        }

        /// <summary>
        /// Makes an Http request to check whether there is connectivity to ably.
        /// If Option.SkipInternetCheck is set to true, the method always returns 'true'.
        /// </summary>
        /// <returns>returns whether there is internet connectivity.</returns>
        public async Task<bool> CanConnectToAbly()
        {
            if (Options.SkipInternetCheck)
            {
                return true;
            }

            try
            {
                // We don't want to retry the internet check url.
                var request = new AblyRequest(Defaults.InternetCheckUrl, HttpMethod.Get) { SkipRetry = true };
                var response = await ExecuteHttpRequest(request).TimeoutAfter(Defaults.MaxHttpOpenTimeout, AblyResponse.EmptyResponse);
                var success = response.TextResponse.SafeTrim().EqualsTo(Defaults.InternetCheckOkMessage);
                if (success == false)
                {
                    Logger.Warning("Cannot get a valid internet connection. Response: " + response.TextResponse);
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.Error("Error accessing ably internet check url. Internet is down!", ex);
                return false;
            }
        }

        /// <summary>
        /// Sync version of StatsAsync.
        /// </summary>
        /// <returns>returns PaginatedResult of Stats.</returns>
        public PaginatedResult<Stats> Stats()
        {
            return AsyncHelper.RunSync(StatsAsync);
        }

        /// <summary>
        /// Sync version of StatsAsync.
        /// </summary>
        /// <param name="query">stats <see cref="StatsRequestParams"/>.</param>
        /// <returns>returns PaginatedResult of Stats.</returns>
        public PaginatedResult<Stats> Stats(StatsRequestParams query)
        {
            return AsyncHelper.RunSync(() => StatsAsync(query));
        }

        /// <summary>
        /// Sync method for getting the current server time.
        /// </summary>
        /// <returns>DateTimeOffset of the current server time.</returns>
        public DateTimeOffset Time()
        {
            return AsyncHelper.RunSync(TimeAsync);
        }
    }
}
