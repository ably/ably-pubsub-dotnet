using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IO.Ably.Push;
using IO.Ably.Realtime.Workflow;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// Manages Realtime channels.
    /// </summary>
    public class RealtimeChannels : IChannels<IRealtimeChannel>
    {
        internal ILogger Logger { get; }

        private ConcurrentDictionary<string, RealtimeChannel> Channels { get; } = new ConcurrentDictionary<string, RealtimeChannel>();

        private readonly LockedList<IRealtimeChannel> _orderedChannels = new LockedList<IRealtimeChannel>();

        private readonly AblyRealtime _realtimeClient;
        private readonly IMobileDevice _mobileDevice;

        internal RealtimeChannels(AblyRealtime realtimeClient, Connection connection, IMobileDevice mobileDevice = null)
        {
            _realtimeClient = realtimeClient;
            Logger = realtimeClient.Logger;
            connection.InternalStateChanged += ConnectionStateChange;
            _mobileDevice = mobileDevice;
        }

        private void ConnectionStateChange(object sender, ConnectionStateChange stateChange)
        {
            foreach (var channel in Channels.Values)
            {
                try
                {
                    channel.ConnectionStateChanged(stateChange);
                }
                catch (Exception e)
                {
                    // TODO: Send to Sentry
                    Logger.Error($"Error notifying channel '{channel.Name}' of connection stage change", e);
                }
            }
        }

        /// <inheritdoc/>
        public IRealtimeChannel Get(string name)
        {
            return Get(name, null);
        }

        /// <inheritdoc/>
        public IRealtimeChannel Get(string name, ChannelOptions options)
        {
            // if the channel cannot be found
            if (!Channels.TryGetValue(name, out var result))
            {
                // create a new instance using the passed in option
                var channel = new RealtimeChannel(name, _realtimeClient.Options.GetClientId(), _realtimeClient, options, _mobileDevice);
                result = Channels.AddOrUpdate(name, channel, (s, realtimeChannel) =>
                {
                    if (options != null)
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
                    if (result.ShouldReAttach(options))
                    {
                        throw new AblyException(new ErrorInfo("Channels.Get() cannot be used to set channel options that would cause the channel to reattach. Please, use Channel.SetOptions() instead.", ErrorCodes.BadRequest, HttpStatusCode.BadRequest));
                    }

                    result.SetOptions(options);
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public IRealtimeChannel this[string name] => Get(name);

        /// <inheritdoc/>
        public bool Release(string name)
        {
            bool IsChannelStateOkForImmediateRelease(RealtimeChannel realtimeChannel)
            {
                var state = realtimeChannel.State;

                return state == ChannelState.Initialized || state == ChannelState.Detached ||
                       state == ChannelState.Failed;
            }

            bool RemoveChannel()
            {
                if (Channels.TryRemove(name, out RealtimeChannel removedChannel))
                {
                    removedChannel.RemoveAllListeners();
                    _orderedChannels.Remove(removedChannel);
                    return true;
                }

                return false;
            }

            if (Logger.IsDebug)
            {
                Logger.Debug($"Releasing channel #{name}");
            }

            if (!Channels.TryGetValue(name, out RealtimeChannel channel))
            {
                return false;
            }

            void DetachedCallback(bool detached, ErrorInfo error)
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug(
                        error is null
                            ? $"Channel #{name} was removed from Channel list. Detached successfully: {detached}."
                            : $"Failed to cleanly detach channel #{name} before removing it from Channel list. Detach error: {error}.");
                }

                RemoveChannel();
            }

            if (IsChannelStateOkForImmediateRelease(channel))
            {
                return RemoveChannel();
            }

            channel.Detach(DetachedCallback);

            return true;
        }

        /// <inheritdoc/>
        public void ReleaseAll()
        {
            var channelList = Channels.Keys.ToArray();
            foreach (var channelName in channelList)
            {
                Release(channelName);
            }
        }

        internal void CleanupChannels()
        {
            try
            {
                var channels = Channels.Keys.ToList();
                foreach (var channelName in channels)
                {
                    var success = Channels.TryRemove(channelName, out RealtimeChannel channel);
                    if (success)
                    {
                        channel.RemoveAllListeners();
                        _orderedChannels.Remove(channel);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error while disposing channels", e);
            }
        }

        /// <inheritdoc/>
        public bool Exists(string name)
        {
            return Channels.ContainsKey(name);
        }

        /// <inheritdoc/>
        IEnumerator<IRealtimeChannel> IEnumerable<IRealtimeChannel>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Returns an enumerator that iterates through the channels collection.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the channels collection.</returns>
        protected virtual IEnumerator<IRealtimeChannel> GetEnumerator()
        {
            lock (_orderedChannels)
            {
                return _orderedChannels.ToList().GetEnumerator();
            }
        }

        internal JArray GetCurrentState()
        {
            return new JArray(Channels.Values.Select(x => x.GetCurrentState()));
        }

        internal Task ExecuteCommand(ChannelCommand cmd)
        {
            var channelName = cmd.ChannelName;
            var affectedChannels = Channels.Values
                                        .ToArray()
                                        .Where(x => cmd.ChannelName.IsEmpty() || x.Name.EqualsTo(channelName))
                                        .ToList();

            foreach (var channel in affectedChannels)
            {
                switch (cmd.Command)
                {
                    case InitialiseFailedChannelsOnConnect _:
                        HandleInitialiseFailedChannelsCommand(channel);
                        break;
                    default:
                        Logger.Debug($"Channels can't handle command: '{cmd.Name}'");
                        break;
                }
            }

            return Task.CompletedTask;
        }

        private void HandleInitialiseFailedChannelsCommand(RealtimeChannel channel)
        {
            switch (_realtimeClient.Connection.State)
            {
                case ConnectionState.Closed:
                case ConnectionState.Failed:
                    /* (RTN11d)
                     * If the [Connection] state is FAILED,
                     * transitions all the channels to INITIALIZED */
                    channel.SetChannelState(ChannelState.Initialized);
                    break;
            }
        }

        // RTN16j, RTL15b
        internal void SetChannelSerialsFromRecoverOption(IDictionary<string, string> serials)
        {
            foreach (var keyValuePair in serials)
            {
                var channelName = keyValuePair.Key;
                var channelSerial = keyValuePair.Value;
                var channel = (RealtimeChannel)Get(channelName);
                channel.Properties.ChannelSerial = channelSerial;
            }
        }

        internal IDictionary<string, string> GetChannelSerials()
        {
            var channelSerials = new Dictionary<string, string>();
            foreach (var realtimeChannel in this)
            {
                if (realtimeChannel.State == ChannelState.Attached)
                {
                    channelSerials[realtimeChannel.Name] = realtimeChannel.Properties.ChannelSerial;
                }
            }

            return channelSerials;
        }
    }
}
