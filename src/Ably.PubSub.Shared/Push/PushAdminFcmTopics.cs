using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Push
{
    /// <summary>
    /// Manages FCM topics with push-admin privileges: subscribing devices to a topic,
    /// listing a device's topics, and publishing to a topic.
    /// <para>
    /// FCM topics let FCM fan a single send out to every Android device subscribed to the topic,
    /// instead of Ably sending one FCM request per device. Ably resolves devices to their FCM
    /// registration tokens and calls FCM with the app's service account, so callers never handle
    /// FCM credentials or tokens.
    /// </para>
    /// <para>
    /// A topic exists once its first device subscribes; there is no create or delete call. Topic names
    /// must match <c>[a-zA-Z0-9-_.~%]{1,900}</c>. FCM topics are open: any instance of the app can
    /// subscribe to any topic directly through the Firebase SDK, so never put user-specific or
    /// sensitive data in a topic message.
    /// </para>
    /// </summary>
    public sealed class PushAdminFcmTopics
    {
        private const string BasePath = "/push/fcmTopics";

        private static readonly Regex TopicNamePattern = new Regex("^[a-zA-Z0-9\\-_.~%]{1,900}$", RegexOptions.Compiled);

        private readonly PubSubHttpClient _restClient;

        internal PushAdminFcmTopics(PubSubHttpClient restClient)
        {
            _restClient = restClient;
        }

        /// <summary>
        /// Returns a <see cref="PushAdminFcmTopic"/> for managing subscriptions to, and publishing to, the named topic.
        /// </summary>
        /// <param name="topic">The FCM topic name, matching <c>[a-zA-Z0-9-_.~%]{1,900}</c>.</param>
        /// <returns>A <see cref="PushAdminFcmTopic"/>.</returns>
        /// <exception cref="AblyException">The topic name is empty or does not match the allowed pattern.</exception>
        public PushAdminFcmTopic Get(string topic)
        {
            if (topic.IsEmpty() || !TopicNamePattern.IsMatch(topic))
            {
                throw new AblyException(
                    $"Invalid FCM topic name '{topic}'. Topic names must match [a-zA-Z0-9-_.~%]{{1,900}}; do not include a '/topics/' prefix.",
                    ErrorCodes.BadRequest);
            }

            return new PushAdminFcmTopic(this, topic);
        }

        /// <summary>
        /// Retrieves the names of the FCM topics a device is subscribed to,
        /// including any it subscribed to other than through Ably.
        /// </summary>
        /// <param name="deviceId">The unique ID of the device.</param>
        /// <returns>The topic names.</returns>
        public async Task<IReadOnlyList<string>> ListAsync(string deviceId)
        {
            RequireNonEmpty(deviceId, nameof(deviceId));

            var request = _restClient.CreateGetRequest(BasePath);
            request.AddQueryParameters(new[] { new KeyValuePair<string, string>("deviceId", deviceId) });
            PushAdmin.AddDeviceAuthenticationIfLocal(request, _restClient.Device, deviceId);

            return await _restClient.ExecuteRequest<List<string>>(request) ?? new List<string>();
        }

        /// <summary>
        /// Sends a push notification to every device subscribed to an FCM topic.
        /// Equivalent to <c>Get(topic).PublishAsync(payload)</c>; see
        /// <see cref="PushAdminFcmTopic.PublishAsync(JObject)"/> for the payload format.
        /// </summary>
        /// <param name="topic">The FCM topic name.</param>
        /// <param name="payload">The FCM message fields.</param>
        /// <returns>The FCM message ID.</returns>
        public async Task<string> PublishAsync(string topic, JObject payload) =>
            await Get(topic).PublishAsync(payload);

        internal async Task SubscribeDeviceAsync(string topic, string deviceId)
        {
            RequireNonEmpty(deviceId, nameof(deviceId));

            var request = _restClient.CreatePostRequest(DeviceSubscriptionPath(topic, deviceId));
            await ExecuteAdminRequest(request, deviceId);
        }

        internal async Task UnsubscribeDeviceAsync(string topic, string deviceId)
        {
            RequireNonEmpty(deviceId, nameof(deviceId));

            var request = _restClient.CreateRequest(DeviceSubscriptionPath(topic, deviceId), HttpMethod.Delete);
            await ExecuteAdminRequest(request, deviceId);
        }

        internal async Task SubscribeClientAsync(string topic, string clientId)
        {
            RequireNonEmpty(clientId, nameof(clientId));

            await PostSubscriptionsAsync(topic, new JObject { ["clientId"] = clientId });
        }

        internal async Task SubscribeChannelsAsync(string topic, IEnumerable<string> channels)
        {
            var channelList = channels?.ToList();
            if (channelList is null || channelList.Count == 0)
            {
                throw new AblyException("Please provide at least one channel.", ErrorCodes.BadRequest);
            }

            var emptyIndex = channelList.FindIndex(c => c.IsEmpty());
            if (emptyIndex >= 0)
            {
                throw new AblyException($"Channel name at index {emptyIndex} is null or empty.", ErrorCodes.BadRequest);
            }

            await PostSubscriptionsAsync(topic, new JObject { ["channels"] = new JArray(channelList) });
        }

        internal async Task UnsubscribeClientAsync(string topic, string clientId)
        {
            RequireNonEmpty(clientId, nameof(clientId));

            var request = _restClient.CreateRequest(SubscriptionsPath(topic), HttpMethod.Delete);
            request.AddQueryParameters(new[] { new KeyValuePair<string, string>("clientId", clientId) });
            await ExecuteAdminRequest(request);
        }

        internal async Task<string> PublishToTopicAsync(string topic, JObject payload)
        {
            if (payload is null || !payload.HasValues)
            {
                throw new AblyException("Please provide a non-empty payload.", ErrorCodes.BadRequest);
            }

            var request = _restClient.CreatePostRequest($"{TopicPath(topic)}/messages");
            PushAdmin.AddFullWaitIfNecessary(request, _restClient.Options);
            request.PostData = payload;

            // The response is {"topic": ..., "messageId": ...}. Parse it loosely so that extra or
            // non-string fields can't turn an already-sent push into an exception (and a duplicate on retry).
            var response = await _restClient.ExecuteRequest<JObject>(request);
            return (response?["messageId"] as JValue)?.Value?.ToString();
        }

        private static void RequireNonEmpty(string value, string name)
        {
            if (value.IsEmpty())
            {
                throw new AblyException($"Please provide a non-empty {name}.", ErrorCodes.BadRequest);
            }
        }

        private static string TopicPath(string topic) => $"{BasePath}/{topic.EncodeUriPart()}";

        private static string SubscriptionsPath(string topic) => $"{TopicPath(topic)}/subscriptions";

        private static string DeviceSubscriptionPath(string topic, string deviceId) =>
            $"{SubscriptionsPath(topic)}/{deviceId.EncodeUriPart()}";

        private async Task PostSubscriptionsAsync(string topic, JObject body)
        {
            var request = _restClient.CreatePostRequest(SubscriptionsPath(topic));
            request.PostData = body;
            await ExecuteAdminRequest(request);
        }

        private async Task ExecuteAdminRequest(AblyRequest request, string deviceId = null)
        {
            PushAdmin.AddFullWaitIfNecessary(request, _restClient.Options);
            PushAdmin.AddDeviceAuthenticationIfLocal(request, _restClient.Device, deviceId);
            _ = await _restClient.ExecuteRequest(request);
        }
    }
}
