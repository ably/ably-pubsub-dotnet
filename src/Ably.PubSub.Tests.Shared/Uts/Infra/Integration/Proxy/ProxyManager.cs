using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Uts.Infra.Integration.Proxy
{
    /// <summary>Where the proxy binary came from.</summary>
    public enum ProxyBinarySource
    {
        /// <summary>Not resolved yet.</summary>
        None,

        /// <summary><c>UTS_PROXY_LOCAL_PATH</c>: a locally built binary, started where it is (no checksum).</summary>
        LocalOverride,

        /// <summary>
        /// The verified binary already in the shared cache, reused as is: an earlier run of this or another SDK's UTS
        /// infra (ably-java, ably-cocoa) installed it, and every installer verifies the archive before extracting.
        /// </summary>
        Cache,

        /// <summary>Downloaded now from the pinned release, archive checksum verified before extraction.</summary>
        VerifiedDownload,
    }

    // The cache (~/.cache/uts-proxy/v<version>/uts-proxy, locked through uts-proxy.lock in the same directory) is
    // shared with ably-java's and ably-cocoa's UTS infra on purpose: one download per machine and version for every
    // SDK. Keep the layout identical to theirs (no os_arch level, no other root, nothing else written there);
    // CacheDirectory(version) is the one place that decides it.

    /// <summary>
    /// Downloads, verifies, caches, starts and reaps <see href="https://github.com/ably/uts-proxy">ably/uts-proxy</see>
    /// (guide 2.4). One proxy process per test process, on a fixed control port; every proxy test class joins the
    /// <c>"UTS Proxy"</c> collection, so the proxy tier runs in one process.
    ///
    /// <para><b>Pinned:</b> uts-proxy v0.3.0 (the version <c>proxy.md</c> documents). The release publishes
    /// <c>uts-proxy_0.3.0_{darwin,linux}_{amd64,arm64}.tar.gz</c> only; each archive's SHA-256 is pinned here and
    /// checked against the downloaded archive before anything is extracted. There is no Windows asset.</para>
    ///
    /// <para><b>Cache, shared with ably-java and ably-cocoa:</b> <c>~/.cache/uts-proxy/v0.3.0/uts-proxy</c>
    /// (<see cref="CacheDirectory(string)"/>), the layout their UTS infra already uses. A binary already there is a
    /// cache hit and is reused as is: it isn't re-downloaded, and it can't be re-hashed, since only archives are
    /// pinned (every installer verifies the archive before extracting). Nothing besides the binary, the lock file and a
    /// short-lived staging folder is written there.</para>
    ///
    /// <para><b>Install:</b> under the cross-process lock <c>uts-proxy.lock</c> beside the binary (opened with
    /// <c>FileShare.None</c>, an advisory <c>flock</c> on Unix, retried for up to <see cref="LockTimeout"/>), the
    /// binary's presence is checked again (a process that held the lock first may have installed it), then the archive
    /// is downloaded into a staging folder in the binary's own directory (so the final rename stays on one file system),
    /// its SHA-256 compared with the pinned value (on a mismatch the staging folder is deleted and nothing is extracted),
    /// extracted there with the system <c>tar</c> (System.Formats.Tar isn't available on net462 or net6), made
    /// executable there, and only then renamed into place. An interrupted or concurrent run never leaves a half-written
    /// binary that would count as a cache hit; the lock only avoids duplicate downloads.</para>
    ///
    /// <para><b>Override:</b> <c>UTS_PROXY_LOCAL_PATH</c> points at a locally built binary (a configuration override,
    /// not a test gate). It is started where it is, never copied into the cache. On Windows, with no release asset,
    /// the proxy tier fails unless it is set.
    /// <c>UTS_PROXY_CONTROL_PORT</c> moves the control port (default 9100), for running two test processes side by
    /// side.</para>
    /// </summary>
    public static class ProxyManager
    {
        /// <summary>The pinned uts-proxy release.</summary>
        public const string Version = "0.3.0";

        /// <summary>The variable naming a locally built proxy binary.</summary>
        public const string LocalPathVariable = "UTS_PROXY_LOCAL_PATH";

        /// <summary>The variable that moves the control port.</summary>
        public const string ControlPortVariable = "UTS_PROXY_CONTROL_PORT";

        /// <summary>The control port when <see cref="ControlPortVariable"/> isn't set.</summary>
        public const int DefaultControlPort = 9100;

        /// <summary>The release download base.</summary>
        public const string ReleaseBaseUrl = "https://github.com/ably/uts-proxy/releases/download/v" + Version + "/";

        /// <summary>The bound on the proxy reporting healthy after it was started.</summary>
        public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The bound on waiting for another process's download (the cross-process lock).</summary>
        public static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

        private const string BinaryName = "uts-proxy";
        private const string LockName = "uts-proxy.lock";
        private const int DownloadAttempts = 3;
        private const int OutputLinesKept = 200;

        private static readonly IReadOnlyDictionary<string, string> PinnedArchiveSha256 = new Dictionary<string, string>
        {
            ["darwin_amd64"] = "1355526543c3022f87efb7f564f55200b78edc68d84c7dba2e49f63429e3b788",
            ["darwin_arm64"] = "a948f99b7daf9b3bffff742f6405637d40a79947389309eed5f87e59026de9a5",
            ["linux_amd64"] = "de741ba21f3630fea4f59714d00585638d565005599ecd84179931eba248f280",
            ["linux_arm64"] = "15b5ca87c40c2c4ff350c94af1911cea0ad6be5a2d890ba41029bc4b8bc52c61",
        };

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim InstallGate = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentQueue<string> Output = new ConcurrentQueue<string>();
        private static readonly HttpClient ControlClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static Process _process;
        private static bool _exitHookRegistered;

        /// <summary>The control port this process uses.</summary>
        public static int ControlPort
        {
            get
            {
                var value = System.Environment.GetEnvironmentVariable(ControlPortVariable);
                return int.TryParse(value, out var port) && port > 0 ? port : DefaultControlPort;
            }
        }

        /// <summary>The control API base URL (no trailing slash).</summary>
        public static string ControlUrl => $"http://localhost:{ControlPort}";

        /// <summary>Where the binary came from, once <see cref="EnsureBinaryAsync"/> (or <see cref="EnsureStartedAsync"/>) has run.</summary>
        public static ProxyBinarySource Source { get; private set; }

        /// <summary>The binary resolved by the last <see cref="EnsureBinaryAsync"/>.</summary>
        public static string BinaryPath { get; private set; }

        /// <summary>The platform key (<c>darwin_arm64</c>, …) the release binary was chosen for; null with the local override.</summary>
        public static string Platform { get; private set; }

        /// <summary>
        /// Whether the last <see cref="EnsureBinaryAsync"/> found the release binary already in the cache (nothing
        /// downloaded). False with the local override.
        /// </summary>
        public static bool LastEnsureWasCacheHit { get; private set; }

        /// <summary>The proxy's last output lines, for diagnostics.</summary>
        public static IReadOnlyList<string> RecentOutput => Output.ToArray();

        private static bool IsWindows => System.Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>
        /// The one place that decides the cache layout: the directory holding <c>uts-proxy</c> and
        /// <c>uts-proxy.lock</c> for a version, <c>~/.cache/uts-proxy/v&lt;version&gt;/</c>, shared with ably-java and
        /// ably-cocoa.
        /// </summary>
        /// <param name="version">The release version, for example <c>0.3.0</c>.</param>
        /// <returns>The directory.</returns>
        public static string CacheDirectory(string version)
            => Path.Combine(HomeDirectory(), ".cache", "uts-proxy", "v" + version);

        /// <summary>
        /// Makes sure the binary to run is there and returns its path, without starting it: the local override as
        /// given, or the verified release binary from the cache, downloading it first if it isn't there. Sets
        /// <see cref="Source"/>, <see cref="BinaryPath"/> and <see cref="LastEnsureWasCacheHit"/>.
        /// </summary>
        /// <returns>The binary's path.</returns>
        public static async Task<string> EnsureBinaryAsync()
        {
            await InstallGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var local = System.Environment.GetEnvironmentVariable(LocalPathVariable);
                if (!string.IsNullOrWhiteSpace(local))
                {
                    local = local.Trim();
                    if (!File.Exists(local))
                    {
                        throw new InvalidOperationException($"{LocalPathVariable} is set to '{local}', but there is no file there.");
                    }

                    LastEnsureWasCacheHit = false;
                    Platform = null;
                    Source = ProxyBinarySource.LocalOverride;
                    BinaryPath = local;
                    return local;
                }

                if (IsWindows)
                {
                    throw new InvalidOperationException(
                        $"uts-proxy v{Version} publishes no Windows binary. Build it from source (clone " +
                        "https://github.com/ably/uts-proxy, check out v" + Version + ", run `go build -o uts-proxy.exe .`) and set " +
                        $"{LocalPathVariable} to the built uts-proxy.exe.");
                }

                var platform = DetectPlatform();
                Platform = platform;
                var directory = CacheDirectory(Version);
                var pinned = PinnedSha256(platform);
                if (pinned == null)
                {
                    throw new InvalidOperationException(
                        $"uts-proxy v{Version} publishes no binary for '{platform}' (it has darwin/linux amd64/arm64). Build it " +
                        $"from source with `go build` and set {LocalPathVariable} to it.");
                }

                var binary = Path.Combine(directory, BinaryName);
                if (File.Exists(binary))
                {
                    LastEnsureWasCacheHit = true;
                    Source = ProxyBinarySource.Cache;
                    BinaryPath = binary;
                    return binary;
                }

                Directory.CreateDirectory(directory);
                using (AcquireLock(Path.Combine(directory, LockName)))
                {
                    // Re-checked under the lock: a process that held it first may have just installed the binary.
                    if (File.Exists(binary))
                    {
                        LastEnsureWasCacheHit = true;
                        Source = ProxyBinarySource.Cache;
                    }
                    else
                    {
                        await DownloadAndInstallAsync(platform, pinned, binary).ConfigureAwait(false);
                        LastEnsureWasCacheHit = false;
                        Source = ProxyBinarySource.VerifiedDownload;
                    }
                }

                BinaryPath = binary;
                return binary;
            }
            finally
            {
                InstallGate.Release();
            }
        }

        /// <summary>
        /// Makes sure the pinned proxy (or the local override) runs on <see cref="ControlPort"/> and reports healthy.
        /// Idempotent and safe to call from every test; the first call installs (<see cref="EnsureBinaryAsync"/>) and
        /// starts it. Fails, never skips: without a usable binary the proxy tier can't run.
        /// </summary>
        /// <returns>A task that completes once <c>GET /health</c> succeeds.</returns>
        public static async Task EnsureStartedAsync()
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var process = _process;
                if (process != null && !HasExited(process))
                {
                    return;
                }

                // A control API already answering on the port isn't ours: it may be a stale proxy of another
                // version left over from a killed run. Reusing it would silently drop the pin, so fail instead.
                if (await IsHealthyAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        $"Something already answers {ControlUrl}/health, but this test process didn't start it. Stop it " +
                        $"(for example `lsof -i :{ControlPort}`, then kill the stale uts-proxy), or set {ControlPortVariable} " +
                        "to a free port.");
                }

                var binary = await EnsureBinaryAsync().ConfigureAwait(false);
                Start(binary);
                await WaitUntilHealthyAsync().ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>One <c>GET /health</c> on the control API.</summary>
        /// <param name="timeout">The bound on the request; 5 s when null.</param>
        /// <returns>True for a 2xx answer whose body, if it has an <c>ok</c> field, says true.</returns>
        public static async Task<bool> IsHealthyAsync(TimeSpan? timeout = null)
        {
            try
            {
                using (var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5)))
                using (var response = await ControlClient.GetAsync(ControlUrl + "/health", cancel.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        var ok = JObject.Parse(text)["ok"];
                        return ok == null || ok.Type != JTokenType.Boolean || (bool)ok;
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The release asset for a platform key.</summary>
        /// <param name="platform">For example <c>linux_amd64</c>.</param>
        /// <returns>The archive file name.</returns>
        internal static string AssetName(string platform) => $"uts-proxy_{Version}_{platform}.tar.gz";

        /// <summary>The pinned archive hash for a platform key, or null if the release has no asset for it.</summary>
        /// <param name="platform">For example <c>linux_amd64</c>.</param>
        /// <returns>The lower-case hex SHA-256, or null.</returns>
        internal static string PinnedSha256(string platform)
            => PinnedArchiveSha256.TryGetValue(platform, out var sha) ? sha : null;

        /// <summary>The lower-case hex SHA-256 of a file.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The hash.</returns>
        internal static string Sha256Hex(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }
        }

        /// <summary>
        /// The release platform key for this machine, from <c>uname -s</c> and <c>uname -m</c> (the same on net462
        /// under Mono and on net6, where <c>Environment.OSVersion</c> reports Unix for both macOS and Linux).
        /// </summary>
        /// <returns>For example <c>darwin_arm64</c>.</returns>
        internal static string DetectPlatform()
        {
            var os = RunTool("uname", "-s").Trim().ToLowerInvariant();
            return os + "_" + NormaliseArch(RunTool("uname", "-m"));
        }

        private static string NormaliseArch(string machine)
        {
            machine = machine.Trim().ToLowerInvariant();
            switch (machine)
            {
                case "x86_64":
                case "amd64":
                    return "amd64";
                case "arm64":
                case "aarch64":
                    return "arm64";
                default:
                    return machine;
            }
        }

        private static string HomeDirectory()
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            return string.IsNullOrEmpty(home)
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
                : home;
        }

        private static async Task DownloadAndInstallAsync(string platform, string pinnedArchiveSha, string binary)
        {
            var directory = Path.GetDirectoryName(binary);
            var asset = AssetName(platform);
            var staging = NewStagingDirectory(directory);
            try
            {
                var archive = Path.Combine(staging, asset);
                await DownloadAsync(ReleaseBaseUrl + asset, archive).ConfigureAwait(false);

                // Verified before anything is extracted; on a mismatch the finally removes the staging folder.
                var actual = Sha256Hex(archive);
                if (actual != pinnedArchiveSha)
                {
                    throw new InvalidOperationException(
                        $"{asset} has SHA-256 {actual}, but v{Version} pins {pinnedArchiveSha}. Not extracting it.");
                }

                var extractDirectory = Path.Combine(staging, "extract");
                Directory.CreateDirectory(extractDirectory);
                RunTool("tar", $"-xzf \"{archive}\" -C \"{extractDirectory}\"");
                var extracted = Directory.GetFiles(extractDirectory, BinaryName, SearchOption.AllDirectories).FirstOrDefault();
                if (extracted == null)
                {
                    throw new InvalidOperationException($"{asset} contains no '{BinaryName}' file.");
                }

                MakeExecutable(extracted);
                try
                {
                    File.Move(extracted, binary);
                }
                catch (IOException) when (File.Exists(binary))
                {
                    // Another installer (one not honouring the lock) renamed its verified copy into place first.
                }
            }
            finally
            {
                TryDelete(() => Directory.Delete(staging, recursive: true));
            }
        }

        private static string NewStagingDirectory(string directory)
        {
            // In the binary's own directory, so the final rename never crosses a file system.
            var staging = Path.Combine(directory, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            return staging;
        }

        private static void MakeExecutable(string path)
        {
            if (!IsWindows)
            {
                RunTool("chmod", $"755 \"{path}\"");
            }
        }

        private static async Task DownloadAsync(string url, string destination)
        {
            // An idempotent GET, so a failed attempt is retried (unlike POST /apps).
            Exception last = null;
            for (var attempt = 1; attempt <= DownloadAttempts; attempt++)
            {
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                    using (var response = await client.GetAsync(url).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException($"GET {url} answered {(int)response.StatusCode} {response.ReasonPhrase}");
                        }

                        using (var file = File.Create(destination))
                        {
                            await response.Content.CopyToAsync(file).ConfigureAwait(false);
                        }

                        return;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            throw new InvalidOperationException($"Couldn't download {url} after {DownloadAttempts} attempts: {last?.Message}", last);
        }

        private static IDisposable AcquireLock(string path)
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (stopwatch.Elapsed < LockTimeout)
                {
                    Thread.Sleep(250);
                }
            }
        }

        private static void Start(string binary)
        {
            while (Output.TryDequeue(out _))
            {
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo(binary, $"-port {ControlPort}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            // Both pipes are drained: a child whose output buffer fills stops making progress, and the proxy logs
            // every frame it forwards. The last lines are kept for failure messages.
            process.OutputDataReceived += (sender, args) => Keep(args.Data);
            process.ErrorDataReceived += (sender, args) => Keep(args.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;

            if (!_exitHookRegistered)
            {
                _exitHookRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => Stop();

                // The xUnit console runner on .NET Framework/Mono runs the tests in a secondary AppDomain, which is
                // unloaded at the end of the run without ProcessExit being raised in it: without this, the proxy
                // outlived the net462 run. DomainUnload is never raised for the default domain, so this doesn't fire
                // twice on .NET (Core); Stop is idempotent anyway.
                AppDomain.CurrentDomain.DomainUnload += (sender, args) => Stop();
            }
        }

        private static void Keep(string line)
        {
            if (line == null)
            {
                return;
            }

            Output.Enqueue(ProxyRedaction.RedactText(line));
            while (Output.Count > OutputLinesKept && Output.TryDequeue(out _))
            {
            }
        }

        private static async Task WaitUntilHealthyAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < StartTimeout)
            {
                var process = _process;
                if (process == null || HasExited(process))
                {
                    throw new InvalidOperationException(
                        $"uts-proxy ({BinaryPath}) exited before reporting healthy. Its output:\n{string.Join("\n", RecentOutput)}");
                }

                if (await IsHealthyAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
                {
                    return;
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            Stop();
            throw new InvalidOperationException(
                $"uts-proxy ({BinaryPath}) didn't report healthy on {ControlUrl}/health within {StartTimeout.TotalSeconds} s. " +
                $"Its output:\n{string.Join("\n", RecentOutput)}");
        }

        private static void Stop()
        {
            var process = Interlocked.Exchange(ref _process, null);
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                // Reaping is best effort at process exit.
            }
            finally
            {
                process.Dispose();
            }
        }

        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static string RunTool(string fileName, string arguments)
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            })
            {
                process.Start();
                var stderr = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(60000))
                {
                    TryDelete(process.Kill);
                    throw new InvalidOperationException($"`{fileName} {arguments}` didn't finish within 60 s.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"`{fileName} {arguments}` exited with {process.ExitCode}: {stderr.GetAwaiter().GetResult()}");
                }

                return stdout;
            }
        }

        private static void TryDelete(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Cleanup of temporary files is best effort.
            }
        }
    }
}
