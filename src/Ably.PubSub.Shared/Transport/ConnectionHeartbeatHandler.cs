using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime.Workflow;
using Ably.PubSub.Types;

namespace Ably.PubSub.Transport
{
    internal class ConnectionHeartbeatHandler
    {
        private readonly ConnectionManager _manager;
        private readonly ILogger _logger;

        public ConnectionHeartbeatHandler(ConnectionManager manager, ILogger logger)
        {
            _manager = manager;
            _logger = logger;
        }

        public Task<bool> OnMessageReceived(ProtocolMessage message, RealtimeState state)
        {
            var canHandle = CanHandleMessage(message);
            if (canHandle && message.Id.IsNotEmpty())
            {
                var pingRequest = state.PingRequests.FirstOrDefault(x => x.Id.EqualsTo(message.Id));

                if (pingRequest != null)
                {
                    state.PingRequests.Remove(pingRequest);
                    TryCallback(pingRequest.Callback, GetElapsedTime(pingRequest), null);
                }
            }

            return Task.FromResult(canHandle);
        }

        private static bool CanHandleMessage(ProtocolMessage message)
        {
            return message.Action == ProtocolMessage.MessageAction.Heartbeat;
        }

        private void TryCallback(Action<TimeSpan?, ErrorInfo> action, TimeSpan? elapsed, ErrorInfo error)
        {
            try
            {
                action.Invoke(elapsed, error);
            }
            catch (Exception e)
            {
                _logger.Error("Error executing callback for Ping request", e);
            }
        }

        private TimeSpan? GetElapsedTime(PingRequest pingRequest)
        {
            var now = _manager.Now();
            return now - pingRequest.Created;
        }
    }
}
