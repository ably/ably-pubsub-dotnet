namespace Ably.PubSub
{
    /// <summary>
    /// The action of an <see cref="Annotation"/> (TAN2b). Experimental: the annotations API may change.
    /// </summary>
    public enum AnnotationAction
    {
        /// <summary>An annotation was created (TAN2b, ANNOTATION_CREATE).</summary>
        Create = 0,

        /// <summary>An annotation was deleted (TAN2b, ANNOTATION_DELETE).</summary>
        Delete = 1,
    }
}
