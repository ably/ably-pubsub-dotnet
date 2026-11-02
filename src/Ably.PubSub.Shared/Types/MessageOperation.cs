using System.Collections.Generic;

namespace Ably.PubSub
{
    /// <summary>
    /// Describes an update, delete or append operation on a message (MOP1). It is supplied to the update, delete and
    /// append message functions and is sent to Ably as the <see cref="Message.Version"/> of the edited message
    /// (RSL15b7, RTL32b2); it is never sent as an object of its own.
    /// </summary>
    public class MessageOperation
    {
        /// <summary>The id of the client performing the operation (MOP2a).</summary>
        public string ClientId { get; set; }

        /// <summary>A description of the operation, for example the reason for the edit (MOP2b).</summary>
        public string Description { get; set; }

        /// <summary>Arbitrary string key-value metadata describing the operation (MOP2c).</summary>
        public IDictionary<string, string> Metadata { get; set; }
    }
}
