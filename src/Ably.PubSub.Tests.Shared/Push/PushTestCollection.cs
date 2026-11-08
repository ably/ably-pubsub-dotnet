using Xunit;

namespace Ably.PubSub.Tests.Push
{
    /// <summary>
    /// Test classes that touch the process-wide <c>LocalDevice.Instance</c> (directly, through a mobile device
    /// backed client, or through the activation state machine) must not run in parallel with each other or with
    /// any other test collection, otherwise one test's reset of the static instance races with another test's reads.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class PushTestCollection
    {
        public const string Name = "LocalDevice";
    }
}
