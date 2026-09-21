using System.Threading.Tasks;

namespace IO.Ably.Push
{
    /// <summary>
    /// PushChannel is a convenience class that facilitates push related actions,
    /// like subscribing and unsubscribing to push notification,
    /// narrowed to a specific channel.
    /// </summary>
    public class PushChannel
    {
        private readonly AblyRest _rest;
        private readonly ILogger _logger;

        internal string ChannelName { get; }

        /// <summary>
        /// Create a new instance of PushChannel.
        /// </summary>
        /// <param name="channelName">Name of the channel.</param>
        /// <param name="rest"><see cref="AblyRest"/> client.</param>
        internal PushChannel(string channelName, AblyRest rest)
        {
            ChannelName = channelName;
            _rest = rest;
            _logger = rest.Logger;
        }

        /// <summary>
        /// Subscribes the current device to receive push notifications from the current channel.
        /// </summary>
        /// <exception cref="AblyException">Throws an exception if the local device is not activated. Please make sure Push.Activate() has completed.</exception>
        /// <returns>Async operation.</returns>
        public async Task SubscribeDevice()
        {
            var localDevice = _rest.Device;
            if (localDevice?.DeviceIdentityToken is null)
            {
                // TODO: What error code should we use here
                throw new AblyException(
                    $"Cannot Subscribe device to channel '{ChannelName}' because the device is missing deviceIdentityToken. Please call AblyRest.Push.Activate() and wait for it to complete");
            }

            var subscription = await _rest.Push.Admin.ChannelSubscriptions.SaveAsync(new PushChannelSubscription()
            {
                Channel = ChannelName,
                DeviceId = localDevice.Id
            });

            _logger.Debug(
                $"Successfully subscribed channel '{subscription.Channel}' to device with id '{subscription.DeviceId}'");
        }

        /// <summary>
        /// Unsubscribes the current device from receiving push notification from the current channel.
        /// </summary>
        /// <exception cref="AblyException">Throws an exception if the local device is not activated. Please make sure Push.Activate() has completed.</exception>
        /// <returns>Async operation.</returns>
        public async Task UnsubscribeDevice()
        {
            var localDevice = _rest.Device;
            if (localDevice?.DeviceIdentityToken is null)
            {
                // TODO: What error code should we use here
                throw new AblyException(
                    $"Cannot Unsubscribe device from channel '{ChannelName}' because the device is missing deviceIdentityToken. Please call AblyRest.Push.Activate() and wait for it to complete");
            }

            await _rest.Push.Admin.ChannelSubscriptions.RemoveAsync(new PushChannelSubscription()
            {
                Channel = ChannelName,
                DeviceId = localDevice.Id
            });

            _logger.Debug(
                $"Successfully removed channel '{ChannelName}' from device with id '{localDevice.Id}'");
        }

        /// <summary>
        /// Subscribes the current clientId to receive push notifications from the current channel.
        /// </summary>
        /// <exception cref="AblyException">Throws an exception if the local device is not activated. Please make sure Push.Activate() has completed.</exception>
        /// <exception cref="AblyException">Throws an exception if the local device does not have a clientId assigned.</exception>
        /// <returns>Async operation.</returns>
        public async Task SubscribeClient()
        {
            var localDevice = _rest.Device;
            if (localDevice?.DeviceIdentityToken is null)
            {
                // TODO: What error code should we use here
                throw new AblyException(
                    $"Cannot Subscribe device to channel '{ChannelName}' because the device is missing deviceIdentityToken. Please call AblyRest.Push.Activate() and wait for it to complete");
            }

            if (localDevice.ClientId.IsEmpty())
            {
                throw new AblyException(
                    $"Cannot Subscribe clientId to channel '{ChannelName}' because the device does not have an assigned clientId."); // TODO: Need to include info on how people come out of this.
            }

            var subscription = await _rest.Push.Admin.ChannelSubscriptions.SaveAsync(new PushChannelSubscription()
            {
                Channel = ChannelName,
                ClientId = localDevice.ClientId
            });

            _logger.Debug($"Successfully subscribed channel '{subscription.Channel}' to clientId '{subscription.ClientId}");
        }

        /// <summary>
        /// Unsubscribes the current client from receiving push notification from the current channel.
        /// </summary>
        /// <exception cref="AblyException">Throws an exception if the local device is not activated. Please make sure Push.Activate() has completed.</exception>
        /// <exception cref="AblyException">Throws an exception if the local device does not have a clientId assigned.</exception>
        /// <returns>Async operation.</returns>
        public async Task UnsubscribeClient()
        {
            var localDevice = _rest.Device;
            if (localDevice?.DeviceIdentityToken is null)
            {
                // TODO: What error code should we use here
                throw new AblyException(
                    $"Cannot Unsubscribe device from channel '{ChannelName}' because the device is missing deviceIdentityToken. Please call AblyRest.Push.Activate() and wait for it to complete");
            }

            if (localDevice.ClientId.IsEmpty())
            {
                throw new AblyException(
                    $"Cannot Unsubscribe clientId from channel '{ChannelName}' because the device does not have an assigned clientId."); // TODO: Need to include info on how people come out of this.
            }

            await _rest.Push.Admin.ChannelSubscriptions.RemoveAsync(new PushChannelSubscription()
            {
                Channel = ChannelName,
                ClientId = localDevice.ClientId
            });

            _logger.Debug(
                $"Successfully unsubscribed channel '{ChannelName}' from clientId '{localDevice.ClientId}'");
        }

        /// <summary>
        /// Returns a list of <see cref="PushChannelSubscription"/> for the current channel, filtered by the currently set clientId and deviceId.
        /// </summary>
        /// <param name="listRequest">A custom <see cref="ListSubscriptionsRequest"/> can be passed. However the current clientId and deviceId are always used. It's mainly to provide further parameters.
        /// If more flexibility is required, please use AblyRest.Push.Admin.ChannelSubscriptions.ListAsync.</param>
        /// <returns>A PaginatedResult of PushChannelSubscription.</returns>
        /// <exception cref="AblyException">Throws an exception if the local device is not activated. Please make sure Push.Activate() has completed.</exception>
        public async Task<PaginatedResult<PushChannelSubscription>> ListSubscriptions(ListSubscriptionsRequest listRequest = null)
        {
            var request = listRequest ?? ListSubscriptionsRequest.Empty();

            var localDevice = _rest.Device;

            if (localDevice?.DeviceIdentityToken is null)
            {
                // TODO: What error code should we use here
                throw new AblyException(
                    $"Cannot list channel subscriptions for '{ChannelName}' because the device is missing deviceIdentityToken. Please call AblyRest.Push.Activate() and wait for it to complete.");
            }

            if (localDevice.ClientId.IsNotEmpty())
            {
                request.ClientId = localDevice.ClientId;
            }

            if (localDevice.Id.IsNotEmpty())
            {
                request.DeviceId = localDevice.Id;
            }

            request.Channel = ChannelName;
            request.ExtraParameters.Add("concatFilters", "true");

            return await _rest.Push.Admin.ChannelSubscriptions.ListAsync(request);
        }
    }
}
