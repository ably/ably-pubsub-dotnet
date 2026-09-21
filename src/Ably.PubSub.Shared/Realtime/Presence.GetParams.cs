namespace IO.Ably.Realtime
{
    /// <summary>
    /// A class that provides access to presence operations and state for the associated Channel.
    /// </summary>
    public sealed partial class Presence
    {
        /// <summary>
        /// Class used to pass parameters when using Presence.GetAsync method.
        /// </summary>
        public class GetParams
        {
            /// <summary>
            /// Should we wait for sync to complete. If false it will return the current saved state.
            /// </summary>
            public bool WaitForSync { get; set; } = true;

            /// <summary>
            /// Indicates whether to get the Presence for a specific ClientId.
            /// </summary>
            public string ClientId { get; set; }

            /// <summary>
            /// Indicates whether to get the Presence for a specific ConnectionId.
            /// </summary>
            public string ConnectionId { get; set; }
        }
    }
}
