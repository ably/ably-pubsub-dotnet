using System;
using IO.Ably.Types;

namespace IO.Ably.Realtime
{
    /// <summary>
    /// Class that encapsulates data sent over Ably Websocket Transport.
    /// </summary>
    public class RealtimeTransportData
    {
        /// <summary>
        /// Original Protocol message. It's handy for logging and debugging.
        /// </summary>
        public ProtocolMessage Original { get; set; }

        /// <summary>
        /// Whether it's a binary message.
        /// </summary>
        public bool IsBinary => Length > 0;

        /// <summary>
        /// Binary data.
        /// </summary>
        public byte[] Data { get; } = Array.Empty<byte>();

        /// <summary>
        /// Text data.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Length of binary data.
        /// </summary>
        public int Length => Data.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="RealtimeTransportData"/> class with text data.
        /// </summary>
        /// <param name="text">Text data.</param>
        public RealtimeTransportData(string text)
        {
            Text = text;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RealtimeTransportData"/> class with binary data.
        /// </summary>
        /// <param name="data">Binary data.</param>
        public RealtimeTransportData(byte[] data)
        {
            Data = data;
        }

        /// <summary>
        /// Either returns the text message or the length of the binary data.
        /// </summary>
        /// <returns>text description.</returns>
        public string Explain()
        {
            if (IsBinary)
            {
                return $"Binary message with length: {Length}";
            }

            return Text;
        }
    }
}
