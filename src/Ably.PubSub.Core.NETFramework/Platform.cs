using IO.Ably.Transport;
using System.Net.NetworkInformation;
using IO.Ably.Push;
using IO.Ably.Realtime;

namespace IO.Ably
{
    internal class Platform : IPlatform
    {
        private static readonly object _lock = new object();

        internal static bool HookedUpToNetworkEvents { get; set; }

        public Agent.PlatformRuntime PlatformId => Agent.PlatformRuntime.Framework;

        public ITransportFactory TransportFactory => null;

        public IMobileDevice MobileDevice { get; set; }

        public void RegisterOsNetworkStateChanged()
        {
            lock (_lock)
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
