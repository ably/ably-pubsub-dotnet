using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace IO.Ably
{
    /// <summary>
    /// Channel params is a "Dictionary&lt;string, string&gt;" used for passing extra parameters when
    /// attaching to an Ably Realtime channel.
    /// </summary>
    public class ChannelParams : Dictionary<string, string>
    {
    }

    /// <summary>
    /// Read only version of <see cref="ChannelParams"/>.
    /// </summary>
    public class ReadOnlyChannelParams : ReadOnlyDictionary<string, string>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyChannelParams"/> class.
        /// </summary>
        /// <param name="dictionary">original dictionary used to initialize ReadOnlyChannelParams.</param>
        public ReadOnlyChannelParams(IDictionary<string, string> dictionary)
            : base(dictionary)
        {
        }
    }
}
