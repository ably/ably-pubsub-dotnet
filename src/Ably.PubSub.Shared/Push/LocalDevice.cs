using System;
using IO.Ably.Encryption;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Push
{
    /// <summary>
    /// LocalDevice represents the current state of the device in respect of it being a target for push notifications.
    /// </summary>
    public class LocalDevice : DeviceDetails
    {
        private const string JsonTokenTypeParameterName = "transportType";
        private const string JsonAndroidTokenParameterName = "registrationToken";
        private const string JsonAppleTokenParameterName = "deviceToken";
        private const string AndroidTokenType = "fcm";
        private const string AppleTokenType = "apns";

        /// <summary>
        /// Devices that have completed registration have an identity token assigned to them by the push service.
        /// It can be used to authenticate Push Admin requests.
        /// </summary>
        [JsonIgnore]
        public string DeviceIdentityToken { get; set; }

        internal Action<string> ClientIdUpdated { get; set; } = (newClientId) => { };

        /// <summary>
        /// Checks if the device is registered to receive push notifications.
        /// </summary>
        public bool IsRegistered => DeviceIdentityToken.IsNotEmpty();

        internal bool IsCreated => Id.IsNotEmpty() && DeviceSecret.IsNotEmpty();

        internal RegistrationToken RegistrationToken
        {
            get => GetRegistrationToken(Push?.Recipient);
            set
            {
                if (value == null)
                {
                    return;
                }

                Push.Recipient = RegistrationTokenToPushRecipientJson(value);
            }
        }

        private static JObject RegistrationTokenToPushRecipientJson(RegistrationToken token)
        {
            switch (token.Type)
            {
                case AppleTokenType:
                    return new JObject
                    {
                        { JsonTokenTypeParameterName, token.Type },
                        { JsonAppleTokenParameterName, token.Token }
                    };
                case AndroidTokenType:
                    return new JObject
                    {
                        { JsonTokenTypeParameterName, token.Type },
                        { JsonAndroidTokenParameterName, token.Token }
                    };
                default:
                    throw new AblyException(
                        $"Unsupported token type. Supported values: `{AppleTokenType}` and `{AndroidTokenType}`");
            }
        }

        private static RegistrationToken GetRegistrationToken(JObject recipient)
        {
            if (recipient == null)
            {
                return null;
            }

            var type = (string)recipient.GetValue(JsonTokenTypeParameterName);
            switch (type)
            {
                case AndroidTokenType:
                    return new RegistrationToken(
                        type,
                        (string)recipient.GetValue(JsonAndroidTokenParameterName));
                case AppleTokenType:
                    return new RegistrationToken(
                        type,
                        (string)recipient.GetValue(JsonAppleTokenParameterName));
                default:
                    throw new AblyException(
                        $"Invalid token parameter type. Supported types are: `{AndroidTokenType}` and `{AppleTokenType}`");
            }
        }

        internal void UpdateClientId(string newClientId, IMobileDevice mobileDevice)
        {
            if (ClientId != newClientId)
            {
                ClientId = newClientId;
                PersistLocalDevice(mobileDevice, this);

                ClientIdUpdated(newClientId);
            }
        }

        /// <summary>
        /// Create a new instance of localDevice with a random Id and secret.
        /// </summary>
        /// <param name="clientId">Optional clientId which is set on the device.</param>
        /// <param name="mobileDevice">If a mobile device is present it we will use the DevicePlatform and FormFactor from there.</param>
        /// <returns>Instance of LocalDevice.</returns>
        public static LocalDevice Create(string clientId = null, IMobileDevice mobileDevice = null)
        {
            return new LocalDevice
            {
                Id = Guid.NewGuid().ToString("D"),
                DeviceSecret = Crypto.GenerateSecret(),
                ClientId = clientId,

                Platform = mobileDevice?.DevicePlatform,
                FormFactor = mobileDevice?.FormFactor,
            };
        }

        internal static void ResetDevice(IMobileDevice mobileDevice)
        {
            mobileDevice.ClearPreferences(PersistKeys.Device.SharedName);
            Instance = null;
        }

        internal static LocalDevice Instance { get; set; }

        internal static bool IsLocalDeviceInitialized => Instance != null;

        internal static LocalDevice GetInstance(IMobileDevice mobileDevice, string clientId)
        {
            if (mobileDevice is null)
            {
                throw new AblyException(
                    "Cannot initialise LocalDevice instance before initialising the MobileDevice class. For Android call AndroidMobileDevice.Initialise() and for iOS call AppleMobileDevice.Initialise()");
            }

            switch (Instance)
            {
                case null:
                    if (LoadPersistedLocalDevice(mobileDevice, out var device))
                    {
                        Instance = device;
                    }
                    else
                    {
                        Instance = Create(clientId, mobileDevice);
                        PersistLocalDevice(mobileDevice, Instance);
                    }

                    return Instance;
                default:
                    return Instance;
            }
        }

        internal static bool LoadPersistedLocalDevice(IMobileDevice mobileDevice, out LocalDevice persistedDevice)
        {
            Debug("Loading Local Device persisted state.");
            string GetDeviceSetting(string key) => mobileDevice.GetPreference(key, PersistKeys.Device.SharedName);

            string id = GetDeviceSetting(PersistKeys.Device.DeviceId);
            if (id.IsEmpty())
            {
                persistedDevice = null;
                return false;
            }

            persistedDevice = new LocalDevice
            {
                Platform = mobileDevice.DevicePlatform,
                FormFactor = mobileDevice.FormFactor,
                Id = id,
                DeviceSecret = GetDeviceSetting(PersistKeys.Device.DeviceSecret),
                ClientId = GetDeviceSetting(PersistKeys.Device.ClientId),
                DeviceIdentityToken = GetDeviceSetting(PersistKeys.Device.DeviceToken)
            };

            var tokenType = GetDeviceSetting(PersistKeys.Device.TokenType);

            if (tokenType.IsNotEmpty())
            {
                string tokenString = GetDeviceSetting(PersistKeys.Device.Token);

                if (tokenString.IsNotEmpty())
                {
                    var token = new RegistrationToken(tokenType, tokenString);
                    persistedDevice.RegistrationToken = token;
                }
            }

            Debug($"LocalDevice loaded: {persistedDevice.ToJson()}");

            return true;
        }

        internal static void PersistLocalDevice(IMobileDevice mobileDevice, LocalDevice localDevice)
        {
            mobileDevice.SetPreference(PersistKeys.Device.DeviceId, localDevice.Id, PersistKeys.Device.SharedName);
            mobileDevice.SetPreference(PersistKeys.Device.ClientId, localDevice.ClientId, PersistKeys.Device.SharedName);
            mobileDevice.SetPreference(PersistKeys.Device.DeviceSecret, localDevice.DeviceSecret, PersistKeys.Device.SharedName);
            mobileDevice.SetPreference(PersistKeys.Device.DeviceToken, localDevice.DeviceIdentityToken, PersistKeys.Device.SharedName);
        }

        internal static void PersistRegistrationToken(IMobileDevice mobileDevice, RegistrationToken token)
        {
            if (token.Type.IsEmpty() || token.Token.IsEmpty())
            {
                return;
            }

            mobileDevice.SetPreference(PersistKeys.Device.TokenType, token.Type, PersistKeys.Device.SharedName);
            mobileDevice.SetPreference(PersistKeys.Device.Token, token.Token, PersistKeys.Device.SharedName);
        }

        private static void Debug(string message) => DefaultLogger.Debug($"LocalDevice: {message}");
    }
}
