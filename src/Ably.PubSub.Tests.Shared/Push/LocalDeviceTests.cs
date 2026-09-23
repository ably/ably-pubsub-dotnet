using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Push;
using IO.Ably.Realtime;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Tests.Realtime;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Push
{
    public sealed class LocalDeviceTests : MockHttpRestSpecs, IDisposable
    {
        [Fact]
        [Trait("spec", "RSH3a2b")]
        public void Create_ShouldCreateRandomDeviceSecretsAndIds()
        {
            var secrets = Enumerable.Range(1, 100)
                                        .Select(x => LocalDevice.Create().DeviceSecret)
                                        .Distinct();

            var ids = Enumerable.Range(1, 100)
                                    .Select(x => LocalDevice.Create().Id)
                                    .Distinct();

            secrets.Should().HaveCount(100);
            ids.Should().HaveCount(100);
        }

        [Theory]
        [InlineData("id", "secret", true)]
        [InlineData("id", "", false)]
        [InlineData("", "secret", false)]
        public void IsCreated_ShouldBeTrueWhenBothIdAndSecretArePresent(string id, string secret, bool result)
        {
            var device = new LocalDevice()
            {
                Id = id,
                DeviceSecret = secret,
            };

            device.IsCreated.Should().Be(result);
        }

        [Fact]
        public async Task LoadPersistedLocalDevice_ShouldLoadAllSavedProperties()
        {
            var mobileDevice = new FakeMobileDevice();
            void SetSetting(string key, string value) => mobileDevice.SetPreference(key, value, PersistKeys.Device.SharedName);

            const string deviceId = "deviceId";
            SetSetting(PersistKeys.Device.DeviceId, deviceId);
            const string clientId = "clientId";
            SetSetting(PersistKeys.Device.ClientId, clientId);
            const string deviceSecret = "secret";
            SetSetting(PersistKeys.Device.DeviceSecret, deviceSecret);
            const string identityToken = "token";
            SetSetting(PersistKeys.Device.DeviceToken, identityToken);
            const string tokenType = "fcm";
            SetSetting(PersistKeys.Device.TokenType, tokenType);
            const string token = "registration_token";
            SetSetting(PersistKeys.Device.Token, token);

            var loadResult = LocalDevice.LoadPersistedLocalDevice(mobileDevice, out var localDevice);
            loadResult.Should().BeTrue();
            localDevice.Platform.Should().Be(mobileDevice.DevicePlatform);
            localDevice.FormFactor.Should().Be(mobileDevice.FormFactor);

            localDevice.Id.Should().Be(deviceId);
            localDevice.ClientId.Should().Be(clientId);
            localDevice.DeviceSecret.Should().Be(deviceSecret);
            localDevice.DeviceIdentityToken.Should().Be(identityToken);
            localDevice.RegistrationToken.Type.Should().Be(tokenType);
            localDevice.RegistrationToken.Token.Should().Be(token);
        }

        [Fact]
        public async Task RestClient_LocalDevice_ShouldReturnSameInstanceForMultipleClients()
        {
            var mobileDevice = new FakeMobileDevice();
            var client1 = GetRestClient(mobileDevice: mobileDevice);
            var client2 = GetRestClient(mobileDevice: mobileDevice);

            client1.Device.Should().BeSameAs(client2.Device);
        }

        [Fact]
        [Trait("spec", "RSH8")]
        public void WhenPlatformsSupportsPushNotifications_ShouldBeAbleToRetrieveLocalDeviceFromRestClient()
        {
            var mobileDevice = new FakeMobileDevice();

            var rest = GetRestClient(mobileDevice: mobileDevice);

            rest.Device.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RSH8")]
        public void WhenPlatformsSupportsPushNotifications_ShouldBeAbleToRetrieveLocalDeviceFromRealtimeClient()
        {
            var mobileDevice = new FakeMobileDevice();

            var options = new ClientOptions(ValidKey)
            {
                AutoConnect = false
            };

            var realtime = new AblyRealtime(options, mobileDevice: mobileDevice);

            realtime.Device.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RSH8")]
        public void WhenPlatformsDoesNotSupportPushNotifications_DeviceShouldBeNull()
        {
            // Realtime check
            var realtime = new AblyRealtime(new ClientOptions(ValidKey)
            {
                AutoConnect = false
            });
            realtime.Device.Should().BeNull();

            // Rest check
            var rest = new AblyRest(ValidKey);
            rest.MobileDevice.Should().BeNull();
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RSH8a")]
        public void LocalDevice_IsOnlyInitialisedOnce()
        {
            var restClient = GetRestClient(mobileDevice: new FakeMobileDevice());
            var restClient2 = GetRestClient(mobileDevice: new FakeMobileDevice());

            restClient.Device.Should().BeSameAs(restClient2.Device);
        }

        [Fact]
        [Trait("spec", "RSH8a")]
        [Trait("spec", "RSH8b")]
        public void LocalDevice_WhenInitialised_ShouldHave_CorrectProperties_set()
        {
            var restClient = GetRestClient(mobileDevice: new FakeMobileDevice());

            var device = restClient.Device;

            device.Id.Should().NotBeEmpty();
            device.DeviceSecret.Should().NotBeEmpty();
            device.Platform.Should().NotBeEmpty();
            device.FormFactor.Should().NotBeEmpty();
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RSH8a")]
        [Trait("spec", "RSH8b")]
        public void LocalDevice_WhenRestClientContainsClientId_ShouldHaveTheSameClientId()
        {
            const string optionsClientId = "123";
            var restClient = GetRestClient(setOptionsAction: options => options.ClientId = optionsClientId, mobileDevice: new FakeMobileDevice());
            var device = restClient.Device;

            device.ClientId.Should().Be(optionsClientId);
        }

        [Fact]
        [Trait("spec", "RSA7b2")]
        [Trait("spec", "RSH8d")]
        public async Task WithoutClientId_WhenAuthorizedWithTokenParamsWithClientId_ShouldUpdateLocalDeviceClientId()
        {
            const string newClientId = "123";
            var mobileDevice = new FakeMobileDevice();
            var ably = GetRestClient(
                handleRequestFunc: async request => new AblyResponse() { TextResponse = new TokenDetails("token").ToJson() },
                mobileDevice: mobileDevice);

            var localDevice = ably.Device;
            localDevice.ClientId.Should().BeNull();

            _ = await ably.Auth.AuthorizeAsync(new TokenParams { ClientId = newClientId });

            localDevice.ClientId.Should().Be(newClientId);
            mobileDevice.GetPreference(PersistKeys.Device.ClientId, PersistKeys.Device.SharedName).Should().Be(newClientId);
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RSA7b3")]
        [Trait("spec", "RSH8d")]
        public async Task WhenConnectedMessageContainsClientId_AuthClientIdShouldBeTheSame()
        {
            // Arrange
            var options = new ClientOptions(ValidKey) { TransportFactory = new FakeTransportFactory(), SkipInternetCheck = true };
            var mobileDevice = new FakeMobileDevice();
            var realtime = new AblyRealtime(options, mobileDevice: mobileDevice);
            const string newClientId = "testId";

            var localDevice = realtime.Device;
            localDevice.ClientId.Should().BeNull();

            // Act
            realtime.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ClientId = newClientId },
            });

            await realtime.WaitForState(ConnectionState.Connected);

            // Assert
            realtime.Auth.ClientId.Should().Be(newClientId);
            localDevice.ClientId.Should().Be(newClientId);
            mobileDevice.GetPreference(PersistKeys.Device.ClientId, PersistKeys.Device.SharedName).Should().Be(newClientId);
        }

        [Theory(Skip = "Intermittently fails")]
        [ClassData(typeof(RSH8eStateTheoryData))]
        [Trait("spec", "RSH8e")]
        internal async Task WhenClientIdChangesAfterInitialisation_StateMachineShouldReceive_GotPushDeviceDetailsEvent(Func<ActivationStateMachine, ActivationStateMachine.State> createCurrentState)
        {
            // Arrange
            const string initialClientId = "123";
            var options = new ClientOptions(ValidKey) { TransportFactory = new FakeTransportFactory(), SkipInternetCheck = true, ClientId = initialClientId };
            var mobileDevice = new FakeMobileDevice();
            var realtime = new AblyRealtime(options, mobileDevice: mobileDevice);
            const string newClientId = "testId";

            var localDevice = realtime.Device;
            // Make sure the LocalDevice is registered
            realtime.Device.DeviceIdentityToken = "token";
            localDevice.ClientId.Should().Be(initialClientId);
            // Initialise the activation statemachine and set a fake state to record the next event.
            realtime.Push.InitialiseStateMachine();
            var taskAwaiter = new TaskCompletionAwaiter();
            realtime.Push.StateMachine.CurrentState =
                createCurrentState(realtime.Push.StateMachine);
            realtime.Push.StateMachine.ProcessingEventCallback = @event =>
            {
                // Check we received the correct event
                @event.Should().BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
                taskAwaiter.Done();
            };

            // Pretend we are connected and change the ClientId
            realtime.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ClientId = newClientId },
            });

            await realtime.WaitForState(ConnectionState.Connected);

            // Check the clientId is set correctly
            realtime.Auth.ClientId.Should().Be(newClientId);
            localDevice.ClientId.Should().Be(newClientId);

            // It's necessary to pause the current thread and let the background action to complete which fires the event.
            await Task.Delay(100);

            (await taskAwaiter).Should().BeTrue();
            mobileDevice.GetPreference(PersistKeys.Device.ClientId, PersistKeys.Device.SharedName).Should().Be(newClientId);
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RSH8e")]
        internal async Task WhenClientIdChangesAfterInitialisationAndStateMachineIsNotActivated_ShouldNotFireEvent()
        {
            // Arrange
            const string initialClientId = "123";
            var options = new ClientOptions(ValidKey) { TransportFactory = new FakeTransportFactory(), SkipInternetCheck = true, ClientId = initialClientId };
            var mobileDevice = new FakeMobileDevice();
            var realtime = new AblyRealtime(options, mobileDevice: mobileDevice);
            const string newClientId = "testId";

            var localDevice = realtime.Device;
            // Make sure the LocalDevice is registered
            realtime.Device.DeviceIdentityToken = "token";
            localDevice.ClientId.Should().Be(initialClientId);
            realtime.Push.InitialiseStateMachine();
            var taskAwaiter = new TaskCompletionAwaiter(1000);
            realtime.Push.StateMachine.ProcessingEventCallback = @event =>
            {
                taskAwaiter.Done();
            };

            // Pretend we are connected and change the ClientId
            realtime.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ClientId = newClientId },
            });

            await realtime.WaitForState(ConnectionState.Connected);

            // It's necessary to pause the current thread and let the background action to complete which fires the event.
            await Task.Delay(100);

            // No event should be sent to the statemachine
            (await taskAwaiter).Should().BeFalse();
        }

        // (RSH8j) If during library initialisation the LocalDevice id or deviceSecret attributes are not able to be
        // loaded then those LocalDevice details must be discarded and the ActivationStateMachine machine should
        // transition to the NotActivated state. New LocalDevice id and deviceSecret attributes should be generated
        // on the next activation event.
        [Fact]
        [Trait("spec", "RSH8j")]
        internal async Task WhenStateMachineIsInitialised_And_LocalDeviceIdIsEmpty_But_StateMachineState_is_loaded_ShouldResetLocalDevice_And_StateMachineState()
        {
            // Arrange
            const string initialClientId = "123";
            var options = new ClientOptions(ValidKey)
                { TransportFactory = new FakeTransportFactory(), SkipInternetCheck = true, ClientId = initialClientId };
            var mobileDevice = new FakeMobileDevice();
            var setupRealtime = new AblyRealtime(options, mobileDevice: mobileDevice);

            setupRealtime.Push.InitialiseStateMachine();
            setupRealtime.Push.StateMachine.CurrentState =
                new ActivationStateMachine.WaitingForNewPushDeviceDetails(setupRealtime.Push.StateMachine);
            setupRealtime.Push.StateMachine.PendingEvents.Enqueue(new ActivationStateMachine.CalledActivate());
            setupRealtime.Push.StateMachine.PersistState();

            var testRealtime = new AblyRealtime(options, mobileDevice: mobileDevice);

            // We let the RestClient create the local device.
            testRealtime.RestClient.Device.Id = null;

            testRealtime.Push.InitialiseStateMachine();
            var stateMachine = testRealtime.Push.StateMachine;
            stateMachine.CurrentState.Should().BeOfType<ActivationStateMachine.NotActivated>();
            stateMachine.PendingEvents.Should().BeEmpty();
            stateMachine.LocalDevice.Id.Should().NotBeEmpty();
            stateMachine.LocalDevice.DeviceSecret.Should().NotBeEmpty();
        }

        // RSH8f
        // (RSH8f) If the LocalDevice is created by an unidentified client (see (RSA7) )
        // and therefore has no clientId set, but on receipt of a registration response (see (RSH3c2) )
        // the registered device has a non-empty clientId, then the LocalDevice clientId is set with that clientId.
        [Fact]
        [Trait("spec", "RSH8f")]
        internal async Task WhenClientIdChangesAfterRegisteringDevice_StateMachineShouldReceive_GotPushDeviceDetailsEvent()
        {
            // Arrange
            const string newClientId = "testId";

            var options = new ClientOptions(ValidKey) { TransportFactory = new FakeTransportFactory(), SkipInternetCheck = true };
            var mobileDevice = new FakeMobileDevice();
            async Task<AblyResponse> HandleRequestFunc(AblyRequest request)
            {
                if (request.Url.Contains("/push/deviceRegistrations"))
                {
                    return new AblyResponse()
                    {
                        TextResponse = JObject.FromObject(new { clientId = newClientId, deviceIdentityToken = new { token = "token" } }).ToString()
                    };
                }

                return new AblyResponse() { TextResponse = "{}" };
            }

            var realtime = new AblyRealtime(options, (clientOptions, device) => GetRestClient(HandleRequestFunc, options, device), mobileDevice);

            // Setup the local device
            var localDevice = PushTestHelpers.GetRegisteredLocalDevice(realtime.RestClient);
            realtime.RestClient.Device = localDevice;
            localDevice.ClientId.Should().BeNull();

            realtime.Push.InitialiseStateMachine();

            var taskAwaiter = new TaskCompletionAwaiter();
            var stateMachine = realtime.Push.StateMachine;
            stateMachine.CurrentState = new ActivationStateMachine.WaitingForPushDeviceDetails(stateMachine);

            // We trigger the GotPushDeviceDetails event
            await stateMachine.HandleEvent(new ActivationStateMachine.GotPushDeviceDetails());

            // From here we expect the stateMachine to move to WaitingForDeviceRegistration and try to register the Device
            // The registration will hit our mocked rest client above and return a localDevice with a new clientId.
            // Once the clientId is received we should expect to receive GotPushDeviceDetails event and the new clientId to be persisted
            realtime.Push.StateMachine.ProcessingEventCallback = @event =>
            {
                // Check we received the correct event
                @event.Should().BeOfType<ActivationStateMachine.GotPushDeviceDetails>();
                taskAwaiter.Done();
            };

            (await taskAwaiter).Should().BeTrue();
            mobileDevice.GetPreference(PersistKeys.Device.ClientId, PersistKeys.Device.SharedName).Should().Be(newClientId);
        }

        private class RSH8eStateTheoryData : TheoryData<Func<ActivationStateMachine, ActivationStateMachine.State>>
        {
            public RSH8eStateTheoryData()
            {
                Add((machine) => new ActivationStateMachine.WaitingForDeregistration(machine, new ActivationStateMachine.NotActivated(machine)));
                Add((machine) => new ActivationStateMachine.AfterRegistrationSyncFailed(machine));
                Add((machine) => new ActivationStateMachine.WaitingForDeviceRegistration(machine));
                Add((machine) => new ActivationStateMachine.WaitingForRegistrationSync(machine, null));
                Add((machine) => new ActivationStateMachine.WaitingForPushDeviceDetails(machine));
                Add((machine) => new ActivationStateMachine.WaitingForNewPushDeviceDetails(machine));
            }
        }

        public LocalDeviceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        public void Dispose()
        {
            LocalDevice.Instance = null;
        }
    }
}
