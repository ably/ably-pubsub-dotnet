using System.Threading.Tasks;
using Ably.PubSub.Device;

namespace Ably.PubSub.PackageConsumption
{
    internal static class Program
    {
        private static Task<int> Main()
        {
            return ConsumptionChecks.RunAsync(
                "ably-pubsub-device",
                typeof(PubSubDevice).Assembly,
                options => PubSubDevice.CreateClient(options),
                createHttpClient: null);
        }
    }
}
