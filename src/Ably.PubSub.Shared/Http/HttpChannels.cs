using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ably.PubSub.Push;

namespace Ably.PubSub.Http
{
    /// <summary>
    /// Class that manages HttpChannels.
    /// </summary>
    public class HttpChannels : IChannels<IHttpChannel>
    {
        private readonly ConcurrentDictionary<string, HttpChannel> _channels =
            new ConcurrentDictionary<string, HttpChannel>();

        private readonly LockedList<IHttpChannel> _orderedChannels = new LockedList<IHttpChannel>();

        private readonly PubSubHttpClient _ablyRest;
        private readonly IMobileDevice _mobileDevice;

        internal HttpChannels(PubSubHttpClient restClient, IMobileDevice mobileDevice = null)
        {
            _ablyRest = restClient;
            _mobileDevice = mobileDevice;
        }

        /// <inheritdoc/>
        public IHttpChannel Get(string name)
        {
            return Get(name, null);
        }

        /// <inheritdoc/>
        public IHttpChannel Get(string name, ChannelOptions options)
        {
            if (!_channels.TryGetValue(name, out var result))
            {
                var channel = new HttpChannel(_ablyRest, name, options, _mobileDevice);
                result = _channels.AddOrUpdate(name, channel, (s, realtimeChannel) =>
                {
                    if (options != null && realtimeChannel != null)
                    {
                        realtimeChannel.Options = options;
                    }

                    return realtimeChannel;
                });
                _orderedChannels.Add(result);
            }
            else
            {
                if (options != null)
                {
                    result.Options = options;
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public IHttpChannel this[string name] => Get(name);

        /// <inheritdoc/>
        public bool Release(string name)
        {
            var result = _channels.TryRemove(name, out var channel);
            _orderedChannels.Remove(channel);
            return result;
        }

        /// <inheritdoc/>
        public void ReleaseAll()
        {
            var channelList = _channels.Keys.ToArray();
            foreach (var channelName in channelList)
            {
                _ = Release(channelName);
            }
        }

        /// <inheritdoc/>
        public bool Exists(string name)
        {
            return _channels.ContainsKey(name);
        }

        /// <inheritdoc/>
        IEnumerator<IHttpChannel> IEnumerable<IHttpChannel>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through the channels collection.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the channels collection.</returns>
        protected virtual IEnumerator<IHttpChannel> GetEnumerator()
        {
            lock (_orderedChannels)
            {
                return _orderedChannels.ToList().GetEnumerator();
            }
        }
    }
}
