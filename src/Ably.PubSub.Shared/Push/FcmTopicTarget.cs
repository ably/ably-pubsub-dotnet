namespace Ably.PubSub.Push
{
    /// <summary>
    /// The devices an FCM topic message is sent to: either the subscribers of one topic, or the devices
    /// matched by a condition combining several topics. Create one with <see cref="Topic"/> or <see cref="Condition"/>.
    /// </summary>
    public sealed class FcmTopicTarget
    {
        private FcmTopicTarget(string field, string value)
        {
            Field = field;
            Value = value;
        }

        // The FCM message field the target is sent as, "topic" or "condition", and its value.
        internal string Field { get; }

        internal string Value { get; }

        /// <summary>
        /// Targets every device subscribed to a topic.
        /// </summary>
        /// <param name="name">The FCM topic name, matching <c>[a-zA-Z0-9-_.~%]{1,900}</c>.</param>
        /// <returns>A topic target.</returns>
        /// <exception cref="AblyException">The topic name is empty or does not match the allowed pattern.</exception>
        public static FcmTopicTarget Topic(string name)
        {
            PushAdminFcmTopics.RequireValidTopic(name);
            return new FcmTopicTarget("topic", name);
        }

        /// <summary>
        /// Targets the devices matched by a topic condition.
        /// <para>
        /// A condition combines up to five topics with <c>&amp;&amp;</c>, <c>||</c> and parentheses, for example
        /// <c>'team-manutd' in topics || 'team-liverpool' in topics</c>. FCM delivers the message once to
        /// each matching device, however many of the topics it is subscribed to. The condition is checked
        /// by FCM, which rejects an invalid one.
        /// </para>
        /// </summary>
        /// <param name="condition">The FCM topic condition.</param>
        /// <returns>A condition target.</returns>
        /// <exception cref="AblyException">The condition is empty.</exception>
        public static FcmTopicTarget Condition(string condition)
        {
            if (condition.IsEmpty())
            {
                throw new AblyException("Please provide a non-empty condition.", ErrorCodes.BadRequest);
            }

            return new FcmTopicTarget("condition", condition);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Field}: {Value}";
    }
}
