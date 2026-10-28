using System;
using System.Threading.Tasks;

namespace Ably.PubSub.Push
{
    /// <summary>
    /// Push APIs for realtime clients. Only <see cref="Admin"/> is public: device push-receive
    /// (activating this device for push notifications) is not available in 2.0.
    /// </summary>
    public sealed class PushRealtime : IDisposable
    {
        private readonly PubSubHttpClient _restClient;
        private readonly ILogger _logger;

        internal ActivationStateMachine StateMachine { get; private set; }

        internal PushRealtime(PubSubHttpClient restClient, ILogger logger)
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
        /// Internal: device push-receive is not available in 2.0, as there is no platform implementation of
        /// <see cref="IMobileDevice"/> to activate.
        /// </summary>
        internal void Activate()
        {
            if (StateMachine is null)
            {
                throw new AblyException("Device push-receive is not available in Ably Pub/Sub 2.0, so `Activate` cannot be used. See PushNotifications.md.");
            }

            _ = StateMachine.HandleEvent(new ActivationStateMachine.CalledActivate());
        }

        /// <summary>
        /// Starts the push notification device de-registration process.
        /// Internal: device push-receive is not available in 2.0.
        /// </summary>
        internal void Deactivate()
        {
            if (StateMachine is null)
            {
                throw new AblyException("Device push-receive is not available in Ably Pub/Sub 2.0, so `Deactivate` cannot be used. See PushNotifications.md.");
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
