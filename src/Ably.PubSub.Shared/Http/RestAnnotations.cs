using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;

namespace Ably.PubSub.Http
{
    /// <summary>
    /// Publishes, deletes and retrieves the annotations of messages on a channel over REST (RSL10).
    /// Accessed through <see cref="IHttpChannel.Annotations"/>.
    /// Experimental: the annotations API may change.
    /// </summary>
    public class RestAnnotations
    {
        private readonly PubSubHttpClient _ablyRest;
        private readonly HttpChannel _channel;
        private readonly string _basePath;

        internal RestAnnotations(PubSubHttpClient ablyRest, HttpChannel channel, string basePath)
        {
            _ablyRest = ablyRest;
            _channel = channel;
            _basePath = basePath;
        }

        /// <summary>
        /// Publishes an annotation to the message with the given serial (RSAN1).
        /// The action of the annotation is set to <see cref="AnnotationAction.Create"/> (RSAN1c1).
        /// </summary>
        /// <param name="messageSerial">the serial of the message to annotate (RSAN1a).</param>
        /// <param name="annotation">the annotation to publish. Only the type, clientId, name, count, data and extras
        /// fields, and the id if you supply one, are used (RSAN1a2). The type is required (RSAN1a3).</param>
        /// <returns>Task.</returns>
        public Task PublishAsync(string messageSerial, Annotation annotation)
        {
            return SendAsync(messageSerial, annotation, AnnotationAction.Create);
        }

        /// <summary>
        /// Publishes an annotation to the given message (RSAN1a1). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the message to annotate.</param>
        /// <param name="annotation">the annotation to publish. See <see cref="PublishAsync(string, Annotation)"/>.</param>
        /// <returns>Task.</returns>
        public Task PublishAsync(Message message, Annotation annotation)
        {
            return SendAsync(message?.Serial, annotation, AnnotationAction.Create);
        }

        /// <summary>
        /// Deletes an annotation from the message with the given serial (RSAN2).
        /// Identical to <see cref="PublishAsync(string, Annotation)"/> except that the action of the annotation
        /// is set to <see cref="AnnotationAction.Delete"/> (RSAN2a).
        /// </summary>
        /// <param name="messageSerial">the serial of the annotated message.</param>
        /// <param name="annotation">the annotation to delete.</param>
        /// <returns>Task.</returns>
        public Task DeleteAsync(string messageSerial, Annotation annotation)
        {
            return SendAsync(messageSerial, annotation, AnnotationAction.Delete);
        }

        /// <summary>
        /// Deletes an annotation from the given message (RSAN1a1, RSAN2a). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the annotated message.</param>
        /// <param name="annotation">the annotation to delete.</param>
        /// <returns>Task.</returns>
        public Task DeleteAsync(Message message, Annotation annotation)
        {
            return SendAsync(message?.Serial, annotation, AnnotationAction.Delete);
        }

        /// <summary>
        /// Retrieves the annotations of the message with the given serial (RSAN3).
        /// </summary>
        /// <param name="messageSerial">the serial of the annotated message (RSAN3a).</param>
        /// <param name="query">optional query parameters (RSAN3a).</param>
        /// <returns>The first page of decoded <see cref="Annotation"/> objects (RSAN3c).</returns>
        public Task<PaginatedResult<Annotation>> GetAsync(string messageSerial, AnnotationsRequestParams query = null)
        {
            query = query ?? new AnnotationsRequestParams();
            query.Validate();
            return GetPageAsync(messageSerial, GetFirstPageParameters(query));
        }

        /// <summary>
        /// Retrieves the annotations of the given message (RSAN3a). The message must have a populated serial.
        /// </summary>
        /// <param name="message">the annotated message.</param>
        /// <param name="query">optional query parameters.</param>
        /// <returns>The first page of decoded <see cref="Annotation"/> objects (RSAN3c).</returns>
        public Task<PaginatedResult<Annotation>> GetAsync(Message message, AnnotationsRequestParams query = null)
        {
            return GetAsync(message?.Serial, query);
        }

