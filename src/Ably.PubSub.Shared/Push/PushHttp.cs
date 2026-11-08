namespace Ably.PubSub.Push
{
    /// <summary>
    /// Push APIs for HTTP clients.
    /// </summary>
    public class PushHttp
    {
        internal PushHttp(PubSubHttpClient rest, ILogger logger)
        {
            Admin = new PushAdmin(rest, logger);
        }

        /// <summary>
        /// Admin APIs for Push notifications.
        /// </summary>
        public PushAdmin Admin { get; }
    }
}
