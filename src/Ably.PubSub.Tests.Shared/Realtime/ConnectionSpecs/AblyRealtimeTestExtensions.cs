using System;
using System.Net;
using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Tests.Realtime
{
    public static class AblyRealtimeTestExtensions
    {
        public static void FakeProtocolMessageReceived(this AblyRealtime client, ProtocolMessage message)
        {
            client.Workflow.QueueCommand(ProcessMessageCommand.Create(message));
        }

        public static void FakeMessageReceived(this AblyRealtime client, Message message, string channel = null)
        {
            client.FakeProtocolMessageReceived(
                new ProtocolMessage(ProtocolMessage.MessageAction.Message) { Messages = new[] { message }, Channel = channel });
        }

        public static async Task DisconnectWithRetryableError(this AblyRealtime client, bool waitForDisconnectedState = true)
        {
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            if (waitForDisconnectedState)
            {
                await client.WaitForState(ConnectionState.Disconnected);
            }
        }

        public static async Task DisconnectWithNonRetryableError(this AblyRealtime client, bool waitForDisconnectedState = true)
        {
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.Forbidden }
            });

            if (waitForDisconnectedState)
            {
                await client.WaitForState(ConnectionState.Disconnected);
            }
        }

        public static async Task ConnectClient(this AblyRealtime client)
        {
            await client.WaitForState(ConnectionState.Connecting, TimeSpan.FromMilliseconds(10000));

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                ConnectionId = "1"
            });

            await client.WaitForState(ConnectionState.Connected);
        }
    }
}
