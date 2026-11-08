using Ably.PubSub.Transport;
using System.Net.NetworkInformation;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;

namespace Ably.PubSub
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
