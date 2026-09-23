using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using IO.Ably.Push;

namespace IO.Ably.Rest
{
    /// <summary>
    /// Class that manages RestChannels.
    /// </summary>
    public class RestChannels : IChannels<IRestChannel>
    {
        private readonly ConcurrentDictionary<string, RestChannel> _channels =
            new ConcurrentDictionary<string, RestChannel>();

        private readonly LockedList<IRestChannel> _orderedChannels = new LockedList<IRestChannel>();

        private readonly AblyRest _ablyRest;
        private readonly IMobileDevice _mobileDevice;

        internal RestChannels(AblyRest restClient, IMobileDevice mobileDevice = null)
        {
            _ablyRest = restClient;
            _mobileDevice = mobileDevice;
        }

        /// <inheritdoc/>
        public IRestChannel Get(string name)
        {
            return Get(name, null);
        }

        /// <inheritdoc/>
        public IRestChannel Get(string name, ChannelOptions options)
        {
            if (!_channels.TryGetValue(name, out var result))
            {
                var channel = new RestChannel(_ablyRest, name, options, _mobileDevice);
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
        public IRestChannel this[string name] => Get(name);

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
        IEnumerator<IRestChannel> IEnumerable<IRestChannel>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through the channels collection.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the channels collection.</returns>
        protected virtual IEnumerator<IRestChannel> GetEnumerator()
        {
            lock (_orderedChannels)
            {
                return _orderedChannels.ToList().GetEnumerator();
            }
        }
    }
}
