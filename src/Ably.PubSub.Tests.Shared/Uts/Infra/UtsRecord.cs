using Ably.PubSub.Realtime;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// Record-and-verify for transient states (<c>writing-test-specs.md</c> "Verifying Transient States"):
    /// subscribe before the stimulus, let the cycle finish, then assert on the record with <see
    /// cref="UtsAssert.ContainsInOrder{T}"/>.
    /// </summary>
    public static class UtsRecord
    {
        /// <summary>Every connection state entered from now on (<c>change.current</c>).</summary>
        public static CaptureList<ConnectionState> ConnectionStates(Connection connection)
        {
            var states = new CaptureList<ConnectionState>();
            connection.On(change => states.Add(change.Current));
            return states;
        }

        /// <summary>Every connection state change from now on, with its reason and retry delay.</summary>
        public static CaptureList<ConnectionStateChange> ConnectionStateChanges(Connection connection)
        {
            var changes = new CaptureList<ConnectionStateChange>();
            connection.On(change => changes.Add(change));
            return changes;
        }

        /// <summary>Every channel state entered from now on.</summary>
        public static CaptureList<ChannelState> ChannelStates(IRealtimeChannel channel)
        {
            var states = new CaptureList<ChannelState>();
            channel.On(change => states.Add(change.Current));
            return states;
        }

        /// <summary>Every channel state change from now on, with its error and resumed flag.</summary>
        public static CaptureList<ChannelStateChange> ChannelStateChanges(IRealtimeChannel channel)
        {
            var changes = new CaptureList<ChannelStateChange>();
            channel.On(change => changes.Add(change));
            return changes;
        }
    }
}
