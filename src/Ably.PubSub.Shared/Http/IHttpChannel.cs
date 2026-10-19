using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Push;

namespace Ably.PubSub.Http
{
    /// <summary>
    /// The Ably Realtime service organises the traffic within any application into named channels.
    /// Channels are the "unit" of message distribution; clients attach to channels to subscribe to messages,
    /// and every message broadcast by the service is associated with a unique channel.
    /// A channel cannot be instantiated but needs to be created using the PubSubHttpClient.Channels.Get("channelname").
    /// </summary>
    public interface IHttpChannel
    {
        /// <summary>
        /// Publish a message to the channel.
        /// </summary>
        /// <param name="name">The event name of the message to publish.</param>
        /// <param name="data">The message payload. Allowed payloads are string, objects and byte[].</param>
        /// <param name="clientId">Explicit message clientId.</param>
        /// <returns>
        /// A task whose result holds the serials of the published messages (RSL1, RSL1n). An individual serial is null if the
        /// message was discarded due to a configured conflation rule (PBR2a). The result is never null: an empty or absent
        /// response body gives a <see cref="PublishResult"/> with no serials. Failures are thrown as <see cref="AblyException"/>.
        /// </returns>
        Task<PublishResult> PublishAsync(string name, object data, string clientId = null);

        /// <summary>
        /// Publish a single message object to the channel.
        /// </summary>
        /// <param name="message"><see cref="Message"/>.</param>
        /// <returns>
        /// A task whose result holds the serial of the published message (RSL1, RSL1n); see
        /// <see cref="PublishAsync(string, object, string)"/> for the null semantics.
        /// </returns>
        Task<PublishResult> PublishAsync(Message message);

        /// <summary>
        /// Publish a list of messages to the channel.
        /// </summary>
        /// <param name="messages">a list of messages.</param>
        /// <returns>
        /// A task whose result holds the serials of the published messages, 1:1 with <paramref name="messages"/> (RSL1, RSL1n);
        /// see <see cref="PublishAsync(string, object, string)"/> for the null semantics.
        /// </returns>
        Task<PublishResult> PublishAsync(IEnumerable<Message> messages);

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
        /// Retrieves the latest version of the message with the given serial (RSL11). The payload is decoded (RSL11c).
        /// </summary>
        /// <param name="serial">the serial of the message to retrieve (RSL11a).</param>
        /// <returns>The decoded <see cref="Message"/> (RSL11c).</returns>
        Task<Message> GetMessageAsync(string serial);

        /// <summary>
        /// Retrieves the latest version of the given message (RSL11a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">a message which has a serial.</param>
        /// <returns>The decoded <see cref="Message"/> (RSL11c).</returns>
        Task<Message> GetMessageAsync(Message message);

        /// <summary>
        /// Retrieves all the versions of the message with the given serial (RSL14).
        /// </summary>
        /// <param name="serial">the serial of the message whose versions are retrieved (RSL14a).</param>
        /// <param name="query">optional <see cref="PaginatedRequestParams"/> query (RSL14a).</param>
        /// <returns>A <see cref="PaginatedResult{T}"/> of the versions of the message (RSL14c).</returns>
        Task<PaginatedResult<Message>> GetMessageVersionsAsync(string serial, PaginatedRequestParams query = null);

        /// <summary>
        /// Retrieves all the versions of the given message (RSL14a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">a message which has a serial.</param>
        /// <param name="query">optional <see cref="PaginatedRequestParams"/> query (RSL14a).</param>
        /// <returns>A <see cref="PaginatedResult{T}"/> of the versions of the message (RSL14c).</returns>
        Task<PaginatedResult<Message>> GetMessageVersionsAsync(Message message, PaginatedRequestParams query = null);

        /// <summary>
        /// Updates an existing message (RSL15). The fields of <paramref name="message"/> replace those of the existing
        /// message; the serial identifies the message to update and is required (RSL15a). The message passed in is not
        /// modified (RSL15c).
        /// </summary>
        /// <param name="message">a message with a populated serial and the fields to apply.</param>
        /// <param name="operation">optional description of the update, sent as the version of the message (RSL15b7).</param>
        /// <param name="parameters">optional publish parameters, sent in the querystring (RSL15f).</param>
        /// <returns>The <see cref="UpdateDeleteResult"/> holding the version serial of the update (RSL15e). Failures are thrown as <see cref="AblyException"/>.</returns>
        Task<UpdateDeleteResult> UpdateMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

        /// <summary>
        /// Marks a message as deleted (RSL15). The message is not removed from the history of the channel.
        /// See <see cref="UpdateMessageAsync(Message, MessageOperation, IDictionary{string, string})"/> for the arguments.
        /// </summary>
        /// <param name="message">a message with a populated serial.</param>
        /// <param name="operation">optional description of the delete, sent as the version of the message (RSL15b7).</param>
        /// <param name="parameters">optional publish parameters, sent in the querystring (RSL15f).</param>
        /// <returns>The <see cref="UpdateDeleteResult"/> holding the version serial of the delete (RSL15e).</returns>
        Task<UpdateDeleteResult> DeleteMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

        /// <summary>
        /// Appends the data of the given message to the data of an existing message (RSL15).
        /// See <see cref="UpdateMessageAsync(Message, MessageOperation, IDictionary{string, string})"/> for the arguments.
        /// </summary>
        /// <param name="message">a message with a populated serial and the data to append.</param>
        /// <param name="operation">optional description of the append, sent as the version of the message (RSL15b7).</param>
        /// <param name="parameters">optional publish parameters, sent in the querystring (RSL15f).</param>
        /// <returns>The <see cref="UpdateDeleteResult"/> holding the version serial of the append (RSL15e).</returns>
        Task<UpdateDeleteResult> AppendMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

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
        /// Publishes, deletes and retrieves annotations of the messages on this channel (RSL10).
        /// Experimental: the annotations API may change.
        /// </summary>
        RestAnnotations Annotations { get; }

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
        /// <returns>The <see cref="PublishResult"/> holding the serials of the published messages (RSL1n).</returns>
        PublishResult Publish(string name, object data, string clientId = null);

        /// <summary>
        /// Sync version of <see cref="PublishAsync(Message)"/>.
        /// Prefer async method where possible.
        /// </summary>
        /// <param name="message">message to publish.</param>
        /// <returns>The <see cref="PublishResult"/> holding the serial of the published message (RSL1n).</returns>
        PublishResult Publish(Message message);

        /// <summary>
        /// Sync version of <see cref="PublishAsync(IEnumerable{Message})"/>.
        /// Prefer sync version where possible.
        /// </summary>
        /// <param name="messages">array of messages to publish.</param>
        /// <returns>The <see cref="PublishResult"/> holding the serials of the published messages (RSL1n).</returns>
        PublishResult Publish(IEnumerable<Message> messages);

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
