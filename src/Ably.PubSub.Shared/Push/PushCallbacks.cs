using System;
using System.Threading.Tasks;

namespace Ably.PubSub.Push
{
    /// <summary>
    /// Class used to setup Push state change callbacks.
    /// </summary>
    /// <remarks>
    /// Device push-receive is not available in Ably Pub/Sub 2.0.
    /// To request the feature, comment on https://github.com/ably/ably-pubsub-dotnet/issues/1360.
    /// </remarks>
    public class PushCallbacks
    {
        /// <summary>
        /// Action called when the device has been deactivated for push notifications.
        /// Error info is either `null` or holds the current error.
        /// </summary>
        public Func<ErrorInfo, Task> DeactivatedCallback { get; set; } = error => Task.CompletedTask;

        /// <summary>
        /// Action called when the device has been activated for push notifications.
        /// Error info is either `null` or holds the current error.
        /// </summary>
        public Func<ErrorInfo, Task> ActivatedCallback { get; set; } = error => Task.CompletedTask;

        /// <summary>
        /// Action called when a registration sync failed. The reason it failed will be in the error info.
        /// </summary>
        public Func<ErrorInfo, Task> SyncRegistrationFailedCallback { get; set; } = error => Task.CompletedTask;
    }
}
