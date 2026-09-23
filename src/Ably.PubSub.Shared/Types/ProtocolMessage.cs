using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;

namespace IO.Ably.Types
{
    /// <summary>
    ///  A message sent and received over the Realtime protocol.
    ///  A ProtocolMessage always relates to a single channel only, but
    ///  can contain multiple individual Messages or PresenceMessages.
    ///  ProtocolMessages are serially numbered on a connection.
    ///  See the Ably client library developer documentation for further
    ///  details on the members of a ProtocolMessage.
    /// </summary>
    public class ProtocolMessage
    {
        /// <summary>
        /// Action associated with the message.
        /// </summary>
        public enum MessageAction
        {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1602 // Enumeration items should be documented
            Heartbeat = 0,
            Ack = 1,
            Nack = 2,
            Connect = 3,
            Connected = 4,
            Disconnect = 5,
            Disconnected = 6,
            Close = 7,
            Closed = 8,
            Error = 9,
            Attach = 10,
            Attached = 11,
            Detach = 12,
            Detached = 13,
            Presence = 14,
            Message = 15,
            Sync = 16,
            Auth = 17,
#pragma warning restore SA1602 // Enumeration items should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        }

        /// <summary>
        /// Message Flag sent by the server.
        /// </summary>
        [Flags]
        public enum Flag
        {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1602 // Enumeration items should be documented
            HasPresence = 1 << 0,
            HasBacklog = 1 << 1,
            Resumed = 1 << 2,
            HasLocalPresence = 1 << 3,
            Transient = 1 << 4,
            AttachResume = 1 << 5,

            // Channel modes
            Presence = 1 << 16,
            Publish = 1 << 17,
            Subscribe = 1 << 18,
            PresenceSubscribe = 1 << 19,
#pragma warning restore SA1602 // Enumeration items should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        }

        /// <summary>
        /// Helper method to check for the existence of a flag in an integer.
        /// </summary>
        /// <param name="value">int value storing the flag.</param>
        /// <param name="flag">flag we check for.</param>
        /// <returns>true or false.</returns>
        public static bool HasFlag(int? value, Flag flag)
        {
            if (value == null)
            {
                return false;
            }

            return (value.Value & (int)flag) != 0;
        }

        /// <summary>
        /// Channel params is a Dictionary&lt;string, string&gt; which is used to pass parameters to the server when
        /// attaching a channel. Some params include `delta` and `rewind`. The server will also echo the params in the
        /// ATTACHED message.
        /// For more information https://ably.com/docs/realtime/channels/channel-parameters/overview.
        /// </summary>
        [JsonProperty("params")]
        public ChannelParams Params { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProtocolMessage"/> class.
        /// </summary>
        public ProtocolMessage()
        {
            Messages = new Message[] { };
            Presence = new PresenceMessage[] { };
        }

        internal ProtocolMessage(MessageAction action)
            : this()
        {
            Action = action;
        }

        internal ProtocolMessage(MessageAction action, string channel)
            : this(action)
        {
            Channel = channel;
        }

        /// <summary>
        /// Current message action.
        /// </summary>
        [JsonProperty("action")]
        public MessageAction Action { get; set; }

        /// <summary>
        /// <see cref="AuthDetails"/> for the current message.
        /// </summary>
        [JsonProperty("auth")]
        public AuthDetails Auth { get; set; }

        /// <summary>
        /// Current message flags.
        /// </summary>
        [JsonProperty("flags")]
        public int? Flags { get; set; }

        /// <summary>
        /// Count.
        /// </summary>
        [JsonProperty("count")]
        public int? Count { get; set; }

        /// <summary>
        /// Error associated with the message.
        /// </summary>
        [JsonProperty("error")]
        public ErrorInfo Error { get; set; }

        /// <summary>
        /// Ably generated message id.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Optional channel for which the message belongs to.
        /// </summary>
        [JsonProperty("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// Current channel serial.
        /// </summary>
        [JsonProperty("channelSerial")]
        public string ChannelSerial { get; set; }

        /// <summary>
        /// Current connectionId.
        /// </summary>
        [JsonProperty("connectionId")]
        public string ConnectionId { get; set; }

        /// <summary>
        /// Current message serial.
        /// </summary>
        [JsonProperty("msgSerial")]
        public long MsgSerial { get; set; }

        /// <summary>
        /// Timestamp of the message.
        /// </summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// List of messages contained in this protocol message.
        /// </summary>
        [JsonProperty("messages")]
        public Message[] Messages { get; set; }

        /// <summary>
        /// List of presence messages contained in this protocol message.
        /// </summary>
        [JsonProperty("presence")]
        public PresenceMessage[] Presence { get; set; }

        /// <summary>
        /// Connection details received. <see cref="IO.Ably.ConnectionDetails"/>.
        /// </summary>
        [JsonProperty("connectionDetails")]
        public ConnectionDetails ConnectionDetails { get; set; }

        [JsonIgnore]
        internal bool AckRequired => Action == MessageAction.Message || Action == MessageAction.Presence;

        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            if (Channel == string.Empty)
            {
                Channel = null;
            }

            // Filter out empty messages
            if (Messages != null)
            {
                Messages = Messages.Where(m => !m.IsEmpty).ToArray();
                if (Messages.Length == 0)
                {
                    Messages = null;
                }
            }

            if (Presence != null && Presence.Length == 0)
            {
                Presence = null;
            }
        }

        /// <summary>
        /// Convenience method to check if the current message contains a flag.
        /// </summary>
        /// <param name="flag">Flag we are looking for.</param>
        /// <returns>true / false.</returns>
        public bool HasFlag(Flag flag)
        {
            return HasFlag(Flags, flag);
        }

        /// <summary>
        /// Convenience method to add <see cref="Flag"/> to the Flags property.
        /// </summary>
        /// <param name="flag"><see cref="Flag"/> to be added.</param>
        public void SetFlag(Flag flag)
        {
            var value = Flags.GetValueOrDefault();
            value |= (int)flag;
            Flags = value;
        }

        internal void SetModesAsFlags(IEnumerable<ChannelMode> modes)
        {
            foreach (var mode in modes)
            {
                var flag = mode.ToFlag();
                if (flag != null)
                {
                    SetFlag(flag.Value);
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append("{ ")
                .AppendFormat("action={0}", Action);
            if (Messages != null && Messages.Length > 0)
            {
                text.Append(", messages=[ ");
                foreach (var message in Messages)
                {
                    text.AppendFormat("{{ name=\"{0}\"", message.Name);
                    if (message.Timestamp.HasValue && message.Timestamp.Value.Ticks > 0)
                    {
                        text.AppendFormat(", timestamp=\"{0}\"}}", message.Timestamp);
                    }

                    text.AppendFormat(", data={0}}}", message.Data);
                }

                text.Append(" ]");
            }

            text.Append(" }");
            return text.ToString();
        }
    }
}
