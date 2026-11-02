using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Push;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Push
{
    [Collection(PushTestCollection.Name)]
    public class PushRealtimeTests : AblyRealtimeSpecs
    {
        [Fact]
        public async Task Initialiase_ShouldRestorePersistedState()
        {
            var mobileDevice = new FakeMobileDevice();
            var clientA = GetRealtimeClient(mobileDevice: mobileDevice);

            // Set the initial state and persist it.
            clientA.Push.InitialiseStateMachine();
            clientA.Push.StateMachine.CurrentState =
                new ActivationStateMachine.WaitingForNewPushDeviceDetails(clientA.Push.StateMachine);
            clientA.Push.StateMachine.PendingEvents.Enqueue(new ActivationStateMachine.CalledActivate());
            clientA.Push.StateMachine.PersistState();

            var clientB = GetRealtimeClient(mobileDevice: mobileDevice);
            clientB.Push.InitialiseStateMachine();

            clientB.Push.StateMachine.CurrentState.Should()
                .BeOfType<ActivationStateMachine.WaitingForNewPushDeviceDetails>();
            clientB.Push.StateMachine.PendingEvents.Should().NotBeEmpty();
        }

        public PushRealtimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // InitialiseStateMachine reads the client's Device, which sets the process-wide LocalDevice.Instance.
            // Clear it, as the other push test classes in this collection do, so a later test doesn't start with a
            // device it didn't create. It is cleared last, in case the base teardown disposes a client: that reads
            // Device and would set it again.
            LocalDevice.Instance = null;
        }
    }
}
