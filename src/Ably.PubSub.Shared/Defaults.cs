using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Ably.PubSub.Transport;

namespace Ably.PubSub
{
    internal class Defaults
    {
        internal static readonly string LibraryVersion = GetVersion();

        internal static string GetVersion()
        {
            // Read the informational version, not the file version: a prerelease stamps its full
            // SemVer (e.g. "2.0.0-beta.1") into AssemblyInformationalVersion while AssemblyFileVersion
            // is numeric-only ("2.0.0"), so reading the file version would report a prerelease as GA
            // on the wire. Do NOT apply the .Take(3) truncation to the informational version - it would
            // corrupt the prerelease label.
            var info = typeof(Defaults).GetTypeInfo().Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                return NormalizeInformationalVersion(info);
            }

            // Fallback: numeric file version, first three parts.
            var fileVersion = typeof(Defaults).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            return fileVersion.Split('.').Take(3).JoinStrings(".");
        }

        internal static string NormalizeInformationalVersion(string informationalVersion)
        {
            // SourceLink appends build metadata as "+<commit>"; strip it, keeping the SemVer core
            // and any prerelease label (the part before '+').
            var plus = informationalVersion.IndexOf('+');
            return plus >= 0 ? informationalVersion.Substring(0, plus) : informationalVersion;
        }

        public const string ProtocolVersion = "6"; // CSV2

        public const int QueryLimit = 100;

        public const string InternetCheckUrl = "https://internet-up.ably-realtime.com/is-the-internet-up.txt";
        public const string InternetCheckOkMessage = "yes";

        // REC1a: the endpoint used when ClientOptions.Endpoint is unset
        public const string Endpoint = "main";

        public const int Port = 80;
        public const int TlsPort = 443;

        private const string NonprodPrefix = "nonprod:";

        // REC2c1: the fallback domains for the default endpoint
        public static readonly string[] FallbackHosts = GetEndpointFallbackHosts(Endpoint);
        public static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromHours(1);
        public static readonly Capability DefaultTokenCapability = Capability.AllowAll;

        // Buffer in seconds before a token is considered unusable
        public const int TokenExpireBufferInSeconds = 15;
        public const int HttpMaxRetryCount = 3;
        public static readonly TimeSpan ChannelRetryTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan HttpMaxRetryDuration = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan MaxHttpRequestTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan MaxHttpOpenTimeout = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan RealtimeRequestTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DisconnectedRetryTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan SuspendedRetryTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ConnectionStateTtl = TimeSpan.FromSeconds(120); // https://sdk.ably.com/builds/ably/specification/main/features/#DF1a
        public static readonly TimeSpan FallbackRetryTimeout = TimeSpan.FromMinutes(10); // https://sdk.ably.com/builds/ably/specification/main/features/#TO3l10

        public static readonly ITransportFactory WebSocketTransportFactory = IoC.TransportFactory;

        internal const int TokenErrorCodesRangeStart = 40140;
        internal const int TokenErrorCodesRangeEnd = 40149;

        internal const string DeviceIdentityTokenHeader = "X-Ably-DeviceIdentityToken";
        internal const string DeviceSecretHeader = "X-Ably-DeviceSecret";

        /// <summary>The default log level you'll see in the debug output.</summary>
        internal const LogLevel DefaultLogLevel = LogLevel.Warning;

        internal static Func<DateTimeOffset> NowFunc()
        {
            return () => DateTimeOffset.UtcNow;
        }

#if MSGPACK
        internal const Protocol DefaultProtocol = Ably.PubSub.Protocol.MsgPack;
        internal const bool MsgPackEnabled = true;
#else
        internal const Protocol Protocol = Ably.PubSub.Protocol.Json;
        internal const bool MsgPackEnabled = false;

#endif

        /// <summary>
        /// Resolves the primary domain that both REST requests and realtime connections use.
        /// Spec: REC1.
        /// </summary>
        /// <param name="endpoint">The configured endpoint, or null for the default.</param>
        /// <returns>The primary domain.</returns>
        internal static string GetPrimaryDomain(string endpoint)
        {
            if (endpoint == null)
            {
                endpoint = Endpoint;
            }

            // REC1b2
            if (IsHostname(endpoint))
            {
                return endpoint;
            }

            // REC1b3
            if (endpoint.StartsWith(NonprodPrefix, StringComparison.Ordinal))
            {
                return endpoint.Substring(NonprodPrefix.Length) + ".realtime.ably-nonprod.net";
            }

            // REC1b4
            return endpoint + ".realtime.ably.net";
        }

        /// <summary>
        /// Resolves the default fallback domains for an endpoint, used when ClientOptions.FallbackHosts is unset.
        /// Spec: REC2c.
        /// </summary>
        /// <param name="endpoint">The configured endpoint, or null for the default.</param>
        /// <returns>The fallback domains; empty when the endpoint is a hostname.</returns>
        internal static string[] GetEndpointFallbackHosts(string endpoint)
        {
            if (endpoint == null)
            {
                endpoint = Endpoint;
            }

            // REC2c2
            if (IsHostname(endpoint))
            {
                return new string[0];
            }

            // REC2c3
            if (endpoint.StartsWith(NonprodPrefix, StringComparison.Ordinal))
            {
                return BuildFallbackHosts(endpoint.Substring(NonprodPrefix.Length), "ably-realtime-nonprod.com");
            }

            // REC2c1, REC2c4
            return BuildFallbackHosts(endpoint, "ably-realtime.com");
        }

        // REC1b2: an endpoint is a hostname if it contains '.' or '::', or is "localhost"
        private static bool IsHostname(string endpoint) =>
            endpoint.Contains(".") || endpoint.Contains("::") || endpoint == "localhost";

        private static string[] BuildFallbackHosts(string routingPolicyId, string domain) => new[]
        {
            $"{routingPolicyId}.a.fallback.{domain}",
            $"{routingPolicyId}.b.fallback.{domain}",
            $"{routingPolicyId}.c.fallback.{domain}",
            $"{routingPolicyId}.d.fallback.{domain}",
            $"{routingPolicyId}.e.fallback.{domain}",
        };
    }
}
