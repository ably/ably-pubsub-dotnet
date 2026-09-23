using IO.Ably.Push;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Tests.Push
{
    public static class PushTestHelpers
    {
        public static LocalDevice GetTestLocalDevice(AblyRest client, string clientId = null)
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

        public static LocalDevice GetRegisteredLocalDevice(AblyRest client, string clientId = null, string identityToken = "token")
        {
            var device = GetTestLocalDevice(client, clientId);
            device.DeviceIdentityToken = identityToken;
            return device;
        }
    }
}
