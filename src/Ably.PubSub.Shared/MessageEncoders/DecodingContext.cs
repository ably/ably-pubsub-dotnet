using System;

namespace Ably.PubSub.MessageEncoders
{
    /// <summary>
    /// Class used to provide context between different encoders.
    /// </summary>
    internal class DecodingContext
    {
        public PayloadCache PreviousPayload { get; set; }

        /// <summary>
        /// The channel options for the current channel.
        /// </summary>
        public ChannelOptions ChannelOptions { get; set; }

        /// <summary>
        /// Replaces the vcdiff codec when set: <c>(base, delta) =&gt; result</c>. Test-only, see ClientOptions.DeltaDecoder.
        /// </summary>
        internal Func<byte[], byte[], byte[]> DeltaDecoder { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DecodingContext"/> class.
        /// </summary>
        public DecodingContext()
        {
            ChannelOptions = new ChannelOptions();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DecodingContext"/> class.
        /// </summary>
        /// <param name="options">Channel options used for the encode / decode operations.</param>
        public DecodingContext(ChannelOptions options)
        {
            ChannelOptions = options ?? new ChannelOptions();
        }
    }

    /// <summary>
    /// Helpers methods for <see cref="DecodingContext"/>.
    /// </summary>
    internal static class ChannelOptionsExtensions
    {
        /// <summary>
        /// Creates a new <see cref="DecodingContext"/> from the provided <see cref="ChannelOptions"/>.
        /// </summary>
        /// <param name="options">the <see cref="ChannelOptions"/> used in the new context.</param>
        /// <returns><see cref="DecodingContext"/> created with passed Channel options.</returns>
        public static DecodingContext ToDecodingContext(this ChannelOptions options)
        {
            return new DecodingContext(options ?? new ChannelOptions());
        }
    }
}
