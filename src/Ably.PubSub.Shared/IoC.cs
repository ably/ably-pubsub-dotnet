using System;
using System.IO;
using System.Reflection;
using IO.Ably.Push;
using IO.Ably.Transport;

namespace IO.Ably
{
    /// <summary>This class initializes dynamically-injected platform dependencies.</summary>
    internal static class IoC
    {
        private static readonly IPlatform Platform;

        /// <summary>Instantiate the IO.Ably.Platform type contributed by the platform head.</summary>
        static IoC()
        {
            try
            {
                // Platform.cs lives in the platform head (Ably.PubSub.Core or
                // Ably.PubSub.Core.NETFramework) and is compiled into the same assembly as this
                // shared code, so look it up in this assembly rather than loading one by name.
                // Loading by name silently degraded every platform service to its fallback the
                // moment the assembly was renamed from IO.Ably to Ably.PubSub.Core.
                var asm = typeof(IoC).GetTypeInfo().Assembly;
                var type = asm.GetType("IO.Ably.Platform");
                if (type != null)
                {
                    var obj = Activator.CreateInstance(type);
                    Platform = obj as IPlatform;
                }
                else
                {
                    DefaultLogger.Debug("Platform class does not exist. Defaulting Microsoft Websocket library.");
                }
            }
            catch (FileNotFoundException e)
            {
                DefaultLogger.Debug($"Assembly cannot be loaded. Defaulting Microsoft Websocket library. ({e.Message})");
            }
        }

        public static ITransportFactory TransportFactory => Platform?.TransportFactory ?? new MsWebSocketTransport.TransportFactory();

        public static void RegisterOsNetworkStateChanged() => Platform.RegisterOsNetworkStateChanged();

        public static Agent.PlatformRuntime PlatformId => Platform?.PlatformId ?? Agent.PlatformRuntime.Other;

        public static IMobileDevice MobileDevice
        {
            get
            {
                try
                {
                    return Platform.MobileDevice;
                }
                catch (Exception e) when (e is NotImplementedException)
                {
                    DefaultLogger.Error("Mobile Device is no supported on the current platform.", e);
                    return null;
                }
            }
            set => Platform.MobileDevice = value;
        }
    }
}
