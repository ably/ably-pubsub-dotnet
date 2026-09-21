namespace IO.Ably.Realtime.Workflow
{
    internal class ChannelCommand : RealtimeCommand
    {
        private ChannelCommand(string channelName, RealtimeCommand command)
        {
            ChannelName = channelName;
            Command = command;
        }

        public string ChannelName { get; }

        public RealtimeCommand Command { get; }

        public static ChannelCommand CreateForAllChannels(RealtimeCommand command) =>
            new ChannelCommand(null, command);

        protected override string ExplainData()
        {
            return $"ChannelName: {ChannelName}, Command: {Command.Name}";
        }
    }

    internal class InitialiseFailedChannelsOnConnect : RealtimeCommand
    {
        public static InitialiseFailedChannelsOnConnect Create() => new InitialiseFailedChannelsOnConnect();

        protected override string ExplainData()
        {
            return string.Empty;
        }
    }
}
