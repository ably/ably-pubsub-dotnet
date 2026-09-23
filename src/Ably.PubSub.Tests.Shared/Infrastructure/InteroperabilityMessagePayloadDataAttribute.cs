using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Xunit.Sdk;

namespace IO.Ably.Tests.Infrastructure
{
    public class InteroperabilityMessagePayloadDataAttribute : DataAttribute
    {
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            var json = JObject.Parse(ResourceHelper.GetResource("messages-encoding.json"));

            foreach (var jToken in (json["messages"] as JArray).Children())
            {
                var message = (JObject)jToken;
                yield return new object[] { Protocol.Json, message };
                yield return new object[] { Defaults.Protocol, message };
            }
        }
    }
}
