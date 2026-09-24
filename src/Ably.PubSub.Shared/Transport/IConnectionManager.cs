using System;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;

namespace Ably.PubSub.Transport
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "No need to document internal interfaces.")]
    internal interface IConnectionManager
    {
        Connection Connection { get; }

        ClientOptions Options { get; }

        void Send(ProtocolMessage message, Action<bool, ErrorInfo> callback = null, ChannelOptions channelOptions = null);
    }
}
