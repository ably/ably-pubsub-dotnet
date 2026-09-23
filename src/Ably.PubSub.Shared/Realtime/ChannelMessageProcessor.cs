using System.Linq;
using System.Threading.Tasks;
using IO.Ably.MessageEncoders;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;

namespace IO.Ably.Realtime
{
    internal class ChannelMessageProcessor
    {
        internal ILogger Logger { get; private set; }

        private readonly IChannels<IRealtimeChannel> _channels;
        private readonly MessageHandler _messageHandler;

        public ChannelMessageProcessor(IChannels<IRealtimeChannel> channels, MessageHandler messageHandler, ILogger logger)
        {
            Logger = logger;
            _channels = channels;
            _messageHandler = messageHandler;
        }

        private RealtimeChannel GetChannel(string name)
        {
            return _channels.Get(name) as RealtimeChannel;
        }

        public Task<bool> MessageReceived(ProtocolMessage protocolMessage, RealtimeState state)
        {
            if (protocolMessage.Channel.IsEmpty())
            {
                return Task.FromResult(false);
            }

            var channel = _channels.Exists(protocolMessage.Channel) ? GetChannel(protocolMessage.Channel) : null;
            if (channel == null)
            {
                Logger.Warning($"Message received {protocolMessage} for a channel that does not exist {protocolMessage.Channel}");
                return Task.FromResult(false);
            }

            // RTL15b
            if (protocolMessage.ChannelSerial.IsNotEmpty() &&
                (protocolMessage.Action == ProtocolMessage.MessageAction.Message ||
                protocolMessage.Action == ProtocolMessage.MessageAction.Presence ||
                protocolMessage.Action == ProtocolMessage.MessageAction.Attached))
            {
                Logger.Debug($"Setting channel serial for channelName - {channel.Name}," +
                             $"previous - {channel.Properties.ChannelSerial}, current - {protocolMessage.ChannelSerial}");
                channel.Properties.ChannelSerial = protocolMessage.ChannelSerial;
            }

            switch (protocolMessage.Action)
            {
                case ProtocolMessage.MessageAction.Error:
                    channel.SetChannelState(ChannelState.Failed, protocolMessage);
                    break;
                case ProtocolMessage.MessageAction.Attached:
                    channel.Properties.AttachSerial = protocolMessage.ChannelSerial; // RTL15a

                    if (protocolMessage.Flags.HasValue)
                    {
                        var modes = new ChannelModes(((ProtocolMessage.Flag)protocolMessage.Flags.Value).FromFlag());
                        channel.Modes = new ReadOnlyChannelModes(modes.ToList());
                    }

                    if (protocolMessage.Params != null)
                    {
                        channel.Params = new ReadOnlyChannelParams(protocolMessage.Params);
                    }

                    // RTL12
                    if (channel.State == ChannelState.Attached)
                    {
                        if (!protocolMessage.HasFlag(ProtocolMessage.Flag.Resumed))
                        {
                            channel.Presence.ChannelAttached(protocolMessage);
                            channel.EmitErrorUpdate(protocolMessage.Error, false, protocolMessage);
                        }
                    }
                    else
                    {
                        channel.Presence.ChannelAttached(protocolMessage);
                        channel.SetChannelState(ChannelState.Attached, protocolMessage);
                    }

                    break;
                case ProtocolMessage.MessageAction.Detach:
                case ProtocolMessage.MessageAction.Detached:
                    if (channel.State != ChannelState.Detached)
                    {
                        channel.SetChannelState(ChannelState.Detached, protocolMessage);
                    }

                    break;
                case ProtocolMessage.MessageAction.Message:

                    if (channel.State != ChannelState.Attached)
                    {
                        Logger.Warning(
                            $"Channel #{channel.Name} is currently in #{channel.State} state. Messages received in this state are ignored. Ignoring {protocolMessage.Messages?.Length ?? 0} messages");
                        return TaskConstants.BooleanTrue;
                    }

                    if (ValidateIfDeltaItHasCorrectPreviousMessageId(protocolMessage, channel.LastSuccessfulMessageIds) == false)
                    {
                        var message =
                            "Delta message decode failure. Previous message id does not equal expected message id.";
                        var reason = new ErrorInfo(message, ErrorCodes.VcDiffDecodeError);
                        channel.StartDecodeFailureRecovery(reason);
                        return TaskConstants.BooleanTrue;
                    }

                    var result = _messageHandler.DecodeMessages(
                        protocolMessage,
                        protocolMessage.Messages,
                        channel.MessageDecodingContext);

                    if (result.IsFailure)
                    {
                        Logger.Error($"{channel.Name} - failed to decode message. ErrorCode: {result.Error.Code}, Message: {result.Error.Message}");
                        if (result.Error is VcDiffErrorInfo)
                        {
                            channel.StartDecodeFailureRecovery(result.Error);

                            // Break any further message processing
                            return TaskConstants.BooleanTrue;
                        }
                    }

                    channel.LastSuccessfulMessageIds = LastMessageIds.Create(protocolMessage);

                    foreach (var msg in protocolMessage.Messages)
                    {
                        channel.OnMessage(msg);
                    }

                    break;
                case ProtocolMessage.MessageAction.Presence:
                case ProtocolMessage.MessageAction.Sync:

                    var presenceDecodeResult = _messageHandler.DecodeMessages(
                                                protocolMessage,
                                                protocolMessage.Presence,
                                                channel.Options);

                    if (presenceDecodeResult.IsFailure)
                    {
                        Logger.Error($"{channel.Name} - failed to decode presence message. ErrorCode: {presenceDecodeResult.Error.Code}, Message: {presenceDecodeResult.Error.Message}");

                        channel.OnError(presenceDecodeResult.Error);
                    }

                    if (protocolMessage.Action == ProtocolMessage.MessageAction.Sync)
                    {
                        channel.Presence.OnSyncMessage(protocolMessage);
                    }
                    else
                    {
                        channel.Presence.OnPresence(protocolMessage.Presence);
                    }

                    break;
            }

            return Task.FromResult(true);
        }

        private bool ValidateIfDeltaItHasCorrectPreviousMessageId(ProtocolMessage protocolMessage, LastMessageIds channelSuccessfulMessageIds)
        {
            if (protocolMessage.Messages == null || protocolMessage.Messages.Length == 0)
            {
                return true;
            }

            var firstMessage = protocolMessage.Messages.First();
            var deltaFrom = firstMessage.Extras?.Delta?.From;
            if (deltaFrom != null && deltaFrom.EqualsTo(channelSuccessfulMessageIds.LastMessageId) == false)
            {
               Logger.Warning($"Delta message decode failure. Previous message id does not equal expected message id. PreviousMessageId: {channelSuccessfulMessageIds.LastMessageId}. ExpectedMessageId: {deltaFrom}");
               return false;
            }

            return true;
        }
    }
}
