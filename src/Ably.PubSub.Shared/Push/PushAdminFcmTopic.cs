using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ably.PubSub.Push
{
    /// <summary>
    /// A single FCM topic whose subscriptions are managed with push-admin privileges. Obtained from <see cref="PushAdminFcmTopics.Get"/>.
    /// </summary>
    public sealed class PushAdminFcmTopic
    {
        private readonly PushAdminFcmTopics _topics;

        internal PushAdminFcmTopic(PushAdminFcmTopics topics, string name)
        {
            _topics = topics;
            Name = name;
        }

        /// <summary>
        /// The FCM topic name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Subscribes a device to the topic. Subscribing a device that is already subscribed succeeds.
        /// The device must be registered with Ably for FCM.
        /// </summary>
        /// <param name="deviceId">The unique ID of the device.</param>
        /// <returns>Task.</returns>
        public Task SubscribeAsync(string deviceId) => _topics.SubscribeDeviceAsync(Name, deviceId);

        /// <summary>
        /// Unsubscribes a device from the topic. Unsubscribing a device that is not subscribed succeeds.
        /// </summary>
        /// <param name="deviceId">The unique ID of the device.</param>
        /// <returns>Task.</returns>
        public Task UnsubscribeAsync(string deviceId) => _topics.UnsubscribeDeviceAsync(Name, deviceId);

        /// <summary>
        /// Requests that every FCM device registered for a clientId is subscribed to the topic.
        /// Ably accepts the request and subscribes the devices in the background, so success means the
        /// request was accepted, not that every device is subscribed. Devices registered for the clientId
        /// later are not subscribed.
        /// </summary>
        /// <param name="clientId">The ID of the client whose devices to subscribe.</param>
        /// <returns>Task.</returns>
        public Task SubscribeClientAsync(string clientId) => _topics.SubscribeClientAsync(Name, clientId);

        /// <summary>
        /// Requests that every FCM device subscribed to push notifications on any of the given channels
        /// is subscribed to the topic. Ably accepts the request and subscribes the devices in the background,
        /// so success means the request was accepted, not that every device is subscribed. This is a one-off
        /// batch, not a standing link: devices that subscribe to the channels later are not subscribed to the topic.
        /// </summary>
        /// <param name="channels">The names of the channels whose subscribed devices to subscribe.</param>
        /// <returns>Task.</returns>
        public Task SubscribeChannelsAsync(IEnumerable<string> channels) => _topics.SubscribeChannelsAsync(Name, channels);

        /// <summary>
        /// Requests that every FCM device registered for a clientId is unsubscribed from the topic.
        /// Ably accepts the request and unsubscribes the devices in the background, so success means the
        /// request was accepted, not that every device is unsubscribed.
        /// </summary>
        /// <param name="clientId">The ID of the client whose devices to unsubscribe.</param>
        /// <returns>Task.</returns>
        public Task UnsubscribeClientAsync(string clientId) => _topics.UnsubscribeClientAsync(Name, clientId);
    }
}
