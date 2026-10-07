using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The running <see href="https://github.com/ably/uts-proxy">ably/uts-proxy</see> control API —
    /// started once per test process, reaped at exit.
    ///
    /// The proxy is not vendored and not committed. It is located, in order, by:
    ///
    /// <list type="number">
    ///   <item><c>UTS_PROXY_CONTROL_URL</c> — a control API already running, which this class then
    ///     neither starts nor stops. This is the CI shape and the shape for a developer who wants to
    ///     watch the proxy's own log.</item>
    ///   <item><c>UTS_PROXY_PATH</c> — an explicit path to the binary.</item>
    ///   <item><c>uts-proxy</c> / <c>uts-proxy.exe</c> on PATH.</item>
    /// </list>
    ///
    /// On Windows there is no prebuilt binary — the project's releases are linux/darwin only — so a
    /// local run builds it from source with <c>go build -o uts-proxy.exe .</c> and points
    /// <c>UTS_PROXY_PATH</c> at the result. CI runners are Linux/macOS and use the release binary.
    /// </summary>
    public sealed class UtsProxyControl
    {
        public const string ControlUrlVariable = "UTS_PROXY_CONTROL_URL";
        public const string BinaryPathVariable = "UTS_PROXY_PATH";

        private const int DefaultControlPort = 9100;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static UtsProxyControl _instance;
        private static Process _process;

        private UtsProxyControl(string baseUrl, bool ownsProcess)
        {
            BaseUrl = baseUrl.TrimEnd('/');
            OwnsProcess = ownsProcess;
        }

        public string BaseUrl { get; }

        /// <summary>False when the control API was already running and is not ours to stop.</summary>
        public bool OwnsProcess { get; }

        /// <summary>
        /// True when a proxy is reachable or launchable. <see cref="ProxyFactAttribute"/> reads this to
        /// decide whether the proxy tier can run at all.
        /// </summary>
        public static bool IsAvailable =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ControlUrlVariable)) ||
            FindBinary() != null;

        public static async Task<UtsProxyControl> Instance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var existing = Environment.GetEnvironmentVariable(ControlUrlVariable);
                if (!string.IsNullOrEmpty(existing))
                {
                    _instance = new UtsProxyControl(existing, ownsProcess: false);
                    await _instance.WaitUntilHealthy().ConfigureAwait(false);
                    return _instance;
                }

                var binary = FindBinary();
                if (binary == null)
                {
                    throw new InvalidOperationException(
                        "uts-proxy was not found. Set UTS_PROXY_CONTROL_URL to an already-running " +
                        $"control API, or {BinaryPathVariable} to the binary, or put uts-proxy on PATH. " +
                        "On Windows there is no prebuilt binary: clone ably/uts-proxy and run " +
                        "`go build -o uts-proxy.exe .`.");
                }

                _process = Start(binary);
                _instance = new UtsProxyControl($"http://localhost:{DefaultControlPort}", ownsProcess: true);
                await _instance.WaitUntilHealthy().ConfigureAwait(false);
                return _instance;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// The specs' <c>create_proxy_session(...)</c>. The caller owns the session and must close it;
        /// <see cref="UtsProxyTestBase"/> does that automatically.
        /// </summary>
        public async Task<UtsProxySession> CreateSession(
            string realtimeHost,
            string restHost,
            JArray rules = null,
            int? port = null,
            int timeoutMs = UtsProxySession.DefaultSessionTimeoutMs)
        {
            var body = new JObject
            {
                ["target"] = new JObject
                {
                    ["realtimeHost"] = realtimeHost,
                    ["restHost"] = restHost,
                },

                // The session's own auto-cleanup timer is an *idle* timer measured from the last
                // traffic through it, not a deadline. The proxy's default is 30s, and a test that has
                // the proxy delay a response and then reads the log can idle longer than that — long
                // enough for the session to be collected out from under it.
                ["timeoutMs"] = timeoutMs,
            };

            if (rules != null)
            {
                body["rules"] = rules;
            }

            if (port.HasValue)
            {
                body["port"] = port.Value;
            }

            var response = await Post("/sessions", body).ConfigureAwait(false);
            return new UtsProxySession(
                this,
                (string)response["sessionId"],
                (int)response["proxy"]["port"]);
        }

        internal async Task<JObject> Post(string path, JObject body)
        {
            using (var client = NewClient())
            {
                var content = new StringContent(
                    body == null ? "{}" : body.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync(BaseUrl + path, content).ConfigureAwait(false);
                return await Read(response, "POST " + path).ConfigureAwait(false);
            }
        }

        internal async Task<JObject> Get(string path)
        {
            using (var client = NewClient())
            {
                var response = await client.GetAsync(BaseUrl + path).ConfigureAwait(false);
                return await Read(response, "GET " + path).ConfigureAwait(false);
            }
        }

        internal async Task<bool> Delete(string path)
        {
            using (var client = NewClient())
            {
                try
                {
                    var response = await client.DeleteAsync(BaseUrl + path).ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }
                catch (Exception)
                {
                    // Closing a session is best effort: it must never be the reason a test fails.
                    return false;
                }
            }
        }

        private static HttpClient NewClient() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static async Task<JObject> Read(HttpResponseMessage response, string what)
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"uts-proxy control API {what} returned {(int)response.StatusCode}: {text}");
            }

            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }

        private static string FindBinary()
        {
            var explicitPath = Environment.GetEnvironmentVariable(BinaryPathVariable);
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            var names = new[] { "uts-proxy", "uts-proxy.exe" };
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    try
                    {
                        var candidate = Path.Combine(directory.Trim(), name);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // A malformed PATH entry is not worth failing over.
                    }
                }
            }

            return null;
        }

        private static Process Start(string binary)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(binary, $"-port {DefaultControlPort}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();

            // Drain both pipes: a child process whose output buffer fills stops making progress, and
            // the proxy logs every frame it forwards.
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            AppDomain.CurrentDomain.ProcessExit += (sender, args) => Stop();
            return process;
        }

        private static void Stop()
        {
            var process = _process;
            _process = null;
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
                // Reaping is best effort at process exit.
            }
        }

        private async Task WaitUntilHealthy()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            Exception last = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    var health = await Get("/health").ConfigureAwait(false);
                    if (health["ok"] != null && (bool)health["ok"])
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"uts-proxy control API at {BaseUrl} did not become healthy within 15s." +
                (last == null ? string.Empty : " Last error: " + last.Message));
        }
    }
}
