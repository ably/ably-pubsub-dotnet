using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Integration
{
    /// <summary>One entry of the specs' <c>app_config.keys</c>.</summary>
    public sealed class SandboxKey
    {
        internal SandboxKey(string keyName, string keySecret, string keyStr, string capability)
        {
            KeyName = keyName;
            KeySecret = keySecret;
            KeyStr = keyStr;
            Capability = capability;
        }

        /// <summary><c>appId.keyId</c>, the part of <see cref="KeyStr"/> before the colon.</summary>
        public string KeyName { get; }

        /// <summary>The part of <see cref="KeyStr"/> after the colon.</summary>
        public string KeySecret { get; }

        /// <summary>The full key, <c>appId.keyId:secret</c>: the specs' <c>key_str</c> / <c>api_key</c>.</summary>
        public string KeyStr { get; }

        /// <summary>The capability JSON the sandbox reports for the key.</summary>
        public string Capability { get; }

        public override string ToString() => KeyName;
    }

    /// <summary>
    /// The specs' sandbox provisioning (<c>integration-testing.md</c> "Sandbox Setup"): an app created from
    /// <c>ably-common/test-resources/test-app-setup.json</c> and deleted afterwards.
    /// </summary>
    public sealed class SandboxApp
    {
        /// <summary>The specs' <c>endpoint: "nonprod:sandbox"</c>.</summary>
        public const string SandboxEndpoint = "nonprod:sandbox";

        /// <summary>The variable that overrides <see cref="SandboxEndpoint"/>, shared with the native sandbox suite.</summary>
        public const string EndpointVariable = "ABLY_ENDPOINT";

        /// <summary>The namespace the specs' mutable-message suites use (<c>mutable:</c> channels).</summary>
        public const string MutableMessagesNamespace = "mutable";

        /// <summary>The bound on each provisioning request (<c>integration-testing.md</c> "Timeout Strategy").</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private const int DeleteAttempts = 2;

        private readonly HttpMessageHandler _handler;

        private SandboxApp(string endpoint, string host, string appId, IReadOnlyList<SandboxKey> keys, HttpMessageHandler handler)
        {
            Endpoint = endpoint;
            Host = host;
            AppId = appId;
            Keys = keys;
            _handler = handler;
        }

        /// <summary>
        /// The endpoint every UTS integration client targets: <c>ABLY_ENDPOINT</c> or <see
        /// cref="SandboxEndpoint"/>.
        /// </summary>
        public static string DefaultEndpoint
        {
            get
            {
                var value = System.Environment.GetEnvironmentVariable(EndpointVariable);
                return string.IsNullOrWhiteSpace(value) ? SandboxEndpoint : value.Trim();
            }
        }

        /// <summary>The endpoint the app was created on; clients built for it use it as <c>ClientOptions.Endpoint</c>.</summary>
        public string Endpoint { get; }

        /// <summary>The host the provisioning requests went to.</summary>
        public string Host { get; }

        /// <summary>The specs' <c>app_config.app_id</c>.</summary>
        public string AppId { get; }

        /// <summary>The specs' <c>app_config.keys</c>, in the order of the app setup.</summary>
        public IReadOnlyList<SandboxKey> Keys { get; }

        /// <summary>The specs' <c>api_key = app_config.keys[0].key_str</c>.</summary>
        public string KeyStr => Keys[0].KeyStr;

        /// <summary>True once a <c>DELETE /apps/{id}</c> got a 2xx answer.</summary>
        public bool Deleted { get; private set; }

        /// <summary>
        /// The status of the last <c>DELETE /apps/{id}</c> response, or null if none arrived (not tried yet, or
        /// the request failed).
        /// </summary>
        public int? LastDeleteStatus { get; private set; }

        /// <summary>The last error <see cref="DeleteAsync"/> swallowed, if any.</summary>
        public Exception LastDeleteError { get; private set; }

        /// <summary>The specs' <c>BEFORE ALL TESTS: POST https://…/apps WITH body from test-app-setup.json</c>.</summary>
        /// <param name="endpoint">The endpoint to provision on; null means <see cref="DefaultEndpoint"/>.</param>
        /// <param name="handler">The test seam: every request of this app goes through it.</param>
        public static async Task<SandboxApp> CreateAsync(
            string endpoint = null,
            HttpMessageHandler handler = null,
            CancellationToken cancellationToken = default)
        {
            endpoint = endpoint ?? DefaultEndpoint;
            var host = Defaults.GetPrimaryDomain(endpoint);
            var body = AppSetupBody().ToString(Formatting.None);

            using (var client = NewHttpClient(handler))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/apps"))
            {
                timeout.CancelAfter(RequestTimeout);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                // Exactly one SendAsync: no retry loop, whatever the failure.
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"POST https://{host}/apps got no response within {RequestTimeout.TotalSeconds} s (not retried)", ex);
                }

                using (response)
                {
                    var text = response.Content == null
                        ? string.Empty
                        : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // The status first: an error page or an Ably error body is not an app.
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"POST https://{host}/apps answered {(int)response.StatusCode} {response.ReasonPhrase} (not retried): {Truncate(text)}");
                    }

                    return Parse(endpoint, host, text, handler);
                }
            }
        }

        /// <summary>
        /// The <c>post_apps</c> body of the embedded <c>test-app-setup.json</c>, with the <see
        /// cref="MutableMessagesNamespace"/> namespace added when it is missing.
        /// </summary>
        public static JObject AppSetupBody()
        {
            var setup = JObject.Parse(ReadEmbeddedAppSetup());
            var postApps = (JObject)setup["post_apps"];
            if (postApps == null)
            {
                throw new InvalidOperationException("test-app-setup.json has no post_apps section");
            }

            var namespaces = postApps["namespaces"] as JArray;
            if (namespaces == null)
            {
                namespaces = new JArray();
                postApps["namespaces"] = namespaces;
            }

            if (namespaces.All(ns => (string)ns["id"] != MutableMessagesNamespace))
            {
                namespaces.Add(new JObject { ["id"] = MutableMessagesNamespace, ["mutableMessages"] = true });
            }

            return postApps;
        }

        /// <summary>
        /// The specs' <c>AFTER ALL TESTS: DELETE https://…/apps/{app_id} WITH Authorization: Basic {api_key}</c>.
        /// </summary>
        public async Task<bool> DeleteAsync()
        {
            if (Deleted)
            {
                return true;
            }

            for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
            {
                try
                {
                    using (var client = NewHttpClient(_handler))
                    using (var timeout = new CancellationTokenSource(RequestTimeout))
                    using (var request = new HttpRequestMessage(HttpMethod.Delete, $"https://{Host}/apps/{AppId}"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue(
                            "Basic",
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(KeyStr)));
                        using (var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            LastDeleteStatus = (int)response.StatusCode;
                            if (response.IsSuccessStatusCode)
                            {
                                Deleted = true;
                                return true;
                            }

                            // A 4xx won't change on a retry.
                            if ((int)response.StatusCode < 500)
                            {
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastDeleteError = ex;
                }
            }

            return false;
        }

        public override string ToString() => $"sandbox app {AppId} on {Host}";

        private static SandboxApp Parse(string endpoint, string host, string text, HttpMessageHandler handler)
        {
            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"POST https://{host}/apps answered 2xx with a body that isn't JSON: {Truncate(text)}", ex);
            }

            var appId = (string)json["appId"];
            var keysJson = json["keys"] as JArray;
            if (string.IsNullOrEmpty(appId) || keysJson == null || keysJson.Count == 0)
            {
                throw new InvalidOperationException($"POST https://{host}/apps answered without an appId or keys: {Truncate(text)}");
            }

            var keys = new List<SandboxKey>();
            foreach (var key in keysJson)
            {
                var keyStr = (string)key["keyStr"];
                var keyName = (string)key["keyName"];
                var keySecret = (string)key["keySecret"];

                // The response's keyName is the key id alone; the specs' key name is appId.keyId, the part of
                // keyStr before the colon (the native fixture builds it the same way).
                var fullName = keyName != null && keyName.StartsWith(appId + ".", StringComparison.Ordinal)
                    ? keyName
                    : appId + "." + keyName;
                keys.Add(new SandboxKey(fullName, keySecret, keyStr, (string)key["capability"]));
            }

            return new SandboxApp(endpoint, host, appId, keys, handler);
        }

        /// <summary>The embedded <c>test-app-setup.json</c>.</summary>
        private static string ReadEmbeddedAppSetup()
        {
            var assembly = typeof(SandboxApp).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".test-app-setup.json", StringComparison.Ordinal));
            if (name == null)
            {
                throw new InvalidOperationException($"{assembly.GetName().Name} embeds no test-app-setup.json");
            }

            using (var stream = assembly.GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static HttpClient NewHttpClient(HttpMessageHandler handler)
        {
            var client = handler == null
                ? new HttpClient()
                : new HttpClient(handler, disposeHandler: false);

            // The cancellation token bounds each request; this is only the backstop.
            client.Timeout = RequestTimeout + TimeSpan.FromSeconds(5);
            return client;
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(empty body)";
            }

            return text.Length <= 500 ? text : text.Substring(0, 500) + "…";
        }
    }
}
