using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using IO.Ably.Push;
using IO.Ably.Realtime;
using IO.Ably.Transport;

namespace IO.Ably
{
    internal class Platform : IPlatform
    {
        private static readonly object Lock = new object();
        private readonly Lazy<Agent.PlatformRuntime> _platformId;

        public Platform()
        {
            _platformId = new Lazy<Agent.PlatformRuntime>(DetectPlatformRuntime);
        }

        internal static bool HookedUpToNetworkEvents { get; set; }

        // Use runtime detection via RuntimeInformation.FrameworkDescription
        // This detects the actual runtime version, not the compile-time target framework
        // This is important because netstandard2.0 assemblies can run on .NET 6/7/8/9+
        // and we want to report the actual runtime being used
        public Agent.PlatformRuntime PlatformId => _platformId.Value;

        private Agent.PlatformRuntime DetectPlatformRuntime()
        {
            // Default fallback for netstandard2.0 or unknown runtimes
            var platformId = Agent.PlatformRuntime.Netstandard20;

            try
            {
                var frameworkDescription = RuntimeInformation.FrameworkDescription;

                if (frameworkDescription.StartsWith(".NET 6.", StringComparison.OrdinalIgnoreCase))
                {
                    platformId = Agent.PlatformRuntime.Net6;
                }
                else if (frameworkDescription.StartsWith(".NET 7.", StringComparison.OrdinalIgnoreCase))
                {
                    platformId = Agent.PlatformRuntime.Net7;
                }
            }
            catch
            {
                // fall back to Netstandard20
            }

            return platformId;
        }

        public ITransportFactory TransportFactory => null;

        public IMobileDevice MobileDevice { get; set; }

        public void RegisterOsNetworkStateChanged()
        {
            lock (Lock)
            {
                if (HookedUpToNetworkEvents == false)
                {
                    NetworkChange.NetworkAvailabilityChanged += (sender, eventArgs) =>
                        Connection.NotifyOperatingSystemNetworkState(eventArgs.IsAvailable ? NetworkState.Online : NetworkState.Offline);
                }

                HookedUpToNetworkEvents = true;
            }
        }
    }
}
