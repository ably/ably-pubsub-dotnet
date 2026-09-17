using System;
using IO.Ably.Types;

namespace IO.Ably.Transport
{
    /// <summary>
    /// Contains current connection details.
    /// </summary>
    public class ConnectionInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionInfo"/> class.
        /// </summary>
        public ConnectionInfo() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionInfo"/> class.
        /// </summary>
        /// <param name="message">initialises it from a message.</param>
        public ConnectionInfo(ProtocolMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Null message");
            }

            if (message.Action != ProtocolMessage.MessageAction.Connected)
            {
                throw new InvalidOperationException(
                    $"A ConnectionInfo only be created from a Connected action protocol message. A value with action '{message.Action}' was passed");
            }

            ConnectionId = message.ConnectionId;
            ClientId = message.ConnectionDetails?.ClientId;
            ConnectionStateTtl = message.ConnectionDetails?.ConnectionStateTtl;
            ConnectionKey = message.ConnectionDetails?.ConnectionKey;
        }

        /// <summary>
        /// current connection time to live.
        /// </summary>
        public TimeSpan? ConnectionStateTtl { get; private set; }

        /// <summary>
        /// contains the client ID assigned to the connection.
        /// </summary>
        public string ClientId { get; private set; }

        /// <summary>
        /// Unique id of the current connection.
        /// </summary>
        public string ConnectionId { get; private set; }

        /// <summary>
        /// the connection secret key string that is used to resume a connection and its state.
        /// </summary>
        public string ConnectionKey { get; private set; }
    }
}
