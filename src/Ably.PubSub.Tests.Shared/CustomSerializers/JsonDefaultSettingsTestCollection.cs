using Xunit;

namespace Ably.PubSub.Tests.CustomSerializers
{
    /// <summary>
    /// Test classes that change the process-wide <c>JsonConvert.DefaultSettings</c> must not run in parallel with
    /// each other or with any other test collection, otherwise the app-level settings they install would leak into
    /// tests that serialise or deserialise json at the same time.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class JsonDefaultSettingsTestCollection
    {
        public const string Name = "JsonConvertDefaultSettings";
    }
}
