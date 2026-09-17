using System.Collections.Generic;
using IO.Ably.Rest;

namespace IO.Ably
{
    /// <summary>
    /// Interface for managing channel objects.
    /// </summary>
    /// <typeparam name="T">type of channel (RealtimeChannel or RestChannel).</typeparam>
    public interface IChannels<out T> : IEnumerable<T>
    {
        /// <summary>
        /// Create or retrieve a channel with the specified name.
        /// </summary>
        /// <param name="name">name of the channel.</param>
        /// <returns>an instance of <see cref="RestChannel"/>.</returns>
        T Get(string name);

        /// <summary>
        /// Create or retrieve a channel with the specified name and options
        /// If the channel already exists the channel's options are updated and
        /// the channel is reattached if the new options contain Modes or Params.
        /// </summary>
        /// <param name="name">name of the channel.</param>
        /// <param name="options"><see cref="ChannelOptions"/>.</param>
        /// <returns>an instance of <see cref="RestChannel"/>.</returns>
        T Get(string name, ChannelOptions options);

        /// <summary>
        /// Same as the Get(string name)/>.
        /// </summary>
        /// <param name="name">name of the channel.</param>
        /// <returns>an instance of <see cref="RestChannel"/>.</returns>
        T this[string name] { get; }

        /// <summary>
        /// Removes a specified channel from the Channels collection.
        /// </summary>
        /// <param name="name">name of the channel.</param>
        /// <returns>true if success and false if Channel was not found.</returns>
        bool Release(string name);

        /// <summary>
        /// Removes and disposes all channels.
        /// </summary>
        void ReleaseAll();

        /// <summary>
        /// Checks if a channel exist.
        /// </summary>
        /// <param name="name">name of channel.</param>
        /// <returns>true / false.</returns>
        bool Exists(string name);
    }
}
