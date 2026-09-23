using System;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;

namespace IO.Ably.Transport
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "No need to document internal interfaces.")]
    internal interface IConnectionContext
    {
        TimeSpan DefaultTimeout { get; }

        TimeSpan RetryTimeout { get; }

        void ExecuteCommand(RealtimeCommand cmd);

        ITransport Transport { get; }

        Connection Connection { get; }

        TimeSpan SuspendRetryTimeout { get; }

        bool ShouldWeRenewToken(ErrorInfo error, RealtimeState state);
    }
}
