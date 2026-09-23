using System;
using System.Threading.Tasks;

namespace IO.Ably.Push
{
    /// <summary>
    /// Push Apis for Realtime clients.
    /// </summary>
    public sealed class PushRealtime : IDisposable
    {
        private readonly AblyRest _restClient;
        private readonly ILogger _logger;

        internal ActivationStateMachine StateMachine { get; private set; }

        internal PushRealtime(AblyRest restClient, ILogger logger)
        {
            _restClient = restClient;
            _logger = logger;
        }

        internal void InitialiseStateMachine()
        {
            if (_restClient.MobileDevice != null)
            {
                StateMachine = new ActivationStateMachine(_restClient, _logger);

                var hasState = StateMachine.LoadPersistedState();

                // if we are not in the initial state
                if (hasState && (StateMachine.CurrentState is ActivationStateMachine.NotActivated == false))
                {
                    var device = _restClient.Device;
                    if (device.Id.IsEmpty() || device.DeviceSecret.IsEmpty())
                    {
                        _logger.Warning("State machine has loaded state but failed to load Local device. Resetting local device.");
                        LocalDevice.ResetDevice(_restClient.MobileDevice);

                        StateMachine.ClearPersistedState();
                        StateMachine.ResetStateMachine();
                    }
                }

                if (_restClient.Device != null)
                {
                    _restClient.Device.ClientIdUpdated = ClientIdUpdated;
                }
            }
        }

        private void ClientIdUpdated(string newClientId)
        {
            var currentStateIsNotNotActivated =
                (StateMachine.CurrentState is ActivationStateMachine.NotActivated) == false;
            if (_restClient.Device.IsRegistered && currentStateIsNotNotActivated)
            {
                _ = Task.Run(() => StateMachine.HandleEvent(new ActivationStateMachine.GotPushDeviceDetails()));
            }
        }

        /// <summary>
        /// Start the push notification device registration process.
        /// </summary>
        public void Activate()
        {
            if (StateMachine is null)
            {
                throw new AblyException("Realtime push is not enabled. Please initialise Ably by calling (AblyAndroidMobileDevice / AblyAppleMobileDevice).Initialize() before calling `Activate`");
            }

            _ = StateMachine.HandleEvent(new ActivationStateMachine.CalledActivate());
        }

        /// <summary>
        /// Starts the push notification device de-registration process.
        /// </summary>
        public void Deactivate()
        {
            if (StateMachine is null)
            {
                throw new AblyException("Realtime push is not enabled. Please initialise Ably by calling (AblyAndroidMobileDevice / AblyAppleMobileDevice).Initialize() before calling `Deactivate`");
            }

            _ = StateMachine.HandleEvent(new ActivationStateMachine.CalledDeactivate());
        }

        /// <summary>
        /// Admin APIs for Push notifications.
        /// </summary>
        public PushAdmin Admin => _restClient.Push.Admin;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_restClient.Device != null)
            {
                _restClient.Device.ClientIdUpdated = null;
            }
        }
    }
}
