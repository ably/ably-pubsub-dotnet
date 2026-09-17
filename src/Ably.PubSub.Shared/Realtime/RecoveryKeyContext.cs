using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace IO.Ably.Shared.Realtime
{
    internal class RecoveryKeyContext
    {
        [JsonProperty("connectionKey")]
        public string ConnectionKey { get; set; }

        [JsonProperty("msgSerial")]
        public long MsgSerial { get; set; }

        [JsonProperty("channelSerials")]
        public IDictionary<string, string> ChannelSerials { get; set; }

        public string Encode()
        {
            return JsonHelper.Serialize(this);
        }

        public static RecoveryKeyContext Decode(string recover, ILogger logger = null)
        {
            try
            {
                return JsonHelper.Deserialize<RecoveryKeyContext>(recover);
            }
            catch (Exception e)
            {
                logger?.Warning($"Error deserializing recover - {recover}, setting it as null", e);
                return null;
            }
        }
    }
}
