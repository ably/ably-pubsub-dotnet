using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace IO.Ably
{
    internal class AblyHttpClient : IAblyHttpClient
    {
        private readonly Random _random = new Random();
        private string _realtimeConnectedFallbackHost;

        internal AblyHttpClient(AblyHttpOptions options)
        {
            Now = options.NowFunc;
            Logger = options.Logger ?? DefaultLogger.LoggerInstance;
            Options = options;
            CreateInternalHttpClient(options.HttpRequestTimeout, options.HttpClient);
            SendAsync = InternalSendAsync;
        }

        internal Func<HttpRequestMessage, Task<HttpResponseMessage>> SendAsync { get; set; }

        internal AblyHttpOptions Options { get; }

        internal HttpClient Client { get; set; }

        internal string PreferredHost { get; private set; }

        internal string RealtimeConnectedFallbackHost
        {
            get => _realtimeConnectedFallbackHost;
            set
            {
                _realtimeConnectedFallbackHost = value;
                if (value.IsNotEmpty())
                {
                    // The realtime connection has set a custom host. We try and stick with it.
                    PreferredHost = null;
                    FallbackHostUsedFrom = null;
                }
            }
        }

        private Func<DateTimeOffset> Now { get; set; }

        private ILogger Logger { get; set; }

        private DateTimeOffset? FallbackHostUsedFrom { get; set; }

        internal void SetPreferredHost(string currentHost)
        {
            // If we are retrying the default host we need to clear the preferred one
            // and usedFrom timestamp
            if (currentHost == Options.Host)
            {
                PreferredHost = null;
                FallbackHostUsedFrom = null;
            }
            else
            {
                FallbackHostUsedFrom = Now();
                PreferredHost = currentHost;
            }
        }

        internal void CreateInternalHttpClient(TimeSpan timeout, HttpClient externalHttpClient = null)
        {
            Client = externalHttpClient ?? new HttpClient();

            // RSC7a
            if (!Client.DefaultRequestHeaders.Contains("X-Ably-Version"))
            {
                Client.DefaultRequestHeaders.Add("X-Ably-Version", Defaults.ProtocolVersion);
            }

            // RSC7d
            if (!Client.DefaultRequestHeaders.Contains(Agent.AblyAgentHeader))
            {
                Client.DefaultRequestHeaders.Add(Agent.AblyAgentHeader, Agent.AblyAgentIdentifier(Options.Agents));
            }

            Client.Timeout = timeout;
        }

        internal async Task<HttpResponseMessage> InternalSendAsync(HttpRequestMessage message)
        {
            return await Client.SendAsync(message, HttpCompletionOption.ResponseContentRead);
        }

        public async Task<AblyResponse> Execute(AblyRequest request)
        {
            var fallbackHosts = GetFallbackHosts();

            int currentTry = 0;
            var startTime = Now();

            var maxNumberOfRetries = Options.HttpMaxRetryCount;
            var host = GetHost();

            request.Headers.TryGetValue("request_id", out var requestId);
            do
            {
                EnsureMaxRetryDurationNotExceeded();

                Logger.Debug(
                    WrapWithRequestId($"Executing request. Host: {host}. Request: {request.Url}. {(currentTry > 0 ? $"try {currentTry}" : string.Empty)}"));

                try
                {
                    var response = await MakeRequest(host);

                    if (response.Success)
                    {
                        return response.AblyResponse;
                    }

                    if (request.SkipRetry)
                    {
                        break;
                    }

                    if (response.CanRetry)
                    {
                        currentTry++;
                        Logger.Warning(WrapWithRequestId("Failed response. " + response.GetFailedMessage() + ". Retrying..."));
                        var (success, newHost) = HandleHostChangeForRetryableFailure(currentTry);
                        if (success)
                        {
                            Logger.Debug(WrapWithRequestId($"Retrying using host: {newHost}"));
                            host = newHost;
                            continue;
                        }
                    }

                    // We only return the response if there is no exception
                    if (request.NoExceptionOnHttpError && response.Exception == null)
                    {
                        return response.AblyResponse;
                    }

                    // there is a case where the user has disabled fallback and there is no exception.
                    // in that case we need to create a new AblyException
                    throw response.Exception ?? AblyException.FromResponse(response.AblyResponse);
                }
                catch (AblyException ex)
                {
                    var errInfo = ex.ErrorInfo;
                    errInfo.Message = WrapWithRequestId("Error executing request. " + errInfo.Message);
                    throw new AblyException(errInfo, ex);
                }
                catch (Exception ex)
                {
                    // TODO: Sentry logging here
                    throw new AblyException(new ErrorInfo(WrapWithRequestId("Error executing request. " + ex.Message), ErrorCodes.InternalError), ex);
                }
            }
            while (currentTry <= maxNumberOfRetries); // 1 primary host and remaining fallback hosts

            throw new AblyException(new ErrorInfo(WrapWithRequestId("Error executing request, exceeded max no. of retries"), ErrorCodes.InternalError));

            List<string> GetFallbackHosts()
            {
                var results = Options.FallbackHosts.ToList();
                if (PreferredHost.IsNotEmpty())
                {
                    // The custom host is a fallback host currently in use by the Realtime client.
                    // We need to remove it from the fallback hosts
                    results.Remove(PreferredHost);
                }

                return results;
            }

            string GetHost()
            {
                // If there is a fallback host and it has expired then clear it and return the default host
                if (FallbackHostUsedFrom.HasValue && FallbackHostUsedFrom.Value.Add(Options.FallbackRetryTimeOut) < Now())
                {
                    PreferredHost = null;
                    FallbackHostUsedFrom = null;
                    return GetDefaultHost();
                }

                // otherwise if there is a preferred host then return it otherwise return the default host
                var hostToUse = PreferredHost.IsNotEmpty() ? PreferredHost : GetDefaultHost();
                return hostToUse;
            }

            string GetDefaultHost()
            {
                // First try the realtime host to which there is a websocket connection
                if (RealtimeConnectedFallbackHost.IsNotEmpty())
                {
                    return RealtimeConnectedFallbackHost;
                }

                return Options.Host;
            }

            // Tries to make a request
            // If it fails it will return
            async Task<HttpResponseWrapper> MakeRequest(string requestHost)
            {
                try
                {
                    var message = GetRequestMessage(request, requestHost);
                    await LogMessage(message);
                    var response = await SendAsync(message).ConfigureAwait(false);
                    var ablyResponse = await GetAblyResponse(response);
                    LogResponse(ablyResponse, request.Url);

                    if (response.IsSuccessStatusCode)
                    {
                        return HttpResponseWrapper.FromSuccess(ablyResponse);
                    }

                    if (IsRetryableResponse(response) || request.NoExceptionOnHttpError)
                    {
                        return HttpResponseWrapper.FromResponse(ablyResponse, false, IsRetryableResponse(response));
                    }

                    throw AblyException.FromResponse(ablyResponse);
                }
                catch (HttpRequestException ex)
                {
                    if (IsRetryableError(ex))
                    {
                        return HttpResponseWrapper.FromError(ex, true);
                    }

                    var reason = new StringBuilder(ex.Message);
                    var innerEx = ex.InnerException;
                    while (innerEx != null)
                    {
                        reason.Append(" " + innerEx.Message);
                        innerEx = innerEx.InnerException;
                    }

                    throw new AblyException(new ErrorInfo(reason.ToString(), ErrorCodes.InternalError), ex);
                }
                catch (TaskCanceledException ex)
                {
                    if (IsRetryableError(ex))
                    {
                        return HttpResponseWrapper.FromError(ex, IsRetryableError(ex));
                    }

                    if (ex.CancellationToken.IsCancellationRequested == false)
                    {
                        throw new AblyException(
                            new ErrorInfo(WrapWithRequestId("Error executing request. Request timed out."), ErrorCodes.InternalError), ex);
                    }

                    throw new AblyException(new ErrorInfo(WrapWithRequestId("Error executing request"), ErrorCodes.InternalError), ex);
                }
            }

            (bool success, string host) HandleHostChangeForRetryableFailure(int attempt)
            {
                if (fallbackHosts.Count == 0)
                {
                    Logger.Debug(WrapWithRequestId("No more hosts left to retry. Cannot assign a new fallback host."));
                    return (false, null);
                }

                bool isFirstTryForRequest = attempt == 1;

                // If there is a Preferred fallback host already set
                // and it failed we should try the RealtimeConnected fallback host first
                // and then the default host
                var nextHost = isFirstTryForRequest && PreferredHost.IsNotEmpty()
                        ? RealtimeConnectedFallbackHost ?? Options.Host
                        : GetNextFallbackHost();

                SetPreferredHost(nextHost);
                return (nextHost.IsNotEmpty(), nextHost);
            }

            string GetNextFallbackHost()
            {
                if (fallbackHosts.Count == 0)
                {
                    return null;
                }

                var fallbackHost = fallbackHosts[_random.Next() % fallbackHosts.Count];
                fallbackHosts.Remove(fallbackHost);
                return fallbackHost;
            }

            void EnsureMaxRetryDurationNotExceeded()
            {
                if ((Now() - startTime).TotalSeconds >= Options.HttpMaxRetryDuration.TotalSeconds)
                {
                    Logger.Error(WrapWithRequestId($"Cumulative retry timeout of {Options.HttpMaxRetryDuration.TotalSeconds}s was exceeded"));
                    throw new AblyException(
                        new ErrorInfo(WrapWithRequestId($"Cumulative retry timeout of {Options.HttpMaxRetryDuration.TotalSeconds}s was exceeded. The value is controlled by `ClientOptions.HttpMaxRetryDuration`."), ErrorCodes.InternalError));
                }
            }

            string WrapWithRequestId(string message) => requestId != null ? $"RequestId {requestId} : {message}" : message;
        }

        private void LogResponse(AblyResponse ablyResponse, string url)
        {
            if (Logger.IsDebug == false)
            {
                return;
            }

            var logMessage = new StringBuilder();
            logMessage.AppendLine($"Response from: {url}");
            logMessage.AppendLine($"Status code: {(int)ablyResponse.StatusCode} {ablyResponse.StatusCode}");

            logMessage.AppendLine("---- Response Headers ----");
            foreach (var header in ablyResponse.Headers)
            {
                logMessage.AppendLine($"{header.Key}: {header.Value.JoinStrings()}");
            }

            logMessage.AppendLine($"Content Type: {ablyResponse.ContentType}");
            logMessage.AppendLine($"Encoding: {ablyResponse.Encoding}");
            logMessage.AppendLine($"Type: {ablyResponse.Type}");

            logMessage.AppendLine("---- Response Body ----");
            if (ablyResponse.Type != ResponseType.Binary)
            {
                logMessage.AppendLine(ablyResponse.TextResponse);
            }
            else if (ablyResponse.Body != null)
            {
                logMessage.AppendLine(ablyResponse.Body.GetText());
            }

            Logger.Debug(logMessage.ToString());
        }

        private async Task LogMessage(HttpRequestMessage message)
        {
            if (Logger.IsDebug == false)
            {
                return;
            }

            var logMessage = new StringBuilder();
            if (message.Headers.Any())
            {
                logMessage.AppendLine("---- Headers ----");
                foreach (var header in message.Headers)
                {
                    logMessage.AppendLine($"{header.Key}:{header.Value.JoinStrings()}");
                }
            }

            if (message.Content != null)
            {
                var body = await message.Content.ReadAsStringAsync();
                logMessage.AppendLine("---- Body ----");
                logMessage.AppendLine(body);
            }

            Logger.Debug(logMessage.ToString());
        }

        internal static bool IsRetryableResponse(HttpResponseMessage response)
        {
            return ErrorInfo.IsRetryableStatusCode(response.StatusCode);
        }

        internal static bool IsRetryableError(Exception ex)
        {
            if (ex is TaskCanceledException)
            {
                return true;
            }

            var httpEx = ex as HttpRequestException;
            if (httpEx?.InnerException is WebException webEx)
            {
                return webEx.Status == WebExceptionStatus.NameResolutionFailure ||
                       webEx.Status == WebExceptionStatus.Timeout ||
                       webEx.Status == WebExceptionStatus.ConnectFailure ||
                       webEx.Status == WebExceptionStatus.ReceiveFailure ||
                       webEx.Status == WebExceptionStatus.ConnectionClosed ||
                       webEx.Status == WebExceptionStatus.SendFailure ||
                       webEx.Status == WebExceptionStatus.ServerProtocolViolation;
            }

            return false;
        }

        private HttpRequestMessage GetRequestMessage(AblyRequest request, string host)
        {
            var message = new HttpRequestMessage(request.Method, GetRequestUrl(request, host));

            foreach (var header in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

#if MSGPACK
            if(request.Protocol == Protocol.MsgPack)
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetHeaderValue(request.Protocol)));
#endif

            // Always accept JSON
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetHeaderValue(Protocol.Json)));
            if (message.Method == HttpMethod.Post || message.Method == HttpMethod.Put)
            {
                if (request.PostParameters.Any() && request.RequestBody.Length == 0)
                {
                    message.Content = new FormUrlEncodedContent(request.PostParameters);
                }
                else
                {
                    var content = new ByteArrayContent(request.RequestBody);
                    content.Headers.ContentType = new MediaTypeHeaderValue(GetHeaderValue(request.Protocol));
                    message.Content = content;
                }
            }

            return message;
        }

        internal static string GetHeaderValue(Protocol protocol)
        {
            if (protocol == Protocol.Json)
            {
                return "application/json";
            }

            return "application/x-msgpack";
        }

        private static async Task<AblyResponse> GetAblyResponse(HttpResponseMessage response)
        {
            byte[] content = null;
            MediaTypeHeaderValue contentTypeHeader = null;

            if (response.Content != null)
            {
                content = await response.Content?.ReadAsByteArrayAsync();
                contentTypeHeader = response.Content?.Headers.ContentType;
            }

            var ablyResponse = new AblyResponse(contentTypeHeader?.CharSet, contentTypeHeader?.MediaType, content)
            {
                StatusCode = response.StatusCode,
                Headers = response.Headers,
            };

            return ablyResponse;
        }

        public Uri GetRequestUrl(AblyRequest request, string host = null)
        {
            if (host == null)
            {
                host = Options.Host;
            }

            if (request.Url.StartsWith("http"))
            {
                return new Uri($"{request.Url}{GetQuery(request)}");
            }

            string protocol = Options.IsSecure ? "https://" : "http://";
            return new Uri($"{protocol}{host}{(Options.Port.HasValue ? ":" + Options.Port.Value : string.Empty)}{request.Url}{GetQuery(request)}");
        }

        private static string GetQuery(AblyRequest request)
        {
            var query = request.QueryParameters.ToQueryString();
            if (query.IsNotEmpty())
            {
                if (request.Url.Contains('?'))
                {
                    return "&" + query;
                }

                return "?" + query;
            }

            return string.Empty;
        }

        private class HttpResponseWrapper
        {
            public AblyResponse AblyResponse { get; private set; }

            public bool Success { get; private set; }

            public bool CanRetry { get; private set; }

            public Exception Exception { get; private set; }

            public string GetFailedMessage()
            {
                if (Exception != null)
                {
                    return "Can't make request because of Exception: " + Exception.Message;
                }

                return $"Ably server returned error. Status: {AblyResponse.StatusCode}.";
            }

            public static HttpResponseWrapper FromError(Exception ex, bool canRetry)
            {
                return new HttpResponseWrapper { Success = false, CanRetry = canRetry, Exception = ex };
            }

            public static HttpResponseWrapper FromResponse(AblyResponse response, bool success, bool canRetry)
            {
                return new HttpResponseWrapper { Success = success, AblyResponse = response, CanRetry = canRetry };
            }

            public static HttpResponseWrapper FromSuccess(AblyResponse response)
            {
                return new HttpResponseWrapper { Success = true, AblyResponse = response };
            }
        }
    }
}
