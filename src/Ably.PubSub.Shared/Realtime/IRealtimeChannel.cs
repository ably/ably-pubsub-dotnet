using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Push;

namespace Ably.PubSub.Realtime
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
        /// Publishes, deletes, retrieves and subscribes to the annotations of the messages on this channel (RTL26).
        /// Experimental: the annotations API may change.
        /// </summary>
        RealtimeAnnotations Annotations { get; }

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
        /// <returns>
        /// A task of <see cref="Result{T}"/>. Failure (including a NACK or the confirmation timing out) is reported as a failed
        /// result rather than thrown (RTL6i). On success <c>Value</c> holds the serials of the published messages (RTL6j), where an
        /// individual serial is null if the message was discarded due to a configured conflation rule (PBR2a); <c>Value</c> itself is
        /// null when the server's acknowledgement carried no result (a connection using a protocol version older than 5).
        /// </returns>
        Task<Result<PublishResult>> PublishAsync(string eventName, object data, string clientId = null);

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
        /// <returns>
        /// A task of <see cref="Result{T}"/>. Failure (including a NACK or the confirmation timing out) is reported as a failed
        /// result rather than thrown (RTL6i). On success <c>Value</c> holds the serials of the published messages (RTL6j), where an
        /// individual serial is null if the message was discarded due to a configured conflation rule (PBR2a); <c>Value</c> itself is
        /// null when the server's acknowledgement carried no result (a connection using a protocol version older than 5).
        /// </returns>
        Task<Result<PublishResult>> PublishAsync(Message message);

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
        /// <returns>
        /// A task of <see cref="Result{T}"/>. Failure (including a NACK or the confirmation timing out) is reported as a failed
        /// result rather than thrown (RTL6i). On success <c>Value</c> holds the serials of the published messages (RTL6j), where an
        /// individual serial is null if the message was discarded due to a configured conflation rule (PBR2a); <c>Value</c> itself is
        /// null when the server's acknowledgement carried no result (a connection using a protocol version older than 5).
        /// </returns>
        Task<Result<PublishResult>> PublishAsync(IEnumerable<Message> messages);

        /// <summary>
        /// Updates an existing message (RTL32). The serial of <paramref name="message"/> identifies the message to update and is
        /// required (RTL32a); an empty serial throws an <see cref="AblyException"/> with code 40003. The message passed in is not
        /// modified (RTL32c). The same connection and channel state conditions apply as for publishing a message.
        /// </summary>
        /// <param name="message">a message with a populated serial and the fields to apply.</param>
        /// <param name="operation">optional description of the update, sent as the version of the message (RTL32b2).</param>
        /// <param name="parameters">optional publish parameters, sent in the params of the protocol message (RTL32e).</param>
        /// <returns>
        /// A task of <see cref="Result{T}"/> which completes on the ACK or NACK. Failure (including a NACK or the confirmation
        /// timing out) is reported as a failed result rather than thrown. On success <c>Value</c> holds the version serial of the
        /// edit (RTL32d); its <see cref="UpdateDeleteResult.VersionSerial"/> is null if the message was superseded, or if the
        /// server's acknowledgement carried no result (a connection using a protocol version older than 5).
        /// </returns>
        Task<Result<UpdateDeleteResult>> UpdateMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

        /// <summary>
        /// Marks a message as deleted (RTL32). The message is not removed from the history of the channel.
        /// See <see cref="UpdateMessageAsync(Message, MessageOperation, IDictionary{string, string})"/> for the arguments and the result.
        /// </summary>
        /// <param name="message">a message with a populated serial.</param>
        /// <param name="operation">optional description of the delete, sent as the version of the message (RTL32b2).</param>
        /// <param name="parameters">optional publish parameters, sent in the params of the protocol message (RTL32e).</param>
        /// <returns>A task of <see cref="Result{T}"/> holding the version serial of the delete (RTL32d).</returns>
        Task<Result<UpdateDeleteResult>> DeleteMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

        /// <summary>
        /// Appends the data of the given message to the data of an existing message (RTL32).
        /// See <see cref="UpdateMessageAsync(Message, MessageOperation, IDictionary{string, string})"/> for the arguments and the result.
        /// </summary>
        /// <param name="message">a message with a populated serial and the data to append.</param>
        /// <param name="operation">optional description of the append, sent as the version of the message (RTL32b2).</param>
        /// <param name="parameters">optional publish parameters, sent in the params of the protocol message (RTL32e).</param>
        /// <returns>A task of <see cref="Result{T}"/> holding the version serial of the append (RTL32d).</returns>
        Task<Result<UpdateDeleteResult>> AppendMessageAsync(Message message, MessageOperation operation = null, IDictionary<string, string> parameters = null);

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
        /// Retrieves the latest version of the message with the given serial. Identical to
        /// <see cref="Http.IHttpChannel.GetMessageAsync(string)"/> (RTL28).
        /// </summary>
        /// <param name="serial">the serial of the message to retrieve.</param>
        /// <returns>The decoded <see cref="Message"/>.</returns>
        Task<Message> GetMessageAsync(string serial);

        /// <summary>
        /// Retrieves the latest version of the given message (RTL28, RSL11a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">a message which has a serial.</param>
        /// <returns>The decoded <see cref="Message"/>.</returns>
        Task<Message> GetMessageAsync(Message message);

        /// <summary>
        /// Retrieves all the versions of the message with the given serial. Identical to
        /// <see cref="Http.IHttpChannel.GetMessageVersionsAsync(string, PaginatedRequestParams)"/> (RTL31).
        /// </summary>
        /// <param name="serial">the serial of the message whose versions are retrieved.</param>
        /// <param name="query">optional <see cref="PaginatedRequestParams"/> query.</param>
        /// <returns>A <see cref="PaginatedResult{T}"/> of the versions of the message.</returns>
        Task<PaginatedResult<Message>> GetMessageVersionsAsync(string serial, PaginatedRequestParams query = null);

        /// <summary>
        /// Retrieves all the versions of the given message (RTL31, RSL14a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">a message which has a serial.</param>
        /// <param name="query">optional <see cref="PaginatedRequestParams"/> query.</param>
        /// <returns>A <see cref="PaginatedResult{T}"/> of the versions of the message.</returns>
        Task<PaginatedResult<Message>> GetMessageVersionsAsync(Message message, PaginatedRequestParams query = null);

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
