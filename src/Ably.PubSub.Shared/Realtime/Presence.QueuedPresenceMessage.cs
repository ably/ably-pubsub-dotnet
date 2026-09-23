using System;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// A class that provides access to presence operations and state for the associated Channel.
    /// </summary>
    public sealed partial class Presence
    {
        internal class QueuedPresenceMessage
        {
            public QueuedPresenceMessage(PresenceMessage message, Action<bool, ErrorInfo> callback)
            {
                Message = message;
                Callback = callback;
            }

            public PresenceMessage Message { get; }

            public Action<bool, ErrorInfo> Callback { get; }
        }
    }
}
