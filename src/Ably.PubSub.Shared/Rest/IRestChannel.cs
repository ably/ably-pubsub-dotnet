using System.Collections.Generic;
using System.Threading.Tasks;
using IO.Ably.Push;

namespace IO.Ably.Rest
{
    /// <summary>
    /// The Ably Realtime service organises the traffic within any application into named channels.
    /// Channels are the "unit" of message distribution; clients attach to channels to subscribe to messages,
    /// and every message broadcast by the service is associated with a unique channel.
    /// A channel cannot be instantiated but needs to be created using the AblyRest.Channels.Get("channelname").
    /// </summary>
    public interface IRestChannel
    {
        /// <summary>
        /// Publish a message to the channel.
        /// </summary>
        /// <param name="name">The event name of the message to publish.</param>
        /// <param name="data">The message payload. Allowed payloads are string, objects and byte[].</param>
        /// <param name="clientId">Explicit message clientId.</param>
        /// <returns>Task.</returns>
        Task PublishAsync(string name, object data, string clientId = null);

        /// <summary>
        /// Publish a single message object to the channel.
        /// </summary>
        /// <param name="message"><see cref="Message"/>.</param>
        /// <returns>Task.</returns>
        Task PublishAsync(Message message);

        /// <summary>
        /// Publish a list of messages to the channel.
        /// </summary>
        /// <param name="messages">a list of messages.</param>
        /// <returns>Task.</returns>
        Task PublishAsync(IEnumerable<Message> messages);

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        Task<PaginatedResult<Message>> HistoryAsync();

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query);

        /// <summary>
        /// Returns the active status for the channel including the number of publishers, subscribers and presenceMembers etc.
        /// </summary>
        /// <returns><see cref="ChannelDetails"/>Channel Details.</returns>
        Task<ChannelDetails> StatusAsync();

        /// <summary>
        /// Name of the channel.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Returns the Presence object.
        /// </summary>
        IPresence Presence { get; }

        /// <summary>
        /// A convenient set of methods that help with managing subscriptions to a push channel.
        /// <see cref="PushChannel"/>.
        /// </summary>
        PushChannel Push { get; }

        /// <summary>
        /// Sync version of <see cref="PublishAsync(string, object, string)"/>.
        /// Prefer async method where possible.
        /// </summary>
        /// <param name="name">message name.</param>
        /// <param name="data">optional message data object.</param>
        /// <param name="clientId">optional client id.</param>
        void Publish(string name, object data, string clientId = null);

        /// <summary>
        /// Sync version of <see cref="PublishAsync(Message)"/>.
        /// Prefer async method where possible.
        /// </summary>
        /// <param name="message">message to publish.</param>
        void Publish(Message message);

        /// <summary>
        /// Sync version of <see cref="PublishAsync(IEnumerable{Message})"/>.
        /// Prefer sync version where possible.
        /// </summary>
        /// <param name="messages">array of messages to publish.</param>
        void Publish(IEnumerable<Message> messages);

        /// <summary>
        /// Sync version of <see cref="HistoryAsync()"/>.
        /// Prefer async version where possible.
        /// </summary>
        /// <returns><see cref="PaginatedResult{T}"/> of Messages.</returns>
        PaginatedResult<Message> History();

        /// <summary>
        /// Sync version of <see cref="HistoryAsync(PaginatedRequestParams)"/>.
        /// Prefer async version where possible.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of Messages.</returns>
        PaginatedResult<Message> History(PaginatedRequestParams query);

        /// <summary>
        /// Sync version of <see cref="StatusAsync()"/>.
        /// Prefer async version where possible.
        /// </summary>
        /// <returns><see cref="ChannelDetails"/>Channel Details.</returns>
        ChannelDetails Status();
    }

    /// <summary>
    /// Interface representing Rest Presence operations.
    /// </summary>
    public interface IPresence
    {
        /// <summary>
        /// Obtain the set of members currently present for a channel.
        /// </summary>
        /// <param name="limit">Maximum number of members to retrieve up to 1,000, defaults to 100.</param>
        /// <param name="clientId">optional clientId filter for the member.</param>
        /// <param name="connectionId">optional connectionId filter for the member.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of the PresenceMessages.</returns>
        Task<PaginatedResult<PresenceMessage>> GetAsync(int? limit = null, string clientId = null, string connectionId = null);

        /// <summary>
        /// Return the presence messages history for the channel.
        /// </summary>
        /// <returns><see cref="PaginatedResult{T}"/> of Presence messages.</returns>
        Task<PaginatedResult<PresenceMessage>> HistoryAsync();

        /// <summary>
        /// Return the presence messages history for the channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of Presence messages.</returns>
        Task<PaginatedResult<PresenceMessage>> HistoryAsync(PaginatedRequestParams query);

        /// <summary>
        /// Obtain the set of members currently present for a channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of Presence messages.</returns>
        Task<PaginatedResult<PresenceMessage>> GetAsync(PaginatedRequestParams query);
    }
}
