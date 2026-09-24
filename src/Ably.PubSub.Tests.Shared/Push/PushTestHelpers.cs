using Ably.PubSub.Push;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests.Push
{
    public static class PushTestHelpers
    {
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
                ablyUrl = "https://" + client.Options.FullRestHost(),
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
