namespace IO.Ably.Push
{
    /// <summary>
    /// Push Apis for Rest clients.
    /// </summary>
    public class PushRest
    {
        internal PushRest(AblyRest rest, ILogger logger)
        {
            Admin = new PushAdmin(rest, logger);
        }

        /// <summary>
        /// Admin APIs for Push notifications.
        /// </summary>
        public PushAdmin Admin { get; }
    }
}
