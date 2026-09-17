using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#if NETSTANDARD2_0_OR_GREATER && UNITY_PACKAGE
using IO.Ably.Unity;
#endif

namespace IO.Ably
{
    internal static class Agent
    {
        public enum PlatformRuntime
        {
            Framework,
            Netstandard20,
            Net6,
            Net7,
            XamarinAndroid,
            XamarinIos,
            Other,
        }

        public static class OS
        {
            public const string Windows = "dotnet-windows";
            public const string MacOS = "dotnet-macOS";
            public const string Linux = "dotnet-linux";
            public const string Android = "dotnet-android";
            public const string IOS = "dotnet-iOS";
            public const string TvOS = "dotnet-tvOS";
            public const string WatchOS = "dotnet-watchOS";
            public const string Browser = "dotnet-browser";
        }

        internal const string AblyAgentHeader = "Ably-Agent";
        private static readonly string AblySdkIdentifier = $"ably-dotnet/{Defaults.LibraryVersion}"; // RSC7d1

        private static readonly Lazy<string> _dotnetRuntimeIdentifier =
            new Lazy<string>(() => GetDotnetRuntimeIdentifier());

        private static readonly Lazy<string> _osIdentifier =
            new Lazy<string>(() => GetOsIdentifier());

        /// <summary>
        /// Gets the cached .NET runtime identifier string.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        internal static string DotnetRuntimeIdentifier => _dotnetRuntimeIdentifier.Value;

        /// <summary>
        /// Gets the cached OS identifier string.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        internal static string OsIdentifier => _osIdentifier.Value;

        /// <summary>
        /// This returns dotnet platform as per ably-lib mappings defined in agents.json.
        /// https://github.com/ably/ably-common/blob/main/protocol/agents.json.
        /// This is required since we are migrating from 'X-Ably-Lib' header (RSC7b) to agent headers (RSC7d).
        /// Please note that uwp platform is Deprecated and removed as a part of https://github.com/ably/ably-dotnet/pull/1101.
        /// </summary>
        /// <returns> Clean Platform Identifier. </returns>
        private static string GetDotnetRuntimeIdentifier()
        {
            string DotnetRuntimeName()
            {
                switch (IoC.PlatformId)
                {
                    case PlatformRuntime.Framework:
                        return "dotnet-framework";
                    case PlatformRuntime.Netstandard20:
                        return "dotnet-standard";
                    case PlatformRuntime.Net6:
                        return "dotnet6";
                    case PlatformRuntime.Net7:
                        return "dotnet7";
                    case PlatformRuntime.XamarinAndroid:
                        return "xamarin";
                    case PlatformRuntime.XamarinIos:
                        return "xamarin";
                }

                return string.Empty;
            }

            var dotnetRuntimeName = DotnetRuntimeName();

            string DotnetRuntimeVersion()
            {
                try
                {
                    return Environment.Version.ToString();
                }
                catch
                {
                    return string.Empty;
                }
            }

            var dotnetRuntimeVersion = DotnetRuntimeVersion();

            return dotnetRuntimeVersion.IsEmpty() ?
                dotnetRuntimeName : $"{dotnetRuntimeName}/{dotnetRuntimeVersion}";
        }

        private static string GetOsIdentifier()
        {
            switch (IoC.PlatformId)
            {
                case PlatformRuntime.XamarinAndroid:
                    return OS.Android;
                case PlatformRuntime.XamarinIos:
                    return OS.IOS;
            }

#if NETSTANDARD2_0_OR_GREATER && UNITY_PACKAGE
            return UnityHelper.OsIdentifier;
#endif

            // Preprocessors defined as per https://learn.microsoft.com/en-us/dotnet/standard/frameworks#preprocessor-symbols
            // Get operating system as per https://stackoverflow.com/a/66618677 for .NET5 and greater
#if NET5_0_OR_GREATER
            if (OperatingSystem.IsWindows())
            {
                return OS.Windows;
            }

            if (OperatingSystem.IsLinux())
            {
                return OS.Linux;
            }

            if (OperatingSystem.IsMacOS())
            {
                return OS.MacOS;
            }

            if (OperatingSystem.IsAndroid())
            {
                return OS.Android;
            }

            if (OperatingSystem.IsIOS())
            {
                return OS.IOS;
            }

            if (OperatingSystem.IsTvOS())
            {
                return OS.TvOS;
            }

            if (OperatingSystem.IsWatchOS())
            {
                return OS.WatchOS;
            }

            if (OperatingSystem.IsBrowser())
            {
                return OS.Browser;
            }
#endif

#pragma warning disable CS0162 // Disable code unreachable warning when above conditional statement is true
            try
            {
                // If netstandard target is used by .Net Core App, https://mariusschulz.com/blog/detecting-the-operating-system-in-net-core
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return OS.Linux;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return OS.MacOS;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return OS.Windows;
                }

                // If netframework/netstandard target is used by .Net Mono App, http://docs.go-mono.com/?link=P%3aSystem.Environment.OSVersion
                // https://stackoverflow.com/questions/9129491/c-sharp-compiled-in-mono-detect-os
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                    case PlatformID.Win32S:
                    case PlatformID.Win32Windows:
                    case PlatformID.WinCE:
                        return OS.Windows;
                    case PlatformID.Unix:
                        return OS.Linux;
                    case PlatformID.MacOSX:
                        return OS.MacOS;
                }
            }
            catch
            {
                // ignored, if above code throws runtime exception for class/type/enum not found
            }

            return string.Empty;
#pragma warning restore CS0162
        }

        internal static string AblyAgentIdentifier(Dictionary<string, string> additionalAgents)
        {
            string GetAgentComponentString(string product, string version)
            {
                return string.IsNullOrEmpty(version) ? product : $"{product}/{version}";
            }

            void AddAgentIdentifier(ICollection<string> currentAgentComponents, string product, string version = null)
            {
                if (!string.IsNullOrEmpty(product))
                {
                    currentAgentComponents.Add(GetAgentComponentString(product, version));
                }
            }

            var agentComponents = new List<string>();
            AddAgentIdentifier(agentComponents, AblySdkIdentifier);
            AddAgentIdentifier(agentComponents, DotnetRuntimeIdentifier);

#if NETSTANDARD2_0_OR_GREATER && UNITY_PACKAGE
            AddAgentIdentifier(agentComponents, UnityHelper.UnityIdentifier);
#endif

            AddAgentIdentifier(agentComponents, OsIdentifier);

            if (additionalAgents == null)
            {
                return string.Join(" ", agentComponents);
            }

            foreach (var agent in additionalAgents)
            {
                AddAgentIdentifier(agentComponents, agent.Key, agent.Value);
            }

            return string.Join(" ", agentComponents);
        }
    }
}
