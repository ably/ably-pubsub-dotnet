using System;
using System.Collections.Generic;

using IO.Ably.Push;

namespace IO.Ably.Tests.Push
{
    public class FakeMobileDevice : IMobileDevice
    {
        public Dictionary<string, string> Settings { get; } = new Dictionary<string, string>();

        public bool RequestRegistrationTokenCalled { get; private set; }

        public void SetPreference(string key, string value, string groupName)
        {
            Settings[$"{groupName}:{key}"] = value;
        }

        public string GetPreference(string key, string groupName)
        {
            var settingKey = $"{groupName}:{key}";
            if (Settings.ContainsKey(settingKey))
            {
                return Settings[settingKey];
            }

            return null;
        }

        public void ClearPreferences(string groupName)
        {
            var keysToRemove = new List<string>();
            foreach (var key in Settings.Keys)
            {
                if (key.StartsWith(groupName))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                Settings.Remove(key);
            }
        }

        public void RequestRegistrationToken(Action<Result<RegistrationToken>> callback)
        {
            RequestRegistrationTokenCalled = true;
        }

        public PushCallbacks Callbacks { get; } = new PushCallbacks();

        public string DevicePlatform => "test";

        public string FormFactor => "phone";
    }
}
