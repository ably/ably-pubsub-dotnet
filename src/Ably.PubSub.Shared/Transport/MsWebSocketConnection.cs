using System;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IO.Ably.Realtime;
using IO.Ably.Utils;

namespace IO.Ably.Transport
{
    /// <summary>
    /// Wrapper around Websocket which handles state changes.
    /// </summary>
    public class MsWebSocketConnection : IDisposable
    {
        private const int MaxAllowedBufferSize = 65536;

        /// <summary>
        /// Websocket connection possible states.
        /// </summary>
        public enum ConnectionState
        {
            /// <summary>
            /// Connecting.
            /// </summary>
            Connecting,

            /// <summary>
            /// Connected.
            /// </summary>
            Connected,

            /// <summary>
            /// Error.
            /// </summary>
            Error,

            /// <summary>
            /// Closing.
            /// </summary>
            Closing,

            /// <summary>
            /// Closed.
            /// </summary>
            Closed
        }

        private bool _disposed;

        internal ILogger Logger { get; set; } = DefaultLogger.LoggerInstance;

        private readonly Uri _uri;
        private Action<ConnectionState, Exception> _handler;

        private readonly Channel<MessageToSend> _sendChannel = Channel.CreateUnbounded<MessageToSend>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

        internal ClientWebSocket ClientWebSocket { get; set; }

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="MsWebSocketConnection"/> class.
        /// </summary>
        /// <param name="uri">Uri used for the websocket connection.</param>
        /// <param name="socketOptions">additional socket options.</param>
        public MsWebSocketConnection(Uri uri, MsWebSocketOptions socketOptions)
        {
            _uri = uri;
            ClientWebSocket = new ClientWebSocket();

            if (socketOptions.SendBufferInBytes.HasValue || socketOptions.ReceiveBufferInBytes.HasValue)
            {
                var receiveBuffer = socketOptions.ReceiveBufferInBytes ?? (8 * 1024);
                var sendBuffer = socketOptions.SendBufferInBytes ?? (8 * 1024);
                receiveBuffer = Math.Min(receiveBuffer, MaxAllowedBufferSize);
                sendBuffer = Math.Min(sendBuffer, MaxAllowedBufferSize);

                if (Logger.IsDebug)
                {
                    Logger.Debug($"Setting socket buffers to: Receive: {receiveBuffer}. Send: {sendBuffer}");
                }

                ClientWebSocket.Options.SetBuffer(receiveBuffer, sendBuffer);
            }
        }

        /// <summary>
        /// Uses the passed handler to notify about events.
        /// </summary>
        /// <param name="handler">Handler used for the notifications.</param>
        public void SubscribeToEvents(Action<ConnectionState, Exception> handler) => _handler = handler;

        /// <summary>
        /// Clears the currently saved notification handler.
        /// </summary>
        public void ClearStateHandler() => _handler = null;

        /// <summary>
        /// Start the websocket connection and the background thread used for sending messages.
        /// </summary>
        /// <returns>return a Task.</returns>
        public async Task StartConnectionAsync()
        {
            _tokenSource = new CancellationTokenSource();
            _handler?.Invoke(ConnectionState.Connecting, null);
            try
            {
                await ClientWebSocket.ConnectAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                StartSenderBackgroundThread();
                _handler?.Invoke(ConnectionState.Connected, null);
            }
            catch (Exception ex)
            {
                if (Logger != null && Logger.IsDebug)
                {
                    Logger.Debug("Error starting connection", ex);
                }

                _handler?.Invoke(ConnectionState.Error, ex);
            }

            void StartSenderBackgroundThread()
            {
                _ = Task.Factory.StartNew(_ => ProcessSenderQueue(), TaskCreationOptions.LongRunning, _tokenSource.Token);
            }
        }

