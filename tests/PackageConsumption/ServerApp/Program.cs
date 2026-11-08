using System;
using System.Threading.Tasks;
using Ably.PubSub.Server;

namespace Ably.PubSub.PackageConsumption
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            try
            {
                // The key overload, which the shared checks do not reach. Constructing an HTTP client
                // makes no request, so this stays offline. (The realtime key overload is left out:
                // it auto-connects to the real endpoint.)
                var client = PubSubServer.CreateHttpClient("appId.keyId:keySecret");
                if (client == null)
                {
                    throw new InvalidOperationException("PubSubServer.CreateHttpClient(string) returned null");
                }

                Console.WriteLine("  ok: created the HTTP client from a key through the door");
            }
            catch (Exception ex)
            {
                Console.WriteLine("PACKAGE CONSUMPTION CHECKS FAILED");
                Console.WriteLine(ex);
                return 1;
            }

            return await ConsumptionChecks.RunAsync(
                "ably-pubsub-server",
                typeof(PubSubServer).Assembly,
                options => PubSubServer.CreateRealtimeClient(options),
                options => PubSubServer.CreateHttpClient(options));
        }
    }
}
