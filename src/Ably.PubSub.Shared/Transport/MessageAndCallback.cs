using System;
using IO.Ably.Types;

namespace IO.Ably.Transport
{
    internal class MessageAndCallback
    {
        internal ILogger Logger { get; private set; }

        public long Serial => Message.MsgSerial;

        public ProtocolMessage Message { get; }

        public Action<bool, ErrorInfo> Callback { get; }

        public MessageAndCallback(ProtocolMessage message, Action<bool, ErrorInfo> callback, ILogger logger = null)
        {
            Message = message;
            Callback = callback;
            Logger = logger ?? DefaultLogger.LoggerInstance;
        }

        protected bool Equals(MessageAndCallback other)
        {
            return Equals(Message.MsgSerial, other.Message.MsgSerial);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((MessageAndCallback)obj);
        }

        public override int GetHashCode()
        {
            return Message?.MsgSerial.GetHashCode() ?? 0;
        }
    }

    internal static class MessageAndCallbackExtensions
    {
        public static void SafeExecute(this MessageAndCallback info, bool success, ErrorInfo error)
        {
            try
            {
                info.Callback?.Invoke(success, error);
            }
            catch (Exception)
            {
                var result = success ? "Success" : "Failed";
                var errorMessage = error != null ? $"Error: {error}" : string.Empty;
                info.Logger.Error($"Error executing callback for message with serial {info.Message.MsgSerial}. Result: {result}. {errorMessage}");
            }
        }
    }
}
