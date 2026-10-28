using System;
using System.Threading.Tasks;

namespace Ably.PubSub.Push
{
    /// <summary>
    /// Push Apis for Realtime clients.
    /// </summary>
    public sealed class PushRealtime : IDisposable
    {
        /// <summary>Where users can request device push-receive support for 2.x.</summary>
        internal const string DevicePushFeatureRequestUrl = "https://github.com/ably/ably-pubsub-dotnet/issues/1360";

        /// <summary>The message used when the unavailable device push-receive API is called.</summary>
        internal const string DevicePushNotAvailableMessage =
            "Device push notifications (push activation/deactivation) are not available in Ably Pub/Sub 2.0. "
            + "To request this feature, comment on " + DevicePushFeatureRequestUrl + ". "
            + "Push administration remains available via Push.Admin.";

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
        /// </summary>
        /// <remarks>
        /// Device push-receive is not available in Ably Pub/Sub 2.0, so this always throws an <see cref="AblyException"/>;
        /// it is reserved for a future release. To request the feature, comment on https://github.com/ably/ably-pubsub-dotnet/issues/1360.
        /// </remarks>
        public void Activate()
        {
            if (StateMachine is null)
            {
                throw new AblyException(DevicePushNotAvailableMessage);
            }

            _ = StateMachine.HandleEvent(new ActivationStateMachine.CalledActivate());
        }

        /// <summary>
        /// Starts the push notification device de-registration process.
        /// </summary>
        /// <remarks>
        /// Device push-receive is not available in Ably Pub/Sub 2.0, so this always throws an <see cref="AblyException"/>;
        /// it is reserved for a future release. To request the feature, comment on https://github.com/ably/ably-pubsub-dotnet/issues/1360.
        /// </remarks>
        public void Deactivate()
        {
            if (StateMachine is null)
            {
                throw new AblyException(DevicePushNotAvailableMessage);
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
