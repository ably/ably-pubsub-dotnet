using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;

namespace IO.Ably.Tests
{
    internal class FakeConnectionContext : IConnectionContext
    {
        public FakeConnectionContext()
        {
            Connection = new Connection(null, TestHelpers.NowFunc());
            Connection.Initialise();
        }

        public ConnectionStateBase LastSetState { get; set; }

        public bool ShouldWeRenewTokenValue { get; set; }

        public TimeSpan DefaultTimeout { get; set; } = Defaults.RealtimeRequestTimeout;

        public TimeSpan RetryTimeout { get; set; } = Defaults.DisconnectedRetryTimeout;

        public void ExecuteCommand(RealtimeCommand cmd)
        {
            ExecutedCommands.Add(cmd);
        }

        public readonly List<RealtimeCommand> ExecutedCommands = new List<RealtimeCommand>();

        public ITransport Transport { get; set; }

        public Connection Connection { get; set; }

        public TimeSpan SuspendRetryTimeout { get; set; }

        public bool ShouldWeRenewToken(ErrorInfo error, RealtimeState state)
        {
            return ShouldWeRenewTokenValue;
        }

        public void ShouldQueueCommand<T>(Action<T> commandCheck = null)
            where T : RealtimeCommand
        {
            var lastCommand = ExecutedCommands.LastOrDefault();
            lastCommand.Should().NotBeNull();
            lastCommand.Should().BeOfType<T>();
            commandCheck?.Invoke(lastCommand as T);
        }

        public void ShouldHaveNotChangedState()
        {
            LastSetState.Should().BeNull();
        }
    }
}
