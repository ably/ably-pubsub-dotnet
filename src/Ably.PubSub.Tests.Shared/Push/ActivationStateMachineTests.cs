using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using IO.Ably.Push;
using IO.Ably.Tests.Infrastructure;

using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Push
{
    public static class ActivationStateMachineTests
    {
        public sealed class GeneralTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH4")]
            public async Task HandleEvent_WhenStateCannotHandleEvent_ShouldPutEventOnAQueue()
            {
                // Arrange
                var machine = GetStateMachine();
                var fakeState = new FakeState(machine, "FakeState");
                fakeState.CanHandleEventFunc = e => false;
                machine.CurrentState = fakeState;

                // Act
                machine.HandleEvent(new FakeEvent("Event"));

                // Assert
                machine.PendingEvents.Should().HaveCount(1);
            }

            [Fact]
            [Trait("spec", "RSH4")]
            public async Task HandleEvent_WhenTransitionReturnsNewState_ShouldUpdateCurrentStateToNewState()
            {
                // Arrange
                var machine = GetStateMachine();
                var fakeState = new FakeState(machine, "FakeState");
                machine.CurrentState = fakeState;
                var fakeEvent = new FakeEvent("FakeEvent");
                fakeEvent.NextState = new ActivationStateMachine.NotActivated(machine);

                // Act
                machine.HandleEvent(fakeEvent);

                // Assert
                machine.CurrentState.Should().BeSameAs(fakeEvent.NextState);
            }

            [Fact]
            [Trait("spec", "RSH4")]
            public async Task HandleEvent_WhenTransitionReturnsANewEvent_ShouldTransitionStateAndProcessNextEvent()
            {
                // Arrange
                var machine = GetStateMachine();
                var fakeState = new FakeState(machine, "FakeState1");
                var fakeState2 = new FakeState(machine, "FakeState2");

                machine.CurrentState = fakeState;
                var fakeEvent2 = new FakeEvent("FakeEvent2");
                fakeEvent2.NextState = new ActivationStateMachine.NotActivated(machine);
                var fakeEvent1 = new FakeEvent("FakeEvent1");
                fakeEvent1.NextState = fakeState2;
                fakeEvent1.GetNextEventFunc = async () => fakeEvent2;

                // Act
                await machine.HandleEvent(fakeEvent1);

                // Assert
                machine.CurrentState.Should().BeSameAs(fakeEvent2.NextState);
            }

            [Fact]
            [Trait("spec", "RSH4")]
            public async Task HandleEvent_WhenTransitionReturnsANewEvent_ShouldTransitionStateAndProcessNextEventAndQueueItIfNotHandled()
            {
                // Arrange
                var machine = GetStateMachine();
                var fakeState = new FakeState(machine, "FakeState1");
                var fakeState2 = new FakeState(machine, "FakeState2");
                fakeState2.CanHandleEventFunc = _ => false;

                machine.CurrentState = fakeState;
                var fakeEvent2 = new FakeEvent("FakeEvent2");
                fakeEvent2.NextState = new ActivationStateMachine.NotActivated(machine);
                var fakeEvent1 = new FakeEvent("FakeEvent1");
                fakeEvent1.NextState = fakeState2;
                fakeEvent1.GetNextEventFunc = async () => fakeEvent2;

                // Act
                await machine.HandleEvent(fakeEvent1);

                // Assert
                machine.CurrentState.Should().BeSameAs(fakeState2);

                // The second fake event should be queued because fakeState2 can't handle any events
                machine.PendingEvents.Should().HaveCount(1);
                machine.PendingEvents.Dequeue().Should().BeSameAs(fakeEvent2);
            }

            [Fact]
            [Trait("spec", "RSH4")]
            public async Task HandleEvent_WhenThereArePendingEvents_ShouldProcessCurrentEventFollowedByPendingEvents()
            {
                // Arrange
                var machine = GetStateMachine();
                var fakeState = new FakeState(machine, "FakeState1");
                var fakeState2 = new FakeState(machine, "FakeState2");

                machine.CurrentState = fakeState;
                var fakeEvent2 = new FakeEvent("FakeEvent1");
                fakeEvent2.NextState = new ActivationStateMachine.NotActivated(machine);
                var fakeEvent1 = new FakeEvent("FakeEvent2");
                fakeEvent1.NextState = fakeState2;
                machine.PendingEvents.Enqueue(fakeEvent2);

                // Act
                await machine.HandleEvent(fakeEvent1);

                // Assert
                machine.CurrentState.Should().BeOfType<ActivationStateMachine.NotActivated>();
            }

            [Fact(DisplayName = "With two events, the first one is initially put in the pending queue and is processed after the second one has caused a transition")]
            [Trait("spec", "RSH5")]
            public async Task HandleEvent_WithTwoEvents_ShouldBeProcessedSequentially()
            {
                // Arrange
                var machine = GetStateMachine();

                // the initial state can only handle the event which will transition to the secondState
                // the second state can handle any event and will happily handle ToSecondState event
                // which will come from the PendingEvents queue because InitialState will put it there.
                var initialState = new FakeState(machine, "InitialState");
                initialState.CanHandleEventFunc = @event => ((FakeEvent)@event).Name == "ToSecondState";
                var secondState = new FakeState(machine, "SecondState");
                var finalState = new FakeState(machine, "FinalState");

                // events
                var transitionToSecondStateEvent = new FakeEvent("ToSecondState");
                transitionToSecondStateEvent.NextState = secondState;
                var toFinalState = new FakeEvent("ToFinalState");
                toFinalState.NextState = finalState;

                machine.CurrentState = initialState;

                // Act
                await machine.HandleEvent(toFinalState);
                await machine.HandleEvent(transitionToSecondStateEvent);

                // Assert
                machine.CurrentState.Should().BeSameAs(finalState);
            }

            [Fact]
            public async Task UpdateRegistrationTokenWithSameToken_DoesNotHandleEvent()
            {
                // Arrange
                var machine = GetStateMachine();
                const string token = "token";

                ActivationStateMachine.Event handledEvent = null;
                var initialState = new FakeState(machine, "InitialState");
                initialState.CanHandleEventFunc = @event =>
                {
                    handledEvent = @event;
                    return true;
                };
                machine.CurrentState = initialState;

                machine.LocalDevice.RegistrationToken = new RegistrationToken("apns", token);

                // Act
                machine.UpdateRegistrationToken(Result.Ok(new RegistrationToken("apns", token)));

                // Assert
                handledEvent.Should().BeNull();
            }

            [Fact]
            public async Task UpdateRegistrationTokenWithNewToken_ShouldSaveItAndPushEvent_GotPushDeviceDetails()
            {
                // Arrange
                var machine = GetStateMachine();
                const string token = "token";
                const string tokenValue = "newToken";

                ActivationStateMachine.Event handledEvent = null;
                var initialState = new FakeState(machine, "InitialState");
                initialState.CanHandleEventFunc = @event =>
                {
                    handledEvent = @event;
                    return true;
                };
                machine.CurrentState = initialState;

                machine.LocalDevice.RegistrationToken = new RegistrationToken("fcm", token);

                machine.UpdateRegistrationToken(Result.Ok(new RegistrationToken("fcm", tokenValue)));

                machine.LocalDevice.RegistrationToken.Token.Should().Be(tokenValue);
                handledEvent.Should().NotBeNull().And.BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
            }

            [Fact]
            public async Task UpdateRegistrationTokenWithError_ShouldPushEvent_GettingPushDeviceDetailsFailedAndPassTheError()
            {
                // Arrange
                var machine = GetStateMachine();
                var error = new ErrorInfo("TestError");

                ActivationStateMachine.Event handledEvent = null;
                var initialState = new FakeState(machine, "InitialState");
                initialState.CanHandleEventFunc = @event =>
                {
                    handledEvent = @event;
                    return true;
                };
                machine.CurrentState = initialState;

                machine.UpdateRegistrationToken(Result.Fail<RegistrationToken>(error));
                handledEvent.Should().NotBeNull()
                    .And.BeOfType<ActivationStateMachine.GettingPushDeviceDetailsFailed>();

                ((ActivationStateMachine.GettingPushDeviceDetailsFailed)handledEvent).Reason.Should().BeSameAs(error);
            }

            public GeneralTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }

            public AblyRest RestClient { get; }

            public FakeMobileDevice MobileDevice { get; set; }

            private ActivationStateMachine GetStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, (restClient ?? RestClient).Logger);
                return stateMachine;
            }

            [DebuggerDisplay("FakeState - {Name}")]
            private class FakeEvent : ActivationStateMachine.Event
            {
                public string Name { get; }

                public ActivationStateMachine.State NextState { get; set; }

                public Func<Task<ActivationStateMachine.Event>> GetNextEventFunc { get; set; } = async () => null;

                public FakeEvent(string name)
                {
                    Name = name;
                }

                public override string ToString()
                {
                    return $"FakeEvent - {Name}";
                }
            }

            [DebuggerDisplay("FakeState - {Name}")]
            private class FakeState : ActivationStateMachine.State
            {
                public FakeState(ActivationStateMachine machine, string name, bool persist = true)
                    : base(machine)
                {
                    Name = name;
                    Persist = persist;
                }

                public Func<ActivationStateMachine.Event, bool> CanHandleEventFunc = @event => true;

                public string Name { get; }

                public override bool Persist { get; }

                public override bool CanHandleEvent(ActivationStateMachine.Event @event)
                {
                    return CanHandleEventFunc(@event);
                }

                public override async Task<(ActivationStateMachine.State, Func<Task<ActivationStateMachine.Event>>)> Transition(ActivationStateMachine.Event @event)
                {
                    switch (@event)
                    {
                        case FakeEvent fakeEvent:
                            return (fakeEvent.NextState, fakeEvent.GetNextEventFunc);
                        default:
                            throw new AblyException("This is a fake state and can only handle fake events.");
                    }
                }

                public override string ToString()
                {
                    return $"FakeState - {Name}";
                }
            }
        }

        public sealed class NotActivatedStateTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3a1")]
            public void CalledDeactivateEvent_CanBeHandled()
            {
                var state = GetState();

                state.CanHandleEvent(new ActivationStateMachine.CalledDeactivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3a1a")]
            public async Task CalledDeactivateEvent_ShouldTriggerDeactivatedCallback()
            {
                var state = GetState();
                var awaiter = new TaskCompletionAwaiter();
                bool callbackExecuted = false;
                MobileDevice.Callbacks.DeactivatedCallback = error =>
                {
                    callbackExecuted = true;
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                await state.Transition(new ActivationStateMachine.CalledDeactivate());

                (await awaiter.Task).Should().BeTrue();
                callbackExecuted.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3a1b")]
            public async Task CalledDeactivateEvent_ShouldKeepTheSameState()
            {
                var state = GetState();

                var (nextState, eventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeSameAs(state);
                (await eventFunc()).Should().BeNull(); // No more events should be generated.
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            public void CalledActivateEvent_CanBeHandled()
            {
                var state = GetState();

                state.CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2a4")]
            public async Task
                WithCalledActivate_WhenLocalDeviceHasDeviceIdentityToken_ShouldCheckClientIdCompatibility()
            {
                var restClient = GetRestClient(null, options => options.ClientId = "999", MobileDevice);
                var (state, stateMachine) = GetStateAndStateMachine(restClient);

                stateMachine.LocalDevice = new LocalDevice() { DeviceIdentityToken = "token", ClientId = "123" };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());
                nextState.Should().BeOfType<ActivationStateMachine.WaitingForRegistrationSync>();

                var nextEvent = await nextEventFunc();
                nextEvent.Should().BeOfType<ActivationStateMachine.SyncRegistrationFailed>()
                    .Which.Reason.Code.Should().Be(ErrorCodes.ActivationFailedClientIdMismatch);
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2a3")]
            public async Task WithCalledActivate_WhenLocalDeviceHasDeviceIdentityToken_AndSuccessfulDeviceRegistration_ShouldReturnRegistrationSyncedNextEvent()
            {
                var restClient = GetRestClient(
                    request =>
                    {
                        if (request.Url.StartsWith("/push/deviceRegistrations"))
                        {
                            return Task.FromResult(new AblyResponse() { StatusCode = HttpStatusCode.OK, TextResponse = LocalDevice.Create().ToJson() });
                        }

                        return Task.FromResult(new AblyResponse());
                    },
                    mobileDevice: MobileDevice);

                var (state, stateMachine) = GetStateAndStateMachine(restClient);
                var localDevice = LocalDevice.Create();
                localDevice.DeviceIdentityToken = "token";
                stateMachine.LocalDevice = localDevice;

                var (_, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                var nextEvent = await nextEventFunc();
                nextEvent.Should().BeOfType<ActivationStateMachine.RegistrationSynced>();
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2a3")]
            public async Task WithCalledActivate_WhenLocalDeviceHasDeviceIdentityToken_AndFailedDeviceRegistration_ShouldReturnSyncRegistrationFailedNextEvent()
            {
                var restClient = GetRestClient(
                    request =>
                    {
                        if (request.Url.StartsWith("/push/deviceRegistrations"))
                        {
                            throw new AblyException("Invalid request");
                        }

                        return Task.FromResult(new AblyResponse());
                    },
                    mobileDevice: MobileDevice);

                var (state, stateMachine) = GetStateAndStateMachine(restClient);

                var localDevice = LocalDevice.Create();
                localDevice.DeviceIdentityToken = "token";
                stateMachine.LocalDevice = localDevice;

                var (_, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                var nextEvent = await nextEventFunc();
                nextEvent.Should().BeOfType<ActivationStateMachine.SyncRegistrationFailed>();
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2b")]
            public async Task WithCalledActivate_WithoutCreatedLocalDevice_ShouldCreateNewLocalDeviceAndPersistIt()
            {
                var (state, _) = GetStateAndStateMachine(GetRestClient(null, options => options.ClientId = "123", MobileDevice));

                await state.Transition(new ActivationStateMachine.CalledActivate());

                MobileDevice.GetPreference(PersistKeys.Device.DeviceId, PersistKeys.Device.SharedName)
                    .Should().NotBeEmpty();

                MobileDevice.GetPreference(PersistKeys.Device.ClientId, PersistKeys.Device.SharedName)
                    .Should().NotBeEmpty();

                MobileDevice.GetPreference(PersistKeys.Device.DeviceSecret, PersistKeys.Device.SharedName)
                    .Should().NotBeEmpty();
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2d")]
            public async Task WithCalledActivate_WithoutCreatedLocalDevice_ShouldTriggerGetRegistrationToken()
            {
                var (state, stateMachine) = GetStateAndStateMachine(GetRestClient(null, options => options.ClientId = "123", MobileDevice));

                stateMachine.LocalDevice = new LocalDevice();

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                (await nextEventFunc()).Should().BeNull(); // No next next
                nextState.Should().BeOfType<ActivationStateMachine.WaitingForPushDeviceDetails>();
                MobileDevice.RequestRegistrationTokenCalled.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3a2")]
            [Trait("spec", "RSH3a2c")]
            public async Task WithCalledActivate_WhenLocalDeviceHasPushDetails_ShouldTriggerGotPushDeviceDetailsEvent()
            {
                var (state, stateMachine) = GetStateAndStateMachine();

                var localDevice = LocalDevice.Create("123");
                localDevice.RegistrationToken = new RegistrationToken("fcm", "token");
                stateMachine.LocalDevice = localDevice;

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
                nextState.Should().BeOfType<ActivationStateMachine.WaitingForPushDeviceDetails>();
            }

            [Fact]
            [Trait("spec", "RSH3a3")]
            [Trait("spec", "RSH3a3a")]
            public async Task WithGotPushDeviceDetails_ShouldReturnTheSameStateWithoutAnyFurtherEvents()
            {
                var state = GetState();

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                (await nextEventFunc()).Should().BeNull();
                nextState.Should().BeSameAs(state);
            }

            [Fact]
            [Trait("spec", "RSH3a3")]
            public async Task NotActivated_ShouldBeAbleToHandleGotPushDeviceDetails()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.GotPushDeviceDetails()).Should().BeTrue();
            }

            [Fact]
            public async Task NotActivated_ShouldThrowIfItCannotHandleEventType()
            {
                var state = GetState();
                Func<Task> transitionWithWrongEvent = () => state.Transition(new ActivationStateMachine.RegistrationSynced());

                await transitionWithWrongEvent.Should().ThrowAsync<AblyException>();
            }

            private ActivationStateMachine.NotActivated GetState()
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.NotActivated(stateMachine);
            }

            private (ActivationStateMachine.NotActivated, ActivationStateMachine) GetStateAndStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.NotActivated(stateMachine), stateMachine);
            }

            public NotActivatedStateTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            public AblyRest RestClient { get; }

            public FakeMobileDevice MobileDevice { get; set; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3b")]
        public sealed class WaitingForPushDeviceDetailsTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3b1")]
            public async Task ShouldBeAbleToHandleCalledActivate()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3b1")]
            public async Task WithCalledActivateEvent_ShouldReturnSameState()
            {
                var state = GetState();
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeSameAs(state);
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3b2")]
            public async Task ShouldBeAbleToHandleCalledDeactivate()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.CalledDeactivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3b2a")]
            public async Task CalledDeactivateEvent_ShouldTriggerDeactivatedCallback()
            {
                var state = GetState();
                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.DeactivatedCallback = error =>
                {
                    error.Should().BeNull();
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                await state.Transition(new ActivationStateMachine.CalledDeactivate());
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3b2b")]
            public async Task CalledDeactivateEvent_ShouldTransitionToNotActivatedWithNoFurtherEvents()
            {
                var state = GetState();
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeOfType<ActivationStateMachine.NotActivated>();
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3b3")]
            public async Task ShouldBeAbleToHandleGotPushDeviceDetails()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.GotPushDeviceDetails()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3b3b")]
            [Trait("spec", "RSH3b3d")]
            public async Task GotPushDeviceDetails_ShouldSetStateToWaitingForDeviceRegistrationAndRegisterDeviceWithAblyRestApi()
            {
                var (state, machine) = GetStateAndStateMachine();
                const string token = "token";

                machine.LocalDevice = LocalDevice.Create(mobileDevice: MobileDevice);
                machine.LocalDevice.Push.Recipient = new JObject();
                RestClient.ExecuteHttpRequest = request =>
                {
                    var localDevice = JObject.FromObject(new LocalDevice());
                    localDevice["deviceIdentityToken"] = JObject.FromObject(new { token });
                    var response = localDevice.ToString();

                    return Task.FromResult(new AblyResponse()
                        { StatusCode = HttpStatusCode.OK, TextResponse = response });
                };
                var (nextState, nextStateFunction) =
                    await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeviceRegistration>();
                (await nextStateFunction()).Should().BeOfType<ActivationStateMachine.GotDeviceRegistration>().Which
                    .DeviceIdentityToken.Should().Be(token);
            }

            [Fact]
            [Trait("spec", "RSH3b3c")]
            [Trait("spec", "RSH3b3d")]
            public async Task GotPushDeviceDetails_WhenRegisterDeviceFails_ShouldReturnGettingDeviceRegistrationFailedEventWithErrorDetailsInTheEvent()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create(mobileDevice: MobileDevice);
                machine.LocalDevice.Push.Recipient = new JObject();
                RestClient.ExecuteHttpRequest = request => throw new AblyException("Error", ErrorCodes.InternalError);

                var (nextState, nextStateFunction) =
                    await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeviceRegistration>();
                (await nextStateFunction()).Should().BeOfType<ActivationStateMachine.GettingDeviceRegistrationFailed>()
                    .Which
                    .Reason.Message.Should().Be("Error");
            }

            [Fact]
            [Trait("spec", "RSH3b4")]
            public async Task ShouldBeAbleToHandleGettingPushDeviceDetailsFailed()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.GettingPushDeviceDetailsFailed(new ErrorInfo())).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3b4")]
            [Trait("spec", "RSH3c3b")]
            public async Task GettingPushDeviceDetailsFailed_ShouldTransitionToNotActivated()
            {
                var state = GetState();

                var (nextState, nextEventFunc) =
                    await state.Transition(new ActivationStateMachine.GettingPushDeviceDetailsFailed(new ErrorInfo()));

                nextState.Should().BeOfType<ActivationStateMachine.NotActivated>();
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3b4")]
            [Trait("spec", "RSH3c3a")]
            public async Task GettingPushDeviceDetailsFailed_ShouldTriggerActivatedCallbackAndPassErrorInfo()
            {
                var state = GetState();

                var errorInfo = new ErrorInfo("Reason");
                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeSameAs(errorInfo);
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                await state.Transition(new ActivationStateMachine.GettingPushDeviceDetailsFailed(errorInfo));
                (await awaiter.Task).Should().BeTrue();
            }

            public WaitingForPushDeviceDetailsTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.WaitingForPushDeviceDetails GetState()
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.WaitingForPushDeviceDetails(stateMachine);
            }

            private (ActivationStateMachine.WaitingForPushDeviceDetails, ActivationStateMachine) GetStateAndStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.WaitingForPushDeviceDetails(stateMachine), stateMachine);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; set; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3c")]
        public sealed class WaitingForDeviceRegistrationTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3c1")]
            public async Task ShouldBeAbleToHandleCallActivateEvent()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3c1a")]
            public async Task WithCalledActivated_ShouldNotChangeState()
            {
                var state = GetState();

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeSameAs(state);
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3c2")]
            public async Task ShouldBeAbleToHandleGotDeviceRegistration()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.GotDeviceRegistration(string.Empty)).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3c2a")]
            [Trait("spec", "RSH8c")]
            public async Task WithGotDeviceRegistration_ShouldUpdateDeviceDetails()
            {
                var (state, machine) = GetStateAndStateMachine();

                await state.Transition(new ActivationStateMachine.GotDeviceRegistration("token"));

                // (RSH3c2a)
                machine.LocalDevice.DeviceIdentityToken.Should().Be("token");
                // Check it was saved in device storage as well
                MobileDevice.GetPreference(PersistKeys.Device.DeviceToken, PersistKeys.Device.SharedName).Should()
                    .Be("token");
            }

            [Fact]
            [Trait("spec", "RSH3c2b")]
            [Trait("spec", "RSH3c2c")]
            public async Task WithGotDeviceRegistration_ShouldTriggerCallbackAndUpdateStateToWaitingForNewPushDeviceDetails()
            {
                var state = GetState();

                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeNull();
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.GotDeviceRegistration("token"));

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForNewPushDeviceDetails>();

                (await nextEventFunc()).Should().BeNull();
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3c3")]
            public async Task ShouldBeAbleHandleGettingDeviceRegistrationFailedEvent()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.GettingDeviceRegistrationFailed(new ErrorInfo()))
                    .Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3c3a")]
            [Trait("spec", "RSH3c3b")]
            public async Task WithGettingDeviceRegistrationFailed_ShouldTriggerCallbackWithErrorAndTransitionToNotActivated()
            {
                var state = GetState();

                var error = new ErrorInfo("Reason");
                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeSameAs(error);
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.GettingDeviceRegistrationFailed(error));

                nextState.Should().BeOfType<ActivationStateMachine.NotActivated>();
                (await nextEventFunc()).Should().BeNull();
                (await awaiter.Task).Should().BeTrue();
            }

            public WaitingForDeviceRegistrationTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.WaitingForDeviceRegistration GetState()
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.WaitingForDeviceRegistration(stateMachine);
            }

            private (ActivationStateMachine.WaitingForDeviceRegistration, ActivationStateMachine) GetStateAndStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.WaitingForDeviceRegistration(stateMachine), stateMachine);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; set; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3d")]
        public sealed class WaitingForNewPushDeviceDetailsTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3d1")]
            public async Task ShouldHandleCalledActivate()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d1a")]
            [Trait("spec", "RSH3d1b")]
            public async Task WithCalledActivate_ShouldTriggerCallbackAndReturnTheSameState()
            {
                var state = GetState();

                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.ActivatedCallback = error =>
                {
                    error.Should().BeNull();
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeSameAs(state);
                (await nextEventFunc()).Should().BeNull();

                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d2")]
            public async Task ShouldHandleCalledDeactivate()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.CalledDeactivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d2b")]
            [Trait("spec", "RSH3d2c")]
            public async Task WithCalledDeactivateEvent_ShouldTransitionToWaitingForDeregistrationAndCallRemoveDeviceRestApi()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();

                var awaiter = new TaskCompletionAwaiter();
                RestClient.ExecuteHttpRequest = request =>
                {
                    request.Method.Should().Be(HttpMethod.Delete);

                    awaiter.SetCompleted();
                    return Task.FromResult(new AblyResponse() { StatusCode = HttpStatusCode.OK, TextResponse = string.Empty });
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeregistration>().Which.PreviousState.Should().BeSameAs(state);

                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.Deregistered>();
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d2b")]
            [Trait("spec", "RSH3d2c")]
            public async Task WithCalledDeactivateEvent_WhenCallToRemoveDeviceFails_ShouldTransitionToWaitingAndTheNextEventShouldBeDeregistrationFailed()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();

                var error = new ErrorInfo();
                RestClient.ExecuteHttpRequest = request => throw new AblyException(error);

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeregistration>().Which.PreviousState.Should().BeSameAs(state);

                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.DeregistrationFailed>().Which.Reason.Should().BeSameAs(error);
            }

            [Fact]
            [Trait("spec", "RSH3d3")]
            public async Task ShouldHandleGotPushDeviceDetails()
            {
                GetState().CanHandleEvent(new ActivationStateMachine.GotPushDeviceDetails()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d3b")]
            public async Task WithGotPushDeviceDetailsEvent_ShouldPatchDeviceDetails()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();
                machine.LocalDevice.DeviceIdentityToken = "token";
                machine.LocalDevice.Push.Recipient = new JObject();

                var awaiter = new TaskCompletionAwaiter();
                RestClient.ExecuteHttpRequest = request =>
                {
                    request.Url.Should().Be($"/push/deviceRegistrations/{machine.LocalDevice.Id}");
                    request.Method.Should().Be(new HttpMethod("PATCH"));

                    awaiter.SetCompleted();
                    return Task.FromResult(new AblyResponse() { StatusCode = HttpStatusCode.OK, TextResponse = string.Empty });
                };

                var (_, nextEventFunc) = await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                await nextEventFunc();
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3d3c")]
            [Trait("spec", "RSH3d3d")]
            public async Task WithGotPushDeviceDetailsEvent_WhenPatchDeviceDetailsSucceeds_ShouldTransitionToWaitingForRegistrationSyncAndProduceRegistrationSyncedEvent()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();
                machine.LocalDevice.DeviceIdentityToken = "token";
                machine.LocalDevice.Push.Recipient = new JObject();

                RestClient.ExecuteHttpRequest = request => Task.FromResult(new AblyResponse() { StatusCode = HttpStatusCode.OK, TextResponse = string.Empty });

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForRegistrationSync>().Which.FromEvent.Should().BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.RegistrationSynced>();
            }

            [Fact]
            [Trait("spec", "RSH3d3c")]
            [Trait("spec", "RSH3d3d")]
            public async Task WithGotPushDeviceDetailsEvent_WhenPatchDeviceDetailsFails_ShouldTransitionToWaitingForRegistrationSyncAndProduceSyncRegistrationFailedEvent()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();
                machine.LocalDevice.DeviceIdentityToken = "token";
                machine.LocalDevice.Push.Recipient = new JObject();

                var error = new ErrorInfo();
                RestClient.ExecuteHttpRequest = request => throw new AblyException(error);

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.GotPushDeviceDetails());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForRegistrationSync>().Which.FromEvent.Should().BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.SyncRegistrationFailed>().Which.Reason.Should().BeSameAs(error);
            }

            public WaitingForNewPushDeviceDetailsTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.WaitingForNewPushDeviceDetails GetState()
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.WaitingForNewPushDeviceDetails(stateMachine);
            }

            private (ActivationStateMachine.WaitingForNewPushDeviceDetails, ActivationStateMachine) GetStateAndStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.WaitingForNewPushDeviceDetails(stateMachine), stateMachine);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; set; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3e")]
        public sealed class WaitingForRegistrationSync : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3e1")]
            public void ShouldBeAbleToHandleCallActivate_WhenFromEventWasNotCallActivate()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());
                state.CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3e1")]
            public void ShouldNotBeAbleToHandleCallActivate_WhenFromEventIsCallActivate()
            {
                var state = GetState(new ActivationStateMachine.CalledActivate());
                state.CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeFalse();
            }

            [Fact]
            [Trait("spec", "RSH3e1a")]
            public async Task WithCalledActivateEvent_ShouldCallActivateCallbackAndNotChangeState()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());
                var awaiter = new TaskCompletionAwaiter();

                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeNull();
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeSameAs(state);
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3e2")]
            public void ShouldBeAbleToHandleRegistrationSynced()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());
                state.CanHandleEvent(new ActivationStateMachine.RegistrationSynced()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3e2a")]
            [Trait("spec", "RSH3e2b")]
            public async Task WithRegistrationSyncedEventAndFromEventCalledActivate_ShouldTriggerCallbackAndTransitionToWaitingForRegistrationSync()
            {
                var state = GetState(new ActivationStateMachine.CalledActivate());

                var awaiter = new TaskCompletionAwaiter();

                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeNull();
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.RegistrationSynced());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForNewPushDeviceDetails>();
                (await nextEventFunc()).Should().BeNull();

                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3e2a")]
            public async Task WithRegistrationSyncedEventAndFromEventIsNotCalledActivate_ShouldTriggerNotTriggerCallbackAnd_ShouldTransitionToWaitingForRegistrationSync()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());

                var awaiter = new TaskCompletionAwaiter(500);

                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.RegistrationSynced());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForNewPushDeviceDetails>();
                (await nextEventFunc()).Should().BeNull();
                (await awaiter.Task).Should().BeFalse();
            }

            [Fact]
            [Trait("spec", "RSH3e2")]
            public void ShouldBeAbleToHandleSyncRegistrationFailed()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());
                state.CanHandleEvent(new ActivationStateMachine.SyncRegistrationFailed(new ErrorInfo())).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3e3b")]
            [Trait("spec", "RSH3e3c")]
            public async Task WithSyncRegistrationFailedEventAndFromEventIsCalledActivate_ShouldTriggerShouldTriggerCallbackAndTransitionToAfterRegistrationSyncFailed()
            {
                var state = GetState(new ActivationStateMachine.CalledActivate());

                var awaiter = new TaskCompletionAwaiter();
                var error = new ErrorInfo();
                MobileDevice.Callbacks.ActivatedCallback = reason =>
                {
                    reason.Should().BeSameAs(error);
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.SyncRegistrationFailed(error));

                nextState.Should().BeOfType<ActivationStateMachine.AfterRegistrationSyncFailed>();
                (await nextEventFunc()).Should().BeNull();
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3e3a")]
            [Trait("spec", "RSH3e3b")]
            public async Task WithSyncRegistrationFailedEventAndFromEventIsNOTCalledActivate_ShouldTriggerShouldTriggerSyncFailedCallbackAndTransitionToAfterRegistrationSyncFailed()
            {
                var state = GetState(new ActivationStateMachine.GotPushDeviceDetails());

                var awaiter = new TaskCompletionAwaiter();
                var error = new ErrorInfo();
                MobileDevice.Callbacks.SyncRegistrationFailedCallback = reason =>
                {
                    reason.Should().BeSameAs(error);
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.SyncRegistrationFailed(error));

                nextState.Should().BeOfType<ActivationStateMachine.AfterRegistrationSyncFailed>();
                (await nextEventFunc()).Should().BeNull();
                (await awaiter.Task).Should().BeTrue();
            }

            public WaitingForRegistrationSync(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.WaitingForRegistrationSync GetState(ActivationStateMachine.Event fromEvent)
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.WaitingForRegistrationSync(stateMachine, fromEvent);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; set; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3f")]
        public sealed class AfterRegistrationSyncFailedTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3f1")]
            [Trait("spec", "RSH3f2")]
            public void ShouldBeAbleToHandleCalledActivate_GotPushDeviceDetails_And_CalledDeactivate()
            {
                var state = GetState();
                state.CanHandleEvent(new ActivationStateMachine.CalledActivate()).Should().BeTrue();
                state.CanHandleEvent(new ActivationStateMachine.GotPushDeviceDetails()).Should().BeTrue();
                state.CanHandleEvent(new ActivationStateMachine.CalledDeactivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3f1a")]
            public async Task WithCalledActivateEvent_ShouldValidateRegistrationAndTransitionToWaitingForRegistrationSync()
            {
                var (state, machine) = GetStateAndStateMachine();

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForRegistrationSync>();
                await nextEventFunc();
                machine.ValidateRegistrationCalled.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3f1a")]
            public async Task WithGotPushDeviceDetails_ShouldValidateRegistrationAndTransitionToWaitingForRegistrationSync()
            {
                var (state, machine) = GetStateAndStateMachine();

                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledActivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForRegistrationSync>();
                await nextEventFunc();
                machine.ValidateRegistrationCalled.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3f2a")]
            public async Task WithDeactivateDevice_ShouldCallDeregisterRestAPIAndTransitionToWaitingForDeregistrationWithDeregisteredEventOnSuccess()
            {
                var (state, machine) = GetStateAndStateMachine();

                machine.LocalDevice = LocalDevice.Create();
                var awaiter = new TaskCompletionAwaiter();
                RestClient.ExecuteHttpRequest = request =>
                {
                    request.Url.Should().StartWith("/push");
                    request.Method.Should().Be(HttpMethod.Delete);

                    awaiter.SetCompleted();
                    return Task.FromResult(new AblyResponse());
                };
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeregistration>();
                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.Deregistered>();
                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3f2a")]
            public async Task WithDeactivateDevice_ShouldCallDeregisterRestAPIAndTransitionToWaitingForDeregistrationWithDeregistrationFailedEventOnFailure()
            {
                var (state, machine) = GetStateAndStateMachine();
                machine.LocalDevice = LocalDevice.Create();
                var error = new ErrorInfo();
                RestClient.ExecuteHttpRequest = request => throw new AblyException(error);
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForDeregistration>();
                (await nextEventFunc()).Should().BeOfType<ActivationStateMachine.DeregistrationFailed>().Which.Reason.Should().BeSameAs(error);
            }

            public AfterRegistrationSyncFailedTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.AfterRegistrationSyncFailed GetState()
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.AfterRegistrationSyncFailed(stateMachine);
            }

            private (ActivationStateMachine.AfterRegistrationSyncFailed, StubActivationStateMachine) GetStateAndStateMachine(AblyRest restClient = null)
            {
                var stateMachine = new StubActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.AfterRegistrationSyncFailed(stateMachine), stateMachine);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; set; }

            private class StubActivationStateMachine : ActivationStateMachine
            {
                internal StubActivationStateMachine(AblyRest restClient, ILogger logger = null)
                    : base(restClient, logger)
                {
                }

                public bool ValidateRegistrationCalled { get; private set; }

                protected override async Task<Event> ValidateRegistration()
                {
                    ValidateRegistrationCalled = true;
                    return new RegistrationSynced();
                }
            }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        [Trait("spec", "RSH3g")]
        public sealed class WaitingForDeregistrationTests : MockHttpRestSpecs, IDisposable
        {
            [Fact]
            [Trait("spec", "RSH3g1")]
            public void ShouldBeAbleToHandleCalledDeactivate()
            {
                var state = GetState(machine => new ActivationStateMachine.NotActivated(machine));
                state.CanHandleEvent(new ActivationStateMachine.CalledDeactivate()).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3g1a")]
            public async Task WithCalledDeactivateEvent_ShouldTransitionToItself()
            {
                var state = GetState(machine => new ActivationStateMachine.NotActivated(machine));
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.CalledDeactivate());

                nextState.Should().BeSameAs(state);
                (await nextEventFunc()).Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RSH3g2")]
            public void ShouldBeAbleToHandleDeregistered()
            {
                var state = GetState(machine => new ActivationStateMachine.NotActivated(machine));
                state.CanHandleEvent(new ActivationStateMachine.Deregistered()).Should().BeTrue();
            }

            [Fact(Skip = "Intermittently fails")]
            [Trait("spec", "RSH3g2а")]
            [Trait("spec", "RSH3g2c")]
            public async Task WithDeregistered_ShouldClearLocalDeviceAndTransitionToNotActivated()
            {
                var (state, machine) = GetStateAndStateMachine(stateMachine => new ActivationStateMachine.NotActivated(stateMachine));
                var machineLocalDevice = machine.LocalDevice;

                MobileDevice.Settings.Should().NotBeEmpty();
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.Deregistered());

                nextState.Should().BeOfType<ActivationStateMachine.NotActivated>();
                (await nextEventFunc()).Should().BeNull();
                machine.LocalDevice.Should().NotBeSameAs(machineLocalDevice);
                machine.LocalDevice.Id.Should().NotBe(machineLocalDevice.Id);
            }

            [Fact]
            [Trait("spec", "RSH3g2b")]
            public async Task WithDeregistered_ShouldTriggerDeactivatedCallback()
            {
                var state = GetState(m => new ActivationStateMachine.NotActivated(m));

                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.DeactivatedCallback = error =>
                {
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };

                await state.Transition(new ActivationStateMachine.Deregistered());

                (await awaiter.Task).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3g3")]
            public void ShouldBeAbleToHandleDeregistrationFailed()
            {
                var state = GetState(machine => new ActivationStateMachine.NotActivated(machine));
                state.CanHandleEvent(new ActivationStateMachine.DeregistrationFailed(new ErrorInfo())).Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSH3g3a")]
            [Trait("spec", "RSH3g3b")]
            public async Task WithDeregistrationFailedEvent_ShouldCallDeactivateCallbackWithErrorAndShouldReturnToThePreviousState()
            {
                var state = GetState(m => new ActivationStateMachine.WaitingForNewPushDeviceDetails(m));
                var reason = new ErrorInfo();
                var awaiter = new TaskCompletionAwaiter();
                MobileDevice.Callbacks.DeactivatedCallback = error =>
                {
                    error.Should().BeSameAs(reason);
                    awaiter.SetCompleted();
                    return Task.CompletedTask;
                };
                var (nextState, nextEventFunc) = await state.Transition(new ActivationStateMachine.DeregistrationFailed(reason));

                nextState.Should().BeOfType<ActivationStateMachine.WaitingForNewPushDeviceDetails>();
                (await nextEventFunc()).Should().BeNull();

                (await awaiter.Task).Should().BeTrue();
            }

            public WaitingForDeregistrationTests(ITestOutputHelper output)
                : base(output)
            {
                MobileDevice = new FakeMobileDevice();
                RestClient = GetRestClient(mobileDevice: MobileDevice);
            }

            private ActivationStateMachine.WaitingForDeregistration GetState(Func<ActivationStateMachine, ActivationStateMachine.State> getPreviousState)
            {
                var stateMachine = new ActivationStateMachine(RestClient, RestClient.Logger);
                return new ActivationStateMachine.WaitingForDeregistration(stateMachine, getPreviousState(stateMachine));
            }

            private (ActivationStateMachine.WaitingForDeregistration, ActivationStateMachine) GetStateAndStateMachine(Func<ActivationStateMachine, ActivationStateMachine.State> getPreviousState, AblyRest restClient = null)
            {
                var stateMachine = new ActivationStateMachine(restClient ?? RestClient, RestClient.Logger);
                return (new ActivationStateMachine.WaitingForDeregistration(stateMachine, getPreviousState(stateMachine)), stateMachine);
            }

            public FakeMobileDevice MobileDevice { get; set; }

            public AblyRest RestClient { get; }

            public void Dispose()
            {
                MobileDevice = null;
                ClearLocalDeviceStaticInstance();
            }
        }

        private static void ClearLocalDeviceStaticInstance() => LocalDevice.Instance = null;
    }
}
