using System.Threading.Tasks;
using Ably.PubSub.Server;

namespace Ably.PubSub.PackageConsumption
{
    internal static class Program
    {
        private static Task<int> Main()
        {
            return ConsumptionChecks.RunAsync(
                "ably-pubsub-server",
                typeof(PubSubServer).Assembly,
                options => PubSubServer.CreateRealtimeClient(options),
                options => PubSubServer.CreateHttpClient(options));
        }
    }
}
