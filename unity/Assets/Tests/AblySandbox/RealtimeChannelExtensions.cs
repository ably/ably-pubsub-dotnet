using System;
using System.Threading.Tasks;
using Ably.PubSub;
using Ably.PubSub.Realtime;

namespace Assets.Tests.AblySandbox
{
    public static class RealtimeChannelExtensions
    {
        public static async Task WaitForState(this IRealtimeChannel channel, ChannelState awaitedState = ChannelState.Attached, TimeSpan? waitSpan = null)
        {
            var channelAwaiter = new ChannelAwaiter(channel, awaitedState);
            var timespan = waitSpan.GetValueOrDefault(TimeSpan.FromSeconds(5));
            Result<bool> result = await channelAwaiter.WaitAsync(timespan);
            if (result.IsFailure)
            {
                throw new Exception($"Channel '{channel.Name}' did not reach '{awaitedState}' in {timespan.TotalSeconds} seconds. Current state {channel.State}");
            }
        }
    }
}
