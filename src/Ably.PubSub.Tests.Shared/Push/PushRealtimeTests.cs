using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Push;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Push
{
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
    }
}
