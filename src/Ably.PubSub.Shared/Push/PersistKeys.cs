namespace IO.Ably.Push
{
    internal static class PersistKeys
    {
        public static class StateMachine
        {
            public const string SharedName = "Ably_StateMachine";
            public const string CurrentState = "ABLY_PUSH_CURRENT_STATE";
            public const string PendingEvents = "ABLY_PUSH_PENDING_EVENTS";
            public const string PushCustomRegistrar = "ABLY_PUSH_REGISTRATION_HANDLER";
        }

        public static class Device
        {
            public const string SharedName = "Ably_Device";

            public const string DeviceId = "ABLY_DEVICE_ID";
            public const string ClientId = "ABLY_CLIENT_ID";
            public const string DeviceSecret = "ABLY_DEVICE_SECRET";
            public const string DeviceToken = "ABLY_DEVICE_IDENTITY_TOKEN";
            public const string TokenType = "ABLY_REGISTRATION_TOKEN_TYPE";
            public const string Token = "ABLY_REGISTRATION_TOKEN";
        }
    }
}
