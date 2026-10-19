using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Transport;

namespace Ably.PubSub.Realtime
{
    /// <summary>
    /// Publishes, deletes, retrieves and subscribes to the annotations of the messages on a realtime channel (RTL26).
    /// Accessed through <see cref="IRealtimeChannel.Annotations"/>.
    /// Experimental: the annotations API may change.
    /// </summary>
    public sealed class RealtimeAnnotations : IDisposable
    {
        private readonly RealtimeChannel _channel;
        private readonly Handlers<Annotation> _handlers = new Handlers<Annotation>(caseSensitive: true); // RTAN4c: the type filter matches exactly

        internal RealtimeAnnotations(RealtimeChannel channel)
        {
            _channel = channel;
        }

        private ILogger Logger => _channel.Logger;

        /// <summary>
        /// Publishes an annotation to the message with the given serial (RTAN1). The arguments are validated,
        /// and the annotation completed and its data encoded, as for <see cref="RestAnnotations.PublishAsync(string, Annotation)"/> (RTAN1a).
        /// The connection and channel state conditions are those of publishing a message (RTAN1b).
        /// </summary>
        /// <param name="messageSerial">the serial of the message to annotate.</param>
        /// <param name="annotation">the annotation to publish. The type is required.</param>
        /// <returns>A <see cref="Result"/> which indicates the success or failure of the publish once it is ACKed or NACKed (RTAN1d).</returns>
        public Task<Result> PublishAsync(string messageSerial, Annotation annotation)
        {
            return SendAsync(messageSerial, annotation, AnnotationAction.Create);
        }

        /// <summary>
        /// Publishes an annotation to the given message (RTAN1a, RSAN1a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the message to annotate.</param>
        /// <param name="annotation">the annotation to publish. The type is required.</param>
        /// <returns>A <see cref="Result"/> which indicates the success or failure of the publish once it is ACKed or NACKed (RTAN1d).</returns>
        public Task<Result> PublishAsync(Message message, Annotation annotation)
        {
            return SendAsync(message?.Serial, annotation, AnnotationAction.Create);
        }

        /// <summary>
        /// Deletes an annotation from the message with the given serial (RTAN2).
        /// Identical to <see cref="PublishAsync(string, Annotation)"/> except that the action of the annotation
        /// is set to <see cref="AnnotationAction.Delete"/> (RTAN2a).
        /// </summary>
        /// <param name="messageSerial">the serial of the annotated message.</param>
        /// <param name="annotation">the annotation to delete.</param>
        /// <returns>A <see cref="Result"/> which indicates the success or failure of the request once it is ACKed or NACKed.</returns>
        public Task<Result> DeleteAsync(string messageSerial, Annotation annotation)
        {
            return SendAsync(messageSerial, annotation, AnnotationAction.Delete);
        }

        /// <summary>
        /// Deletes an annotation from the given message (RTAN2a). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the annotated message.</param>
        /// <param name="annotation">the annotation to delete.</param>
        /// <returns>A <see cref="Result"/> which indicates the success or failure of the request once it is ACKed or NACKed.</returns>
        public Task<Result> DeleteAsync(Message message, Annotation annotation)
        {
            return SendAsync(message?.Serial, annotation, AnnotationAction.Delete);
        }

        /// <summary>
        /// Retrieves the annotations of the message with the given serial. Identical to
        /// <see cref="RestAnnotations.GetAsync(string, AnnotationsRequestParams)"/> (RTAN3a).
        /// </summary>
        /// <param name="messageSerial">the serial of the annotated message.</param>
        /// <param name="query">optional query parameters.</param>
        /// <returns>The first page of decoded <see cref="Annotation"/> objects.</returns>
        public Task<PaginatedResult<Annotation>> GetAsync(string messageSerial, AnnotationsRequestParams query = null)
        {
            return _channel.RestChannel.Annotations.GetAsync(messageSerial, query);
        }

        /// <summary>
        /// Retrieves the annotations of the given message (RTAN3a). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the annotated message.</param>
        /// <param name="query">optional query parameters.</param>
        /// <returns>The first page of decoded <see cref="Annotation"/> objects.</returns>
        public Task<PaginatedResult<Annotation>> GetAsync(Message message, AnnotationsRequestParams query = null)
        {
            return _channel.RestChannel.Annotations.GetAsync(message, query);
        }

        /// <summary>
        /// Subscribes to every annotation received on the channel (RTAN4a, RTAN4b).
        /// This implicitly attaches the channel if it is not already attached (RTAN4d).
        /// </summary>
        /// <param name="handler">handler to be notified of the arrival of annotations.</param>
        public void Subscribe(Action<Annotation> handler)
        {
            AttachIfNeeded();
            _handlers.Add(new MessageHandlerAction<Annotation>(handler));
            WarnIfSubscribeModeNotGranted();
        }

