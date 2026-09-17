using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Realtime
{
    internal class PresenceMap
    {
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private readonly string _channelName;
        private readonly ConcurrentDictionary<string, PresenceMessage> _members;

        private ICollection<string> _beforeSyncMembers;
        private bool _isSyncInProgress;
        private bool _isSyncCompleted;

        public PresenceMap(string channelName, ILogger logger)
        {
            _logger = logger;
            _channelName = channelName;
            _members = new ConcurrentDictionary<string, PresenceMessage>();
        }

        internal virtual string GetKey(PresenceMessage presence)
        {
            return presence.MemberKey;
        }

        // Exposed internally to allow for testing.
        internal ConcurrentDictionary<string, PresenceMessage> Members => _members;

        public bool SyncInProgress
        {
            get
            {
                lock (_lock)
                {
                    return _isSyncInProgress;
                }
            }

            private set
            {
                lock (_lock)
                {
                    _isSyncInProgress = value;
                }
            }
        }

        public bool SyncCompleted
        {
            get
            {
                lock (_lock)
                {
                    return _isSyncCompleted;
                }
            }

            private set
            {
                lock (_lock)
                {
                    _isSyncCompleted = value;
                }
            }
        }

        public PresenceMessage[] Values
        {
            get
            {
                return _members.Values.Where(c => c.Action != PresenceAction.Absent)
                    .ToArray();
            }
        }

        public bool Put(PresenceMessage item)
        {
            lock (_lock)
            {
                // we've seen this member, so do not remove it at the end of sync
                _beforeSyncMembers?.Remove(GetKey(item));
            }

            try
            {
                // RTP2a, RTP2b
                if (_members.TryGetValue(GetKey(item), out var existingItem) && existingItem.IsNewerThan(item))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"PresenceMap.Put | Channel: {_channelName}, Error: {ex.Message}");
                throw;
            }

            switch (item.Action)
            {
                case PresenceAction.Enter:
                case PresenceAction.Update:
                    item = item.ShallowClone();
                    item.Action = PresenceAction.Present;
                    break;
            }

            _members[GetKey(item)] = item;

            return true;
        }

        public bool Remove(PresenceMessage item)
        {
            PresenceMessage existingItem;

            // RTP2a, RTP2b
            if (_members.TryGetValue(GetKey(item), out existingItem) && existingItem.IsNewerThan(item))
            {
                return false;
            }

            _members.TryRemove(GetKey(item), out PresenceMessage _);
            if (existingItem?.Action == PresenceAction.Absent)
            {
                return false;
            }

            return true;
        }

        public void StartSync()
        {
            if (_logger.IsDebug)
            {
                _logger.Debug($"StartSync | Channel: {_channelName}, SyncInProgress: {SyncInProgress}");
            }

            if (!SyncInProgress)
            {
                lock (_lock)
                {
                    _beforeSyncMembers = new HashSet<string>(_members.Keys); // RTP19
                    SyncInProgress = true;
                    SyncCompleted = false;
                }
            }
        }

        public PresenceMessage[] EndSync()
        {
            if (_logger.IsDebug)
            {
                _logger.Debug($"EndSync | Channel: {_channelName}, SyncInProgress: {SyncInProgress}");
            }

            List<PresenceMessage> removed = new List<PresenceMessage>();
            try
            {
                if (!SyncInProgress)
                {
                    SyncCompleted = true;
                    return removed.ToArray();
                }

                // RTP2f
                foreach (var member in _members.ToArray())
                {
                    if (member.Value.Action == PresenceAction.Absent)
                    {
                        _members.TryRemove(member.Key, out PresenceMessage _);
                    }
                }

                lock (_lock)
                {
                    // RTP19
                    if (_beforeSyncMembers != null)
                    {
                        foreach (var member in _beforeSyncMembers)
                        {
                            if (_members.TryRemove(member, out PresenceMessage pm))
                            {
                                removed.Add(pm);
                            }
                        }

                        _beforeSyncMembers = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"PresenceMap.EndSync | Channel: {_channelName}, Error: {ex.Message}");
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    SyncCompleted = true;
                    SyncInProgress = false;
                }
            }

            return removed.ToArray();
        }

        public void Clear()
        {
            lock (_lock)
            {
                _members?.Clear();
                _beforeSyncMembers?.Clear();
            }
        }

        internal JObject GetState()
        {
            var matchingMembers = _members.Select(x => JObject.FromObject(new { Name = x.Key, Data = x.Value }));

            var state = new JObject
            {
                ["channelName"] = _channelName,
                ["syncInProgress"] = SyncInProgress,
                ["syncCompleted"] = SyncCompleted,
                ["members"] = new JArray(matchingMembers),
            };

            return state;
        }
    }

    // RTP17
    internal class InternalPresenceMap : PresenceMap
    {
        public InternalPresenceMap(string channelName, ILogger logger)
            : base(channelName, logger)
        {
        }

        // RTP17h
        internal override string GetKey(PresenceMessage presence)
        {
            return presence.ClientId;
        }
    }
}
