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

            public bool HasConnectionStateTtlPassed(Func<DateTimeOffset> now)
            {
                return ConfirmedAliveAt?.Add(ConnectionStateTtl) < now();
            }

            public void Update(ConnectionInfo info)
            {
                Id = info.ConnectionId;
                Key = info.ConnectionKey;
                if (info.ConnectionStateTtl.HasValue)
                {
                    ConnectionStateTtl = info.ConnectionStateTtl.Value;
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
