using System;
using System.Collections.Generic;
using IO.Ably.Shared.Utils;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Realtime.Workflow
{
    internal class RealtimeState
    {
        public class ConnectionData
        {
            public ConnectionData(List<string> fallbackHosts)
            {
                FallbackHosts = fallbackHosts ?? new List<string>();
            }

            public List<string> FallbackHosts { get; }

            public DateTimeOffset? ConfirmedAliveAt { get; private set; }

            /// <summary>
            ///     The id of the current connection. This string may be
            ///     used when recovering connection state.
            /// </summary>
            public string Id { get; set; }

            public string Host { get; set; }

            public bool IsFallbackHost => FallbackHosts.Contains(Host);

            internal long MessageSerial { get; set; }

            /// <summary>
            /// The current connection key.
            /// </summary>
            public string Key { get; set; }

            public TimeSpan ConnectionStateTtl { get; internal set; } = Defaults.ConnectionStateTtl;

            /// <summary>
            /// The maximum period of inactivity the server promises in the server to client
            /// direction, from the connectionDetails of the most recent Connected message.
            /// Null when the server declines to make that promise, in which case no idle
            /// timeout is applied (CD2h).
            /// </summary>
            public TimeSpan? MaxIdleInterval { get; internal set; }

            /// <summary>
            ///     Information relating to the transition to the current state,
            ///     as an Ably ErrorInfo object. This contains an error code and
            ///     message and, in the failed state in particular, provides diagnostic
            ///     error information.
            /// </summary>
            public ErrorInfo ErrorReason { get; set; }

            public ConnectionStateBase CurrentStateObject { get; set; }

            public ConnectionState State => CurrentStateObject.State;

            public ConnectionStateChange UpdateState(ConnectionStateBase state, ILogger logger)
            {
                if (!state.IsUpdate && state.State == State)
                {
                    return null;
                }

                if (logger.IsDebug)
                {
                    logger.Debug($"Updating state to `{state.State}`");
                }

                var oldState = State;
                var newState = state.State;
                CurrentStateObject = state;
                ErrorReason = state.Error;
                var connectionEvent = oldState == newState ? ConnectionEvent.Update : newState.ToConnectionEvent();
                return new ConnectionStateChange(connectionEvent, oldState, newState, state.RetryIn, ErrorReason);
            }

            public void Update(ConnectionInfo info, bool isUpdate)
            {
                // Guarded differently on purpose. connectionId is a top-level field and always
                // meaningful, per RTN8b. connectionKey lives inside connectionDetails, and RTN21
                // scopes an override to "the attributes within ConnectionDetails" - so a CONNECTED
                // carrying none overrides no key, and emptying it would leave a live connection with
                // nothing to resume with under RTN15b. Clearing the key belongs to ClearKey and
                // ClearKeyAndId, at the points that mean it.
                Id = info.ConnectionId;

                if (info.ConnectionKey.IsNotEmpty())
                {
                    Key = info.ConnectionKey;
                }

                if (info.ConnectionStateTtl.HasValue)
                {
                    ConnectionStateTtl = info.ConnectionStateTtl.Value;
                }

                // RTN23a measures against the maxIdleInterval "sent in the connectionDetails of the
                // most recent CONNECTED message received on that transport", so the promise belongs
                // to the transport that carried it. Hence the two cases, which isUpdate separates:
                //
                //  - A CONNECTED starting a new transport must not inherit the old threshold. An
                //    omitted field is Ably declining to promise anything, so detection stands down.
                //    Strictly unspecified - RTN23a says the field "will be sent" and CD2h licenses
                //    arbitrary inactivity for an explicit 0 - but failing open matches CD2h's
                //    outcome for 0, and ably-js.
                //  - A CONNECTED arriving while already CONNECTED is an RTN24 update on the
                //    transport we already hold, so an omitted field is not a withdrawal and the
                //    previous value stands. ably-js keeps it with the same guard.
                if (isUpdate == false || info.MaxIdleInterval.HasValue)
                {
                    MaxIdleInterval = info.MaxIdleInterval;
                }
            }

            public void ClearKeyAndId()
            {
                Id = string.Empty;
                Key = string.Empty;
            }

            public void SetConfirmedAlive(DateTimeOffset now)
            {
                ConfirmedAliveAt = now;
            }

            public void ClearKey()
            {
                Key = string.Empty;
            }

            public long IncrementSerial()
            {
                return MessageSerial++;
            }
        }

        public List<PingRequest> PingRequests { get; set; } = new List<PingRequest>();

        public ConnectionData Connection { get; private set; }

        public ConnectionAttemptsInfo AttemptsInfo { get; }

        public List<MessageAndCallback> PendingMessages { get; }

        public readonly List<MessageAndCallback> WaitingForAck = new List<MessageAndCallback>();

        public void AddAckMessage(ProtocolMessage message, Action<bool, ErrorInfo> callback) =>
            WaitingForAck.Add(new MessageAndCallback(message, callback));

        public RealtimeState()
            : this(null)
        {
        }

        public RealtimeState(List<string> fallbackHosts, Func<DateTimeOffset> now = null)
        {
            Connection = new ConnectionData(fallbackHosts);
            AttemptsInfo = new ConnectionAttemptsInfo(now);
            PendingMessages = new List<MessageAndCallback>();
        }

        public JObject WhatDoIHave()
        {
            var stateJson = new JObject
            {
                ["connection"] = JObject.FromObject(Connection),
                ["pings"] = JArray.FromObject(PingRequests),
                ["attempts"] = JObject.FromObject(AttemptsInfo),
                ["pendingMessages"] = JArray.FromObject(PendingMessages),
                ["waitingForAck"] = JArray.FromObject(WaitingForAck),
            };
            return stateJson;
        }
    }
}
