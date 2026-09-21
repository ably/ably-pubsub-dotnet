using System;

namespace IO.Ably.Tests.Extensions
{
    /// <summary>
    /// Helper methods to make adding channel options easier.
    /// </summary>
    public static class ChannelOptionsExtensions
    {
        /// <summary>
        /// Makes the ChannelOptions API easier to use by providing this convenience method to add ChannelModes.
        /// You can do `new ChannelOptions().WithModes(ChannelMode.Publish, ChannelMode.Subscribe)`.
        /// </summary>
        /// <param name="options">the <see cref="ChannelOptions"/> that will be modified.</param>
        /// <param name="modes">the <see cref="ChannelMode"/> list that will be added to ChannelOptions.</param>
        /// <returns>the same ChannelOptions object so other calls can be chained.</returns>
        public static ChannelOptions WithModes(this ChannelOptions options, params ChannelMode[] modes)
        {
            foreach (var mode in modes)
            {
                options.Modes.Add(mode);
            }

            return options;
        }

        /// <summary>
        /// Makes the ChannelOptions API easier to use by providing this convenience method to add ChannelParams.
        /// You can do `new ChannelOptions().WithParam("key", "value")`.
        /// </summary>
        /// <param name="options">the <see cref="ChannelOptions"/> that will be modified.</param>
        /// <param name="key">the channel param key.</param>
        /// <param name="value">the channel param value.</param>
        /// <returns>the same ChannelOptions object so other calls can be chained.</returns>
        public static ChannelOptions WithParam(this ChannelOptions options, string key, string value)
        {
            options.Params[key] = value;
            return options;
        }

        /// <summary>
        /// Makes adding Rewind by a number of messages easier.
        /// Full documentation can be found here: https://ably.com/docs/realtime/channels/channel-parameters/overview#rewind.
        /// </summary>
        /// <param name="options">the <see cref="ChannelOptions"/> that will be modified.</param>
        /// <param name="numberOfMessages">the value passed to Rewind param.</param>
        /// <returns>the same ChannelOptions object so other calls can be chained.</returns>
        public static ChannelOptions WithRewind(this ChannelOptions options, int numberOfMessages)
        {
            return options.WithParam("rewind", numberOfMessages.ToString());
        }

        /// <summary>
        /// Makes adding Rewind by a time period with an option interval easier.
        /// Full documentation can be found here: https://ably.com/docs/realtime/channels/channel-parameters/overview#rewind.
        /// </summary>
        /// <param name="options">the <see cref="ChannelOptions"/> that will be modified.</param>
        /// <param name="rewindBy">by how much should we rewind.</param>
        /// <param name="limit">the max number of messages that should be returned. Max: 100.</param>
        /// <returns>the same ChannelOptions object so other calls can be chained.</returns>
        public static ChannelOptions WithRewind(this ChannelOptions options, TimeSpan rewindBy, int? limit)
        {
            var result = options.WithParam("rewind", rewindBy.TotalSeconds + "s");
            if (limit.HasValue && limit > 0)
            {
                var l = Math.Min(limit.Value, 100);
                result.WithParam("rewindLimit", l.ToString());
            }

            return result;
        }
    }
}
