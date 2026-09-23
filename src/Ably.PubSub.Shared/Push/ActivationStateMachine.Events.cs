namespace IO.Ably.Push
{
    internal partial class ActivationStateMachine
    {
        public sealed class CalledActivate : Event
        {
        }

        public sealed class CalledDeactivate : Event
        {
        }

        public sealed class GotPushDeviceDetails : Event
        {
        }

        public sealed class GotDeviceRegistration : Event
        {
            public string DeviceIdentityToken { get; set; }

            public GotDeviceRegistration(string token)
            {
                DeviceIdentityToken = token;
            }
        }

        public sealed class GettingPushDeviceDetailsFailed : ErrorEvent
        {
            public GettingPushDeviceDetailsFailed(ErrorInfo reason)
                : base(reason)
            {
            }
        }

        public sealed class GettingDeviceRegistrationFailed : ErrorEvent
        {
            public GettingDeviceRegistrationFailed(ErrorInfo reason)
                : base(reason)
            {
            }
        }

        public sealed class RegistrationSynced : Event
        {
        }

        public sealed class SyncRegistrationFailed : ErrorEvent
        {
            public SyncRegistrationFailed(ErrorInfo reason)
                : base(reason)
            {
            }
        }

        public sealed class Deregistered : Event
        {
        }

        public sealed class DeregistrationFailed : ErrorEvent
        {
            public DeregistrationFailed(ErrorInfo reason)
                : base(reason)
            {
            }
        }

        public abstract class Event
        {
        }

        public abstract class ErrorEvent : Event
        {
            public ErrorInfo Reason { get; }

            protected ErrorEvent(ErrorInfo reason)
            {
                Reason = reason;
            }
        }
    }
}
