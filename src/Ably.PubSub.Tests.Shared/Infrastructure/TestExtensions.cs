using System;
using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Tests.Infrastructure
{
    public static class TestExtensions
    {
        internal static Task WaitForState(this IPubSubRealtimeClient realtime, ConnectionState awaitedState = ConnectionState.Connected, TimeSpan? waitSpan = null)
        {
            var connectionAwaiter = new ConnectionAwaiter(realtime.Connection, awaitedState);
            if (waitSpan.HasValue)
            {
                return connectionAwaiter.Wait(waitSpan.Value);
            }

            return connectionAwaiter.Wait();
        }

        internal static void ExecuteCommand(this IPubSubRealtimeClient client, RealtimeCommand command)
        {
            ((PubSubRealtimeClient)client).Workflow.QueueCommand(command);
        }

        internal static async Task ProcessMessage(this IPubSubRealtimeClient client, ProtocolMessage message)
        {
            ((PubSubRealtimeClient)client).Workflow.QueueCommand(ProcessMessageCommand.Create(message));
            await client.ProcessCommands();
        }
    }
}
