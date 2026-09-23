using System;
using System.Threading.Tasks;

namespace IO.Ably.Push
{
    /// <summary>
    /// Class used to setup Push state change callbacks.
    /// </summary>
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
