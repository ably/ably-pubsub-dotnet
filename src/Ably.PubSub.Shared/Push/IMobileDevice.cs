using System;

namespace IO.Ably.Push
{
    /// <summary>
    /// Interface for communicating with a mobile device supporting pushing notifications.
    /// </summary>
    public interface IMobileDevice
    {
        /// <summary>
        /// Persist a preferences on the mobile device. TODO: Add info specific to Android and iOS.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        /// <param name="groupName">Groups preferences so they can be removed easier.</param>
        void SetPreference(string key, string value, string groupName);

        /// <summary>
        /// Retrieves a preference from the mobile device.
        /// </summary>
        /// <param name="key">Preference Key.</param>
        /// <param name="groupName">Group name.</param>
        /// <returns>The value of the preference or null if it doesn't exist.</returns>
        string GetPreference(string key, string groupName);

        /// <summary>
        /// Remove a whole group of preferences.
        /// </summary>
        /// <param name="groupName">Group name.</param>
        void ClearPreferences(string groupName);

        /// <summary>
        /// Requests a registration token. So far used only by Android.
        /// </summary>
        /// <param name="callback">Action which is executed when the operation completes.</param>
        void RequestRegistrationToken(Action<Result<RegistrationToken>> callback);

        /// <summary>
        /// Defines callbacks executed at different parts of the push journey.
        /// </summary>
        PushCallbacks Callbacks { get; }

        /// <summary>
        /// Device platform i.e. Android.
        /// </summary>
        string DevicePlatform { get; }

        /// <summary>
        /// Device form factor.
        /// </summary>
        string FormFactor { get; }
    }
}