        private async Task ProcessSenderQueue()
        {
            if (_disposed)
            {
                throw new AblyException($"Attempting to start sender queue consumer when {typeof(MsWebSocketConnection)} has been disposed is not allowed.");
            }

            try
            {
                while (await _sendChannel.Reader.WaitToReadAsync())
                {
                    while (_sendChannel.Reader.TryRead(out var message))
                    {
                        await Send(message.Message, message.Type, _tokenSource.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                if (Logger != null && Logger.IsDebug)
                {
                    Logger.Debug(
                        _disposed ? $"{typeof(MsWebSocketConnection)} has been Disposed." : "WebSocket Send operation cancelled.",
                        e);
                }
            }
            catch (OperationCanceledException e)
            {
                if (Logger != null && Logger.IsDebug)
                {
                    Logger.Debug(
                        _disposed ? $"{typeof(MsWebSocketConnection)} has been Disposed, WebSocket send operation cancelled." : "WebSocket Send operation cancelled.",
                        e);
                }
            }
            catch (Exception e)
            {
                Logger?.Error("Error Sending to WebSocket", e);
                _handler?.Invoke(ConnectionState.Error, e);
            }
        }

        /// <summary>
        /// Closes the websocket connection.
        /// </summary>
        /// <returns>returns a Task.</returns>
        public async Task StopConnectionAsync()
        {
            _handler?.Invoke(ConnectionState.Closing, null);
            try
            {
                if (ClientWebSocket.CloseStatus.HasValue)
                {
                    if (Logger != null && Logger.IsDebug)
                    {
                        Logger.Debug(
                            "Closing websocket. Close status: "
                            + Enum.GetName(typeof(WebSocketCloseStatus), ClientWebSocket.CloseStatus)
                            + ", Description: " + ClientWebSocket.CloseStatusDescription);
                    }
                }

                if (!_disposed)
                {
                    if (ClientWebSocket?.State != WebSocketState.Closed)
                    {
                        await ClientWebSocket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    _tokenSource?.Cancel();
                }

                _handler?.Invoke(ConnectionState.Closed, null);
            }
            catch (ObjectDisposedException ex)
            {
                if (Logger != null && Logger.IsDebug)
                {
                    Logger.Debug($"Error stopping connection. {typeof(MsWebSocketConnection)} was disposed.", ex);
                }

                _handler?.Invoke(ConnectionState.Closed, ex);
            }
            catch (Exception ex)
            {
                Logger?.Warning("Error stopping connection.", ex);
                _handler?.Invoke(ConnectionState.Closed, ex);
            }
        }

        /// <summary>
        /// Sends a text message over the websocket connection.
        /// </summary>
        /// <param name="message">The text message sent over the websocket.</param>
        public void SendText(string message)
        {
            EnqueueForSending(new MessageToSend(message.GetBytes(), WebSocketMessageType.Text));
        }

        /// <summary>
        /// Sends a binary message over the websocket connection.
        /// </summary>
        /// <param name="data">The data to be sent over the websocket.</param>
        public void SendData(byte[] data)
        {
            EnqueueForSending(new MessageToSend(data, WebSocketMessageType.Binary));
        }

        private void EnqueueForSending(MessageToSend message)
        {
            try
            {
                var writeResult = _sendChannel.Writer.TryWrite(message);
                if (writeResult == false)
                {
                    Logger.Warning("Failed to enqueue message to WebSocket connection. The connection is being disposed.");
                }
            }
            catch (Exception e)
            {
                var msg = _disposed
                          ? $"EnqueueForSending failed. {typeof(MsWebSocketConnection)} has been Disposed."
                          : "EnqueueForSending failed.";

                Logger?.Error(msg, e);
                throw;
            }
        }

        private async Task Send(ArraySegment<byte> data, WebSocketMessageType type, CancellationToken token)
        {
            if (ClientWebSocket.State != WebSocketState.Open)
            {
                Logger?.Warning($"Trying to send message of type {type} when the socket is {ClientWebSocket.State}. Ack for this message will fail shortly.");
                return;
            }

            await ClientWebSocket.SendAsync(data, type, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The Receive methods starts the receiving loop. It's run on a separate thread and it waits for data to become
        /// available on the websocket.
        /// </summary>
        /// <param name="handleMessage">The handle which is notified when a message is received.</param>
        /// <returns>return a Task.</returns>
        public async Task Receive(Action<RealtimeTransportData> handleMessage)
        {
            while (ClientWebSocket?.State == WebSocketState.Open)
            {
                try
                {
                    var buffer = new ArraySegment<byte>(new byte[1024 * 16]); // Default receive buffer size
                    WebSocketReceiveResult result;
                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await ClientWebSocket.ReceiveAsync(buffer, _tokenSource.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }

                            ms.Write(buffer.Array ?? throw new InvalidOperationException("buffer cannot be null"), buffer.Offset, result.Count);
                        }
                        while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        switch (result.MessageType)
                        {
                            case WebSocketMessageType.Text:
                                var text = ms.ToArray().GetText();
                                handleMessage?.Invoke(new RealtimeTransportData(text));
                                break;
                            case WebSocketMessageType.Binary:
                                handleMessage?.Invoke(new RealtimeTransportData(ms.ToArray()));
                                break;
                        }
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await StopConnectionAsync();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _handler?.Invoke(ConnectionState.Error, ex);
                    break;
                }
            }
        }

        /// <summary>
        /// Dispose method. Stops the send thread and disposes the websocket.
        /// </summary>
        /// <param name="disposing">Whether it should do some disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _tokenSource.Cancel();
                _tokenSource.Dispose();
                _sendChannel?.Writer.Complete();
                ClientWebSocket?.Dispose();
            }

            _disposed = true;
        }

#pragma warning disable CA1063  // Implement IDisposable correctly.
        /// <summary>
        /// Cleans up resources and disconnects the websocket.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);

            // Typically we would call GC.SuppressFinalize(this)
            // at this point in the Dispose pattern
            // to suppress an expensive GC cycle
            // But disposing of the Connection should not be frequent
            // and based on profiling this speeds up the release of objects
            // and reduces memory bloat considerably
        }
#pragma warning default CA1063  // Implement IDisposable correctly.

        /// <summary>
        /// Attempt to query the backlog length of the queue.
        /// </summary>
        /// <param name="count">The (approximate) count of items in the Channel.</param>
        /// <returns>true if it managed to get count.</returns>
        public bool TryGetCount(out int count)
        {
            // get this using the reflection
            try
            {
                var prop = _sendChannel.GetType()
                    .GetProperty("ItemsCountForDebugger", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null)
                {
                    count = (int)prop.GetValue(_sendChannel);
                    return true;
                }
            }
            catch (Exception e)
            {
                ErrorPolicy.HandleUnexpected(e, Logger);
            }

            count = default(int);
            return false;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="MsWebSocketConnection"/> class.
        /// </summary>
        ~MsWebSocketConnection()
        {
            Dispose(false);
        }

        private readonly struct MessageToSend
        {
            public MessageToSend(byte[] message, WebSocketMessageType type)
            {
                Message = new ArraySegment<byte>(message);
                Type = type;
            }

            public ArraySegment<byte> Message { get; }

            public WebSocketMessageType Type { get; }
        }
    }
}