        /// <summary>
        /// Subscribes to the annotations whose type equals <paramref name="type"/> (RTAN4c).
        /// This implicitly attaches the channel if it is not already attached (RTAN4d).
        /// </summary>
        /// <param name="type">the annotation type to deliver.</param>
        /// <param name="handler">handler to be notified of the arrival of annotations of the type.</param>
        public void Subscribe(string type, Action<Annotation> handler)
        {
            AttachIfNeeded();
            _handlers.Add(type, new MessageHandlerAction<Annotation>(handler));
            WarnIfSubscribeModeNotGranted();
        }

        /// <summary>
        /// Unsubscribes a handler which was subscribed to every annotation (RTAN5).
        /// </summary>
        /// <param name="handler">the handler to be removed.</param>
        /// <returns>true if the handler was removed, false if it was not found.</returns>
        public bool Unsubscribe(Action<Annotation> handler)
        {
            return _handlers.Remove(new MessageHandlerAction<Annotation>(handler));
        }

        /// <summary>
        /// Unsubscribes a handler which was subscribed to the given annotation type (RTAN5).
        /// </summary>
        /// <param name="type">the annotation type.</param>
        /// <param name="handler">the handler to be removed. When null, every handler of the type is removed.</param>
        /// <returns>true if a handler was removed, false if none was found.</returns>
        public bool Unsubscribe(string type, Action<Annotation> handler)
        {
            return _handlers.Remove(type, handler == null ? null : new MessageHandlerAction<Annotation>(handler));
        }

        /// <summary>
        /// Unsubscribes every handler (RTAN5).
        /// </summary>
        public void Unsubscribe()
        {
            _handlers.RemoveAll();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _handlers.Dispose();
        }

        internal void RemoveAllListeners()
        {
            _handlers.RemoveAll();
        }

        // RTAN4b, RTAN4c
        internal void OnAnnotations(Annotation[] annotations)
        {
            foreach (var annotation in annotations ?? new Annotation[0])
            {
                Notify(_handlers.GetHandlers(), annotation);
                if (annotation.Type.IsNotEmpty())
                {
                    Notify(_handlers.GetHandlers(annotation.Type), annotation);
                }
            }
        }

        // RTAN4e - called when the channel becomes attached, with the modes the server granted.
        internal void ChannelAttached()
        {
            WarnIfSubscribeModeNotGranted();
        }

        private void Notify(System.Collections.Generic.IEnumerable<MessageHandlerAction<Annotation>> handlers, Annotation annotation)
        {
            foreach (var handler in handlers)
            {
                var loopHandler = handler;
                _channel.RealtimeClient.NotifyExternalClients(() => loopHandler.SafeHandle(annotation, Logger));
            }
        }

        private void AttachIfNeeded()
        {
            if (_channel.State != ChannelState.Attached && _channel.State != ChannelState.Attaching)
            {
                _channel.Attach();
            }
        }

        // RTAN4e - the modes checked are the ones the server granted, not the ones requested.
        private void WarnIfSubscribeModeNotGranted()
        {
            if (_channel.State != ChannelState.Attached || _handlers.IsEmpty)
            {
                return;
            }

            if (!_channel.Modes.Contains(ChannelMode.AnnotationSubscribe))
            {
                Logger.Warning(
                    $"Channel '{_channel.Name}': an annotation listener was added but the AnnotationSubscribe channel mode was not granted for this channel. " +
                    "Request the AnnotationSubscribe mode in the ChannelOptions, otherwise the listener will not receive annotations.");
            }
        }

        private async Task<Result> SendAsync(string messageSerial, Annotation annotation, AnnotationAction action)
        {
            // RTAN1a: argument validation, field setting and data encoding as in RSAN1.
            var outbound = RestAnnotations.CreateOutboundAnnotation(messageSerial, annotation, action);

            // RTAN1b: a state which refuses publishing throws, as it does when publishing a message.
            // The exception is not wrapped, so the error code of the refusal is preserved.
            var tw = new TaskWrapper();
            _channel.PublishAnnotation(outbound, tw.Callback);

            var failResult = Result.Fail(new ErrorInfo("Annotation publish timeout expired. The annotation was not confirmed by the server"));
            return await tw.Task.TimeoutAfter(_channel.RealtimeClient.Options.RealtimeRequestTimeout, failResult);
        }
    }
}
