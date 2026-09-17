using System;
using System.Reflection;

namespace IO.Ably.Unity
{
    /// <summary>
    /// Reflection-based Unity adapter with static methods.
    /// Uses reflection to access UnityEngine.Application APIs without compile-time dependency.
    /// Thread-safe lazy initialization on first method call.
    ///
    /// IL2CPP COMPATIBILITY:
    /// This reflection usage is fully compatible with Unity's IL2CPP compiler because:
    /// 1. Type.GetType() with assembly-qualified names works in IL2CPP for types that exist in the build
    /// 2. PropertyInfo.GetValue() on static properties is supported in IL2CPP
    /// 3. We only access public static properties (unityVersion, platform) which are preserved by IL2CPP
    /// 4. No dynamic code generation or emit is used - only metadata reflection
    /// 5. The UnityEngine.Application type and its properties are always preserved in Unity builds
    ///
    /// IMPORTANT FOR UNITY DEVELOPERS:
    /// - No link.xml preservation directives needed for UnityEngine.Application (it's always preserved)
    /// - Works across all Unity scripting backends: Mono, IL2CPP (iOS/Android/WebGL/Console)
    /// - Gracefully degrades in non-Unity environments (returns empty/default values)
    /// - Zero reflection overhead after first access due to Lazy caching
    /// - Safe for AOT platforms: no runtime code generation, only metadata queries.
    /// </summary>
    internal static class UnityAdapter
    {
        private static readonly Lazy<Type> _unityApplication =
            new Lazy<Type>(() => GetUnityApplication());

        private static readonly Lazy<string> _unityVersion =
            new Lazy<string>(() => GetUnityVersion());

        private static readonly Lazy<string> _runtimePlatform =
            new Lazy<string>(() => GetRuntimePlatform());

        /// <summary>
        /// Indicates whether Unity runtime is available.
        /// </summary>
        public static bool IsAvailable => _unityApplication.Value != null;

        /// <summary>
        /// Gets the cached Unity version string.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        public static string UnityVersion => _unityVersion.Value;

        /// <summary>
        /// Gets the cached runtime platform string value.
        /// The value is computed once on first access and cached for subsequent calls.
        /// </summary>
        public static string RuntimePlatform => _runtimePlatform.Value;

        /// <summary>
        /// Gets the UnityEngine.Application type using reflection.
        /// </summary>
        /// <returns>UnityEngine.Application Type or null if unavailable.</returns>
        private static Type GetUnityApplication()
        {
            try
            {
                // Attempt to load UnityEngine.Application type at runtime
                // The UnityEngine.Application class has been located in the UnityEngine.CoreModule assembly since Unity 2017.1
                // Returns null if the class is not found in UnityEngine.CoreModule
                return Type.GetType("UnityEngine.Application, UnityEngine.CoreModule", throwOnError: false);
            }
            catch
            {
                // Silently fail - Unity is not available
                return null;
            }
        }

        /// <summary>
        /// Gets the Unity version string using reflection.
        /// </summary>
        /// <returns>Unity version string (e.g., "2019.4.40f1") or empty string if unavailable.</returns>
        private static string GetUnityVersion()
        {
            var applicationType = _unityApplication.Value;
            if (applicationType == null)
            {
                return string.Empty;
            }

            try
            {
                // Get property info for unityVersion
                var version = applicationType.GetProperty(
                    "unityVersion",
                    BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;

                return version ?? string.Empty;
            }
            catch
            {
                // Ignore reflection errors
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the runtime platform as a string value using reflection.
        /// </summary>
        /// <returns>Platform string value (e.g., "OSXEditor", "WindowsPlayer") or empty string if unavailable.</returns>
        private static string GetRuntimePlatform()
        {
            var applicationType = _unityApplication.Value;
            if (applicationType == null)
            {
                return string.Empty;
            }

            try
            {
                // Get property info for platform and convert enum to string (e.g., "OSXEditor", "WindowsPlayer")
                var platform = applicationType.GetProperty(
                    "platform",
                    BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null)
                    ?.ToString();

                return platform ?? string.Empty;
            }
            catch
            {
                // Ignore reflection errors
                return string.Empty;
            }
        }
    }
}
