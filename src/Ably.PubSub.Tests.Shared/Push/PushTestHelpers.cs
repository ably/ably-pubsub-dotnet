using Ably.PubSub.Http;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Push
{
    internal static class PushTestHelpers
    {
        // Channel.Push is internal in 2.0 and no longer on IRealtimeChannel / IHttpChannel.
        public static PushChannel PushChannel(this IRealtimeChannel channel) => ((RealtimeChannel)channel).Push;

        public static PushChannel PushChannel(this IHttpChannel channel) => ((HttpChannel)channel).Push;

        public static LocalDevice GetTestLocalDevice(PubSubHttpClient client, string clientId = null)
        {
            var device = LocalDevice.Create(clientId);
            device.FormFactor = "phone";
            device.Platform = "android";
            device.Push.Recipient = JObject.FromObject(new
            {
                transportType = "ablyChannel",
                channel = "pushenabled:test",
                ablyKey = client.Options.Key,
                ablyUrl = "https://" + Defaults.GetPrimaryDomain(client.Options.Endpoint),
            });
            return device;
        }

        public static LocalDevice GetRegisteredLocalDevice(PubSubHttpClient client, string clientId = null, string identityToken = "token")
        {
            var device = GetTestLocalDevice(client, clientId);
            device.DeviceIdentityToken = identityToken;
            return device;
        }
    }
}
