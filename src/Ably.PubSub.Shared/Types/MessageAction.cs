namespace Ably.PubSub
{
    /// <summary>
    /// The action of a <see cref="Message"/> (TM5). Describes what a message represents:
    /// the creation of a message, a later update, a deletion, metadata, a summary of annotations, or an append.
    /// </summary>
    public enum MessageAction
    {
        /// <summary>A newly created message (TM5, MESSAGE_CREATE).</summary>
        MessageCreate = 0,

        /// <summary>An update to a previously published message (TM5, MESSAGE_UPDATE).</summary>
        MessageUpdate = 1,

        /// <summary>A deletion of a previously published message (TM5, MESSAGE_DELETE).</summary>
        MessageDelete = 2,

        /// <summary>A meta message that carries no user payload (TM5, META).</summary>
        Meta = 3,

        /// <summary>A message whose annotations summary has changed (TM5, MESSAGE_SUMMARY).</summary>
        MessageSummary = 4,

        /// <summary>An append to the data of a previously published message (TM5, MESSAGE_APPEND).</summary>
        MessageAppend = 5,
    }
}
