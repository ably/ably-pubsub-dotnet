using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IO.Ably.Push;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// Interface representing a Realtime channel.
    /// Implement <see cref="IEventEmitter{TEvent, TArgs}"/>.
    /// </summary>
    public interface IRealtimeChannel : IEventEmitter<ChannelEvent, ChannelStateChange>
    {
        /// <summary>
        ///     Indicates the current state of this channel.
        ///     <see cref="ChannelState"/> for more details.
        /// </summary>
        ChannelState State { get; }

        /// <summary>
        /// Channel params that the server has recognised and validated.
        /// It cannot be used to set ChannelParams on this channel. See <see cref="SetOptions"/>.
        /// </summary>
        ReadOnlyChannelParams Params { get; }

        /// <summary>
        /// Channel modes received from the server during Attach of this channel.
        /// It cannot be used to set ChannelModes on this channel. See <see cref="SetOptions"/>.
        /// </summary>
        ReadOnlyChannelModes Modes { get; }

        /// <summary>
        /// Channel name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Presence object for the current channel.
        /// </summary>
        Presence Presence { get; }

        /// <summary>
        /// Channel options.
        /// </summary>
        ChannelOptions Options { get; }

        /// <summary>
        /// Current error emitted on this channel.
        /// </summary>
        ErrorInfo ErrorReason { get; }

        /// <summary>
        /// <see cref="ChannelProperties"/>.
        /// </summary>
        ChannelProperties Properties { get; }

        /// <summary>
        /// A convenient set of methods that help with managing subscriptions to a push channel.
        /// <see cref="PushChannel"/>.
        /// </summary>
        PushChannel Push { get; }

        /// <summary>
        /// EventHandler for notifying Clients with channel state changes.
        /// </summary>
        event EventHandler<ChannelStateChange> StateChanged;

        /// <summary>
        /// EventHandler for notifying Client with Errors emitted on the channel.
        /// </summary>
        event EventHandler<ChannelErrorEventArgs> Error;

        /// <summary>
        /// Attach to this channel, and execute callback if provided.
        /// </summary>
        /// <param name="callback">optional callback.</param>
        void Attach(Action<bool, ErrorInfo> callback = null);

        /// <summary>
        /// Attach to this channel and return a Task that can be awaited.
        /// The task completes when Attach has completed.
        /// </summary>
        /// <returns>Task of Result.</returns>
        Task<Result> AttachAsync();

        /// <summary>
        /// Detach from this channel and execute callback if provided.
        /// </summary>
        /// <param name="callback">optional callback.</param>
        void Detach(Action<bool, ErrorInfo> callback = null);

        /// <summary>
        /// Detach from this channel and return a Task that can be awaited.
        /// The task complete when the Detach has completed.
        /// </summary>
        /// <returns>Task of Result.</returns>
        Task<Result> DetachAsync();

        /// <summary>Subscribe a handler to all messages.</summary>
        /// <param name="handler">the provided handler will be called when messages are received.</param>
        void Subscribe(Action<Message> handler);

        /// <summary>Subscribe a handler to only messages whose name member matches the string name.</summary>
        /// <param name="eventName">name of the event (usually name of the message).</param>
        /// <param name="handler">the provided handler will be called every time a message with <paramref name="eventName"/> is received.</param>
        void Subscribe(string eventName, Action<Message> handler);

        /// <summary>
        /// Unsubscribe a handler so it's no longer called.
        /// </summary>
        /// <param name="handler">handler to be unsubscribed.</param>
        void Unsubscribe(Action<Message> handler);

        /// <summary>
        /// Unsubscribe a handler for a specific eventName.
        /// </summary>
        /// <param name="eventName">event name (usually name of the message).</param>
        /// <param name="handler">handler to be unsubscribed.</param>
        void Unsubscribe(string eventName, Action<Message> handler);

        /// <summary>
        /// Unsubscribe all handlers.
        /// </summary>
        void Unsubscribe();

        /// <summary>Publish a single message on this channel based on a given event name and payload.</summary>
        /// <param name="name">The event name.</param>
        /// <param name="data">The payload of the message.</param>
        /// <param name="callback">handler to be notified if the operation succeeded.</param>
        /// <param name="clientId">optional, id of the client.</param>
        void Publish(string name, object data, Action<bool, ErrorInfo> callback = null, string clientId = null);

        /// <summary>
        /// Async implementation of publish. Use this method if you want to
        /// ensure the message was received by ably. If you don't want to wait for the Ack message
        /// then use <see cref="Publish(string, object, Action{bool, ErrorInfo}, string)"/>.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="data">The payload of the message.</param>
        /// <param name="clientId">optional, id of the client.</param>
        /// <returns>Task of Result.</returns>
        Task<Result> PublishAsync(string eventName, object data, string clientId = null);

        /// <summary>
        /// Publish a single message and execute an optional callback when completed.
        /// </summary>
        /// <param name="message">Message to be published.</param>
        /// <param name="callback">optional callback that is executed when the message is confirmed by the server.</param>
        void Publish(Message message, Action<bool, ErrorInfo> callback = null);

        /// <summary>
        /// Publish a single message.
        /// The resulted task completes when a response from the server with Ack or Nack.
        /// Use this if you care whether the message has been received.
        /// </summary>
        /// <param name="message">Message to be published.</param>
        /// <returns>Task of Result.</returns>
        Task<Result> PublishAsync(Message message);

        /// <summary>
        /// Publish a number of messages and execute an optional callback when completed.
        /// </summary>
        /// <param name="messages">list of messages to be published.</param>
        /// <param name="callback">optional, callback to be executed on Ack on Nack received from the server.</param>
        void Publish(IEnumerable<Message> messages, Action<bool, ErrorInfo> callback = null);

        /// <summary>
        /// Publish a list of messages.
        /// The resulted task completes when a response from the server with Ack or Nack.
        /// </summary>
        /// <param name="messages">list of messages.</param>
        /// <returns>Task of Result.</returns>
        Task<Result> PublishAsync(IEnumerable<Message> messages);

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        Task<PaginatedResult<Message>> HistoryAsync();

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <param name="untilAttach">indicates whether it should pass the latest attach serial to 'fromSerial' parameter.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        /// <exception cref="AblyException">Throws an error if untilAttach is true and the channel is not currently attached.</exception>
        [Obsolete("No need for untilAttach param since presence sync happens on attach, use HistoryAsync() instead")]
        Task<PaginatedResult<Message>> HistoryAsync(bool untilAttach);

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query);

        /// <summary>
        /// Returns past message of this channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <param name="untilAttach">indicates whether it should pass the latest attach serial to 'fromSerial' parameter.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of past Messages.</returns>
        /// <exception cref="AblyException">Throws an error if untilAttach is true and the channel is not currently attached.</exception>
        [Obsolete("No need for untilAttach param since presence sync happens on attach, use HistoryAsync(PaginatedRequestParams query) instead")]
        Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query, bool untilAttach);

        /// <summary>
        /// Updates the options for a channel. If the ChannelModes or ChannelParams differ and the channel is Attaching or Attached
        /// then the channel will be reattached.
        /// </summary>
        /// <param name="options">the new <see cref="ChannelOptions"/> for the channel.</param>
        /// <param name="callback">optional callback that will indicate whether the method succeeded.</param>
        void SetOptions(ChannelOptions options, Action<bool, ErrorInfo> callback = null);

        /// <summary>
        /// Updates the options for a channel. If the ChannelModes or ChannelParams differ and the channel is Attaching or Attached
        /// then the channel will be reattached.
        /// </summary>
        /// <param name="options">the new <see cref="ChannelOptions"/> for the channel.</param>
        /// <returns>returns Result to indicate whether the operation completed successfully or failed.</returns>
        Task<Result> SetOptionsAsync(ChannelOptions options);
    }
}
