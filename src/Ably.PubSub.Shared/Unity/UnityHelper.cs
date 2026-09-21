using System;

namespace IO.Ably.Unity
{
    /// <summary>
    /// Unity platform detection and identification.
    /// Provides constants and methods for Unity OS identification without compile-time dependency on UnityEngine.
    /// </summary>
    internal static class UnityHelper
    {
        private static readonly Lazy<string> _osIdentifier =
            new Lazy<string>(() => GetOsIdentifier());

        private static readonly Lazy<string> _unityIdentifier =
            new Lazy<string>(() => GetUnityIdentifier());

        // Platform identifier constants for Unity agent strings
        // These match the format expected by Ably's agent protocol
        public const string Windows = "unity-windows";
        public const string MacOS = "unity-macOS";
        public const string Linux = "unity-linux";
        public const string Android = "unity-android";
        public const string IOS = "unity-iOS";
        public const string TvOS = "unity-tvOS";
        public const string WebGL = "unity-webGL";
        public const string Switch = "unity-nintendo-switch";
        public const string PS4 = "unity-PS4";
        public const string PS5 = "unity-PS5";
        public const string Xbox = "unity-xbox";

        /// <summary>
        /// Constants representing UnityEngine.RuntimePlatform enum string values.
        /// These string values map to the RuntimePlatform enum names without requiring a compile-time reference.
        /// Using string values instead of integers provides stability across Unity versions.
        /// </summary>
        public static class RuntimePlatform
        {
            /// <summary>In the Unity editor on macOS.</summary>
            public const string OSXEditor = "OSXEditor";

            /// <summary>In the player on macOS.</summary>
            public const string OSXPlayer = "OSXPlayer";

            /// <summary>In the player on Windows.</summary>
            public const string WindowsPlayer = "WindowsPlayer";

            /// <summary>In the Unity editor on Windows.</summary>
            public const string WindowsEditor = "WindowsEditor";

            /// <summary>In the player on iPhone.</summary>
            public const string IPhonePlayer = "IPhonePlayer";

            /// <summary>In the player on Android.</summary>
            public const string Android = "Android";

            /// <summary>In the player on Linux.</summary>
            public const string LinuxPlayer = "LinuxPlayer";

            /// <summary>In the Unity editor on Linux.</summary>
            public const string LinuxEditor = "LinuxEditor";

            /// <summary>In the player on WebGL.</summary>
            public const string WebGLPlayer = "WebGLPlayer";

            /// <summary>In the player on PS4.</summary>
            public const string PS4 = "PS4";

            /// <summary>In the player on Xbox One.</summary>
            public const string XboxOne = "XboxOne";

            /// <summary>In the player on Apple TV.</summary>
            public const string TvOS = "tvOS";

            /// <summary>In the player on Nintendo Switch.</summary>
            public const string Switch = "Switch";

            /// <summary>In the player on PS5.</summary>
            public const string PS5 = "PS5";
        }

        /// <summary>
        /// Gets the cached Unity OS identifier string.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        public static string OsIdentifier => _osIdentifier.Value;

        /// <summary>
        /// Gets the cached Unity player identifier string.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        public static string UnityIdentifier => _unityIdentifier.Value;

        /// <summary>
        /// Gets the Unity OS identifier string based on the current runtime platform.
        /// Uses reflection to detect Unity platform without compile-time dependency.
        /// </summary>
        /// <returns>Unity OS identifier string (e.g., "unity-windows") or empty string if Unity is not available.</returns>
        private static string GetOsIdentifier()
        {
            if (!UnityAdapter.IsAvailable)
            {
                return string.Empty;
            }

            try
            {
                // Use reflection to get platform value at runtime.
                // Platform values map to UnityEngine.RuntimePlatform enum.
                // At runtime unity player version >= 2019.x.x will be used.
                var platform = UnityAdapter.RuntimePlatform;

                switch (platform)
                {
                    case RuntimePlatform.OSXEditor:
                        return MacOS;
                    case RuntimePlatform.OSXPlayer:
                        return MacOS;
                    case RuntimePlatform.WindowsPlayer:
                        return Windows;
                    case RuntimePlatform.WindowsEditor:
                        return Windows;
                    case RuntimePlatform.IPhonePlayer:
                        return IOS;
                    case RuntimePlatform.Android:
                        return Android;
                    case RuntimePlatform.LinuxPlayer:
                        return Linux;
                    case RuntimePlatform.LinuxEditor:
                        return Linux;
                    case RuntimePlatform.WebGLPlayer:
                        return WebGL;
                    case RuntimePlatform.PS4:
                        return PS4;
                    case RuntimePlatform.XboxOne:
                        return Xbox;
                    case RuntimePlatform.TvOS:
                        return TvOS;
                    case RuntimePlatform.Switch:
                        return Switch;
                    case RuntimePlatform.PS5:
                        return PS5;
                }
            }
            catch
            {
                // ignored, If enum case is not found for future versions of unity
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the Unity player identifier string with version.
        /// </summary>
        /// <returns>Unity player identifier string (e.g., "unity/2019.4.40f1") or "unity" if version unavailable.</returns>
        private static string GetUnityIdentifier()
        {
            if (!UnityAdapter.IsAvailable)
            {
                return "unity";
            }

            var version = UnityAdapter.UnityVersion;
            return version.IsEmpty() ? "unity" : $"unity/{version}";
        }
    }
}
