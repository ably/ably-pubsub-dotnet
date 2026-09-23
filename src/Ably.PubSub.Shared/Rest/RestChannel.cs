using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IO.Ably.Encryption;
using IO.Ably.Push;

namespace IO.Ably.Rest
{
#pragma warning disable SA1600

    /// <summary>
    /// The Ably Realtime service organises the traffic within any application into named channels.
    /// Channels are the "unit" of message distribution; clients attach to channels to subscribe to messages,
    /// and every message broadcast by the service is associated with a unique channel.
    /// A channel cannot be instantiated but needs to be created using the AblyRest.Channels.Get("channelname").
    /// </summary>
    public class RestChannel : IRestChannel, IPresence
    {
        private readonly AblyRest _ablyRest;
        private readonly string _basePath;
        private ChannelOptions _options;
        private readonly PushChannel _pushChannel;

        internal RestChannel(AblyRest ablyRest, string name, ChannelOptions options, IMobileDevice mobileDevice = null)
        {
            Name = name;
            _ablyRest = ablyRest;
            _options = options;
            _basePath = $"/channels/{name.EncodeUriPart()}";

            if (mobileDevice != null)
            {
                _pushChannel = new PushChannel(name, ablyRest);
            }
        }

        /// <inheritdoc/>
        public string Name { get; private set; }

        /// <summary>
        /// Channel options of this channel.
        /// </summary>
        public ChannelOptions Options
        {
            get => _options;
            set => _options = value ?? new ChannelOptions();
        }

        /// <inheritdoc />
        public PushChannel Push
        {
            get
            {
                if (_pushChannel is null)
                {
                    _ablyRest.Logger.Debug("The current device is not configured for push notifications.");
                    return null;
                }

                return _pushChannel;
            }
        }

        /// <inheritdoc/>
        public IPresence Presence => this;

        /// <inheritdoc/>
        public Task PublishAsync(string name, object data, string clientId = null)
        {
            return PublishAsync(new Message(name, data, clientId));
        }

        /// <inheritdoc/>
        public Task PublishAsync(Message message)
        {
            return PublishAsync(new[] { message });
        }

        /// <inheritdoc/>
        public Task PublishAsync(IEnumerable<Message> messages)
        {
            var result = _ablyRest.AblyAuth.ValidateClientIds(messages);
            if (result.IsFailure)
            {
                throw new AblyException(result.Error);
            }

            var request = _ablyRest.CreatePostRequest(_basePath + "/messages", Options);

            // if idempotentRestPublishing is enabled
            if (_ablyRest.Options.IdempotentRestPublishing)
            {
                // and all Messages have an empty id attribute
                if (messages.All(m => m.Id == null))
                {
                    // generate a base id string by base64-encoding a sequence of at least 9 bytes
                    var baseId = Crypto.GetRandomMessageId();
                    int serial = 0;
                    foreach (var message in messages)
                    {
                        // each message gets a unique id of the form <base id>:<serial>
                        message.Id = $"{baseId}:{serial}";
                        serial++;
                    }
                }
            }

            request.PostData = messages;
            return _ablyRest.ExecuteRequest(request);
        }

        /// <inheritdoc/>
        Task<PaginatedResult<PresenceMessage>> IPresence.GetAsync(int? limit, string clientId, string connectionId)
        {
            if (limit.HasValue && (limit < 0 || limit > 1000))
            {
                throw new ArgumentException("Limit must be between 0 and 1000", nameof(limit));
            }

            var presenceLimit = limit ?? Defaults.QueryLimit;

            var request = _ablyRest.CreateGetRequest(_basePath + "/presence", Options);

            request.QueryParameters.Add("limit", presenceLimit.ToString());
            if (clientId.IsNotEmpty())
            {
                request.QueryParameters.Add("clientId", clientId);
            }

            if (connectionId.IsNotEmpty())
            {
                request.QueryParameters.Add("connectionId", connectionId);
            }

            return _ablyRest.ExecutePaginatedRequest(request, Presence.GetAsync);
        }

        /// <inheritdoc/>
        Task<PaginatedResult<PresenceMessage>> IPresence.GetAsync(PaginatedRequestParams query) => GetAsync(query);

        /// <summary>
        /// Obtain the set of members currently present for a channel.
        /// </summary>
        /// <param name="query"><see cref="PaginatedRequestParams"/> query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of Presence messages.</returns>
        protected virtual Task<PaginatedResult<PresenceMessage>> GetAsync(PaginatedRequestParams query)
        {
            if (query == null)
            {
                // Fall back on the default implementation
                return Presence.GetAsync();
            }

            query.Validate();

            var request = _ablyRest.CreateGetRequest(_basePath + "/presence", Options);
            request.AddQueryParameters(query.GetParameters());
            return _ablyRest.ExecutePaginatedRequest(request, Presence.GetAsync);
        }

        /// <inheritdoc/>
        Task<PaginatedResult<PresenceMessage>> IPresence.HistoryAsync()
        {
            return Presence.HistoryAsync(new PaginatedRequestParams());
        }

        /// <inheritdoc/>
        Task<PaginatedResult<PresenceMessage>> IPresence.HistoryAsync(PaginatedRequestParams query)
        {
            query = query ?? new PaginatedRequestParams();

            query.Validate();

            var request = _ablyRest.CreateGetRequest(_basePath + "/presence/history", Options);
            request.AddQueryParameters(query.GetParameters());
            return _ablyRest.ExecutePaginatedRequest(request, Presence.HistoryAsync);
        }

        /// <inheritdoc/>
        public Task<PaginatedResult<Message>> HistoryAsync()
        {
            return HistoryAsync(new PaginatedRequestParams());
        }

        /// <inheritdoc/>
        public Task<PaginatedResult<Message>> HistoryAsync(PaginatedRequestParams query)
        {
            query = query ?? new PaginatedRequestParams();

            query.Validate();

            var request = _ablyRest.CreateGetRequest(_basePath + "/messages", Options);

            request.AddQueryParameters(query.GetParameters());

            return _ablyRest.ExecutePaginatedRequest(request, HistoryAsync);
        }

        /// <inheritdoc/>
        public void Publish(string name, object data, string clientId = null)
        {
            AsyncHelper.RunSync(() => PublishAsync(name, data, clientId));
        }

        /// <inheritdoc/>
        public void Publish(Message message)
        {
            AsyncHelper.RunSync(() => PublishAsync(message));
        }

        /// <inheritdoc/>
        public void Publish(IEnumerable<Message> messages)
        {
            AsyncHelper.RunSync(() => PublishAsync(messages));
        }

        /// <inheritdoc/>
        public PaginatedResult<Message> History()
        {
            return AsyncHelper.RunSync(HistoryAsync);
        }

        /// <inheritdoc/>
        public PaginatedResult<Message> History(PaginatedRequestParams query)
        {
            return AsyncHelper.RunSync(() => HistoryAsync(query));
        }

        /// <inheritdoc/>
        public async Task<ChannelDetails> StatusAsync()
        {
            AblyRequest request = _ablyRest.CreateGetRequest("/channels/" + Name);
            return await _ablyRest.ExecuteRequest<ChannelDetails>(request);
        }

        /// <inheritdoc/>
        public ChannelDetails Status()
        {
            return AsyncHelper.RunSync(StatusAsync);
        }
    }
}
