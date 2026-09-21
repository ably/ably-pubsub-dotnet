namespace IO.Ably
{
    internal static class ErrorCodes
    {
        /* generic error codes */
        public const int NoError = 10000;

        /* 400 codes */
        public const int BadRequest = 40000;
        public const int InvalidRequestBody = 40001;
        public const int InvalidParameterName = 40002;
        public const int InvalidParameterValue = 40003;
        public const int InvalidHeader = 40004;
        public const int InvalidCredential = 40005;
        public const int InvalidConnectionId = 40006;
        public const int InvalidMessageId = 40007;
        public const int InvalidContentLength = 40008;
        public const int MaxMessageLengthExceeded = 40009;
        public const int InvalidChannelName = 40010;
        public const int StaleRingState = 40011;
        public const int InvalidClientId = 40012;
        public const int InvalidMessageDataOrEncoding = 40013;
        public const int ResourceDisposed = 40014;
        public const int VcDiffDecodeError = 40018;
        public const int BatchError = 40020;
        public const int InvalidPublishRequestInvalidClientSpecifiedId = 40031;

        /* 401 codes */
        public const int Unauthorized = 40100;
        public const int InvalidCredentials = 40101;
        public const int IncompatibleCredentials = 40102;
        public const int InvalidUseOfBasicAuthOverNonTlsTransport = 40103;
        public const int UnableToObtainCredentialsFromGivenParameters = 40106;
        public const int AccountDisabled = 40110;
        public const int AccountBlockedConnectionLimitExceeded = 40111;
        public const int AccountBlockedMessageLimitExceeded = 40112;
        public const int AccountBlocked = 40113;
        public const int ApplicationDisabled = 40120;
        public const int KeyError = 40130;
        public const int KeyRevoked = 40131;
        public const int KeyExpired = 40132;
        public const int KeyDisabled = 40133;
        public const int TokenError = 40140;
        public const int TokenRevoked = 40141;
        public const int TokenExpired = 40142;
        public const int TokenUnrecognised = 40143;
        public const int ConnectionBlockedLimitsExceeded = 40150;
        public const int OperationNotPermittedWithCapability = 40160;
        public const int ClientCallbackError = 40170;
        public const int NoMeansProvidedToRenewAuthToken = 40171;

        /* 403 codes */
        public const int Forbidden = 40300;
        public const int TlsConnectionNotPermitted = 40310;
        public const int OperationRequiresTlsConnection = 40311;
        public const int OperationRequiresAuth = 40320;

        /* 404 codes */
        public const int NotFound = 40400;

        /* 405 codes */
        public const int MethodNotAllowed = 40500;

        /* 500 codes */
        public const int InternalError = 50000;
        public const int InternalChannelError = 50001;
        public const int InternalConnectionError = 50002;
        public const int TimeoutError = 50003;

        /* connection-related */
        public const int ConnectionFailed = 80000;
        public const int ConnectionFailedNoCompatibleTransport = 80001;
        public const int ConnectionSuspended = 80002;
        public const int Disconnected = 80003;
        public const int AlreadyConnected = 80004;
        public const int InvalidConnectionIdRemoteNotFound = 80005;
        public const int UnableToRecoverConnectionMessagesExpired = 80006;
        public const int UnableToRecoverConnectionMessageLimitExceeded = 80007;
        public const int UnableToRecoverConnectionExpired = 80008;
        public const int ConnectionNotEstablishedNoTransportHandle = 80009;
        public const int InvalidTransportHandle = 80010;
        public const int UnableToRecoverIncompatibleAuthParams = 80011;
        public const int ProtocolError = 80013;
        public const int ConnectionTimedOut = 80014;
        public const int IncompatibleConnectionParams = 80015;
        public const int OperationOnSupersededTransport = 80016;
        public const int ConnectionClosed = 80017;
        public const int InvalidFormatForConnectionId = 80018;
        public const int ClientAuthProviderRequestFailed = 80019;
        public const int MaxConnectionMessageRateExceededForPublish = 80020;
        public const int MaxConnectionMessageRateExceededForSubscribe = 80021;
        public const int ClientRestrictionNotSatisfied = 80030;

        /* channel-related */
        public const int ChannelOperationFailed = 90000;
        public const int ChannelOperationFailedWithInvalidState = 90001;
        public const int ChannelOperationFailedEpochExpiredOrNeverExisted = 90002;
        public const int UnableToRecoverChannelMessageExpired = 90003;
        public const int UnableToRecoverChannelMessageLimitExceeded = 90004;
        public const int UnableToRecoverChannelNoMatchingEpoch = 90005;
        public const int UnableToRecoverChannelUnboundedRequest = 90006;
        public const int ChannelOperationFailedNoServerResponse = 90007;
        public const int UnableToEnterPresenceChannelNoClientId = 91000;
        public const int UnableToEnterPresenceChannelInvalidState = 91001;
        public const int UnableToLeavePresenceChannelThatIsNotEntered = 91002;
        public const int UnableToEnterPresenceChannelMaxMemberLimitExceeded = 91003;
        public const int MemberImplicitlyLeftPresenceChannel = 91100;

        // Activation State machine
        public const int ActivationFailedClientIdMismatch = 61002;
    }
}