        /// <summary>
        /// Validates the serial and annotation and builds the annotation which is sent to Ably.
        /// Shared by the REST and the realtime paths so that RSAN1a1, RSAN1a2, RSAN1a3, RSAN1c1 and RSAN1c2
        /// are applied identically (RTAN1a).
        /// </summary>
        internal static Annotation CreateOutboundAnnotation(string messageSerial, Annotation annotation, AnnotationAction action)
        {
            if (messageSerial.IsEmpty())
            {
                throw new AblyException(new ErrorInfo(
                    "A message serial is required to annotate a message",
                    ErrorCodes.InvalidParameterValue,
                    HttpStatusCode.BadRequest));
            }

            if (annotation == null)
            {
                throw new ArgumentNullException(nameof(annotation));
            }

            if (annotation.Type.IsEmpty())
            {
                throw new AblyException(new ErrorInfo(
                    "An annotation type is required",
                    ErrorCodes.InvalidParameterValue,
                    HttpStatusCode.BadRequest));
            }

            // RSAN1a2: only the fields a publisher may supply are carried over. A copy is sent so that
            // encoding the data does not alter the object the caller holds.
            return new Annotation
            {
                Id = annotation.Id,
                Type = annotation.Type,
                ClientId = annotation.ClientId,
                Name = annotation.Name,
                Count = annotation.Count,
                Data = annotation.Data,
                Extras = annotation.Extras,
                Action = action, // RSAN1c1, RSAN2a
                MessageSerial = messageSerial, // RSAN1c2
            };
        }

        private Task SendAsync(string messageSerial, Annotation annotation, AnnotationAction action)
        {
            var outbound = CreateOutboundAnnotation(messageSerial, annotation, action);

            var result = _ablyRest.AblyAuth.ValidateClientIds(new[] { outbound });
            if (result.IsFailure)
            {
                throw new AblyException(result.Error);
            }

            // RSAN1c4
            if (_ablyRest.Options.IdempotentRestPublishing && outbound.Id.IsEmpty())
            {
                outbound.Id = $"{Crypto.GetRandomMessageId()}:0";
            }

            // RSAN1c6
            var request = _ablyRest.CreatePostRequest(AnnotationsPath(messageSerial), _channel.Options);

            // RSAN1c: a body of one annotation, as an array. RSAN1c3 and RSAN1c5 are applied when the body is built.
            request.PostData = new[] { outbound };
            return _ablyRest.ExecuteRequest(request);
        }

        private Task<PaginatedResult<Annotation>> GetPageAsync(string messageSerial, IEnumerable<KeyValuePair<string, string>> queryParameters)
        {
            if (messageSerial.IsEmpty())
            {
                throw new AblyException(new ErrorInfo(
                    "A message serial is required to retrieve the annotations of a message",
                    ErrorCodes.InvalidParameterValue,
                    HttpStatusCode.BadRequest));
            }

            // RSAN3b
            var request = _ablyRest.CreateGetRequest(AnnotationsPath(messageSerial), _channel.Options);
            request.AddQueryParameters(queryParameters);

            return _ablyRest.ExecutePaginatedRequest(request, next => GetPageAsync(messageSerial, GetNextPageParameters(next)));
        }

        // Annotation queries take a limit only (RSAN3a), so the default of 100 is stated explicitly.
        private static IEnumerable<KeyValuePair<string, string>> GetFirstPageParameters(AnnotationsRequestParams query)
        {
            yield return new KeyValuePair<string, string>("limit", (query.Limit ?? Defaults.QueryLimit).ToString());
        }

        // The query returned by Ably for the next page is replayed as it was received, without the history defaults.
        private static IEnumerable<KeyValuePair<string, string>> GetNextPageParameters(PaginatedRequestParams next)
        {
            if (next.QueryString.IsEmpty())
            {
                return new[] { new KeyValuePair<string, string>("limit", (next.Limit ?? Defaults.QueryLimit).ToString()) };
            }

            var parsed = HttpUtility.ParseQueryString(next.QueryString);
            return parsed.AllKeys.Select(key => new KeyValuePair<string, string>(key, parsed[key])).ToList();
        }

        private string AnnotationsPath(string messageSerial) => $"{_basePath}/messages/{messageSerial.EncodeUriPart()}/annotations";
    }
}
