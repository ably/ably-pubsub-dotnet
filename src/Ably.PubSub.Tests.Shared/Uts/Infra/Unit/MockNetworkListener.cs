using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Ably.PubSub.Realtime;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// The corpus' <c>MockNetworkListener</c> (no helper spec; <c>network_change_test.md</c>):
    /// <c>simulate_network_lost()</c> / <c>simulate_network_available()</c> for the clients it is bound to.
    /// </summary>
    public sealed class MockNetworkListener
    {
        private readonly object _lock = new object();
        private readonly List<PubSubRealtimeClient> _clients = new List<PubSubRealtimeClient>();

        /// <summary>The clients a simulated change reaches.</summary>
        public IReadOnlyList<PubSubRealtimeClient> Clients
        {
            get
            {
                lock (_lock)
                {
                    return _clients.ToList();
                }
            }
        }

        /// <summary>The specs' <c>simulate_network_lost()</c> (RTN20a).</summary>
        public void SimulateNetworkLost(
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => Notify(NetworkState.Offline, callerFilePath, callerLineNumber);

        /// <summary>The specs' <c>simulate_network_available()</c> (RTN20b).</summary>
        public void SimulateNetworkAvailable(
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
            => Notify(NetworkState.Online, callerFilePath, callerLineNumber);

        internal void Bind(PubSubRealtimeClient client)
        {
            lock (_lock)
            {
                _clients.Add(client);
            }
        }

        private void Notify(NetworkState state, string callerFilePath, int callerLineNumber)
        {
            var clients = Clients;
            if (clients.Count == 0)
            {
                throw UtsAssert.Failure(
                    "MockNetworkListener: no client is bound. InstallMock(listener) before building the client with Realtime(...)",
                    callerFilePath,
                    callerLineNumber,
                    null);
            }

            foreach (var client in clients.Where(c => !c.Disposed))
            {
                // The two lines of the SDK's private Connection.HandleNetworkStateChange.
                client.Connection.NetworkState = state;
                client.ConnectionManager.HandleNetworkStateChange(state);
            }
        }
    }
}
