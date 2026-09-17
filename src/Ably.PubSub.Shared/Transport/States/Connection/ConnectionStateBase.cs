using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Transport.States.Connection
{
    [DebuggerDisplay("{State}")]
    internal abstract class ConnectionStateBase
    {
        protected ConnectionStateBase(IConnectionContext context, ILogger logger)
        {
            Logger = logger ?? DefaultLogger.LoggerInstance;
            Context = context;
        }

        internal ILogger Logger { get; private set; }

        protected readonly IConnectionContext Context;

        public abstract ConnectionState State { get; }

        public ErrorInfo Error { get; protected set; }

        public Exception Exception { get; set; }

        public TimeSpan? RetryIn { get; protected set; }

        public virtual bool CanQueue => false;

        public virtual bool CanSend => false;

        public bool IsUpdate { get; protected set; }

        public virtual ErrorInfo DefaultErrorInfo => ErrorInfo.ReasonUnknown;

        public virtual RealtimeCommand Connect()
        {
            return EmptyCommand.Instance;
        }

        public virtual void Close()
        {
        }

        public virtual Task<bool> OnMessageReceived(ProtocolMessage message, RealtimeState state)
        {
            return Task.FromResult(false);
        }

        public virtual void AbortTimer()
        {
        }

        public virtual void StartTimer()
        {
        }

        public override string ToString()
        {
            return $"Type: {GetType().Name}, State: {State}";
        }
    }
}
