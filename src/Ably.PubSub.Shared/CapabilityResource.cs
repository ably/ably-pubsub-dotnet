using System.Collections.Generic;

namespace IO.Ably
{
    /// <summary>
    /// Describes a CapabilityResource.
    /// </summary>
    public class CapabilityResource
    {
        /// <summary>
        /// Initializes a new instance and assigns a name.
        /// </summary>
        /// <param name="name">name of the resource.</param>
        public CapabilityResource(string name)
            : this()
        {
            Name = name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CapabilityResource"/> class.
        /// </summary>
        private CapabilityResource()
        {
            AllowedOperations = new List<string>();
        }

        /// <summary>
        /// Name of the resource.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// List of allowed operations.
        /// </summary>
        public List<string> AllowedOperations { get; }

        /// <summary>
        /// Does the current resource allow all operations.
        /// </summary>
        public bool AllowsAll => AllowedOperations.Contains(AllowedOps.All);

        /// <summary>
        /// Adds the All(*) operation to the list of allowed operations.
        /// Unlike the other Capability helpers it doesn't return this
        /// because there is no need to add any more operations once everything is allowed.
        /// </summary>
        public void AllowAll()
        {
            AllowedOperations.Add(AllowedOps.All);
            AllowedOperations.Sort();
        }

        /// <summary>
        /// Allows the Presence operation.
        /// </summary>
        /// <returns>the current instance so more Allow methods can be chained.</returns>
        public CapabilityResource AllowPresence()
        {
            AllowedOperations.Add(AllowedOps.Presence);
            AllowedOperations.Sort();
            return this;
        }

        /// <summary>
        /// Allows the Subscribe operation.
        /// </summary>
        /// <returns>the current instance so more Allow methods can be chained.</returns>
        public CapabilityResource AllowSubscribe()
        {
            AllowedOperations.Add(AllowedOps.Subscribe);
            AllowedOperations.Sort();
            return this;
        }

        /// <summary>
        /// Allows the Publish operation.
        /// </summary>
        /// <returns>the current instance so more Allow methods can be chained.</returns>
        public CapabilityResource AllowPublish()
        {
            AllowedOperations.Add(AllowedOps.Publish);
            AllowedOperations.Sort();
            return this;
        }

        /// <summary>
        /// Describes all the allowed operations.
        /// </summary>
        public static class AllowedOps
        {
            /// <summary>
            /// Publish operation.
            /// </summary>
            public const string Publish = "publish";

            /// <summary>
            /// Subscribe operation.
            /// </summary>
            public const string Subscribe = "subscribe";

            /// <summary>
            /// Presence operation.
            /// </summary>
            public const string Presence = "presence";

            /// <summary>
            /// All operations.
            /// </summary>
            public const string All = "*";
        }
    }
}
