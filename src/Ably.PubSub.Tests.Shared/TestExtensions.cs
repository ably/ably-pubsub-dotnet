using System;
using System.Threading.Tasks;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;

namespace IO.Ably.Tests
{
    public static class TestExtensions
    {
        /// <summary>
        /// This method yields the current thread and waits until the whole command queue is processed.
        /// </summary>
        /// <returns></returns>
        public static async Task ProcessCommands(this IRealtimeClient client)
        {
            var realtime = (AblyRealtime)client;
            var taskAwaiter = new TaskCompletionAwaiter();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(50);

                    if (realtime.Workflow.IsProcessingCommands() == false)
                    {
                        taskAwaiter.SetCompleted();
                    }
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await taskAwaiter.Task;
        }

        internal static TestTransportWrapper GetTestTransport(this IRealtimeClient client)
        {
            return ((AblyRealtime)client).ConnectionManager.Transport as TestTransportWrapper;
        }

        internal static void BlockActionFromSending(this IRealtimeClient client, ProtocolMessage.MessageAction action)
        {
            var transport = (TestTransportWrapper)((AblyRealtime)client).ConnectionManager.Transport;
            if (transport is null)
            {
                throw new Exception("Client is not using test transport so you can't add BlockedActions");
            }

            transport.BlockSendActions.Add(action);
        }

        internal static void BlockActionFromReceiving(this IRealtimeClient client, ProtocolMessage.MessageAction action)
        {
            var transport = (TestTransportWrapper)((AblyRealtime)client).ConnectionManager.Transport;
            if (transport is null)
            {
                throw new Exception("Client is not using test transport so you can't add BlockedActions");
            }

            transport.BlockReceiveActions.Add(action);
        }

        internal static void BeforeProtocolMessageProcessed(this AblyRealtime client, Action<ProtocolMessage> action)
        {
            var t = client.GetTestTransport();
            if (t != null)
            {
                t.BeforeDataProcessed = action;
            }

            if (client.Options.TransportFactory is TestTransportFactory f)
            {
                f.BeforeDataProcessed = action;
            }
        }
    }
}
