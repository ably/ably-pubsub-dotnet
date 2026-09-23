using System;
using System.Threading;
using System.Threading.Tasks;

using IO.Ably.Realtime;

namespace IO.Ably.Transport
{
    /// <summary>
    /// Class encapsulating additional parameters for the websocket connection.
    /// </summary>
    public class MsWebSocketOptions
    {
        /// <summary>
        /// If populated it will specify a new value for the Send buffer used in the websocket
        /// Default: 8192.
        /// </summary>
        public int? SendBufferInBytes { get; set; }

        /// <summary>
        /// If populated it will specify a new value for the Receive buffer used in the websocket
        /// Default: 8192.
        /// </summary>
        public int? ReceiveBufferInBytes { get; set; }
    }

    /// <summary>
    /// Websocket Transport implementation based on System.Net.Websocket.
    /// </summary>
    public class MsWebSocketTransport : ITransport
    {
        private readonly MsWebSocketOptions _socketOptions;
        private bool _disposed;
        private readonly CancellationTokenSource _readerThreadSource = new CancellationTokenSource();
        private MsWebSocketConnection _socket;

        /// <inheritdoc />
        public class TransportFactory : ITransportFactory
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TransportFactory"/> class.
            /// </summary>
            public TransportFactory()
            {
                SocketOptions = new MsWebSocketOptions();
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="TransportFactory"/> class.
            /// </summary>
            /// <param name="socketOptions">Additional Websocket options. <see cref="MsWebSocketOptions"/>.</param>
            public TransportFactory(MsWebSocketOptions socketOptions)
            {
                SocketOptions = socketOptions;
            }

            /// <summary>
            /// Custom <see cref="MsWebSocketOptions"/> passed to the MsWebsocket transport and then
            /// <see cref="MsWebSocketConnection"/>.
            /// </summary>
            public MsWebSocketOptions SocketOptions { get; }

            /// <inheritdoc/>
            public ITransport CreateTransport(TransportParams parameters)
            {
                return new MsWebSocketTransport(parameters, SocketOptions);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MsWebSocketTransport"/> class.
        /// </summary>
        /// <param name="parameters">Transport parameters.</param>
        /// <param name="socketOptions">Additional websocket options. See <see cref="MsWebSocketOptions"/>.</param>
        protected MsWebSocketTransport(TransportParams parameters, MsWebSocketOptions socketOptions)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters), "Null parameters are not allowed");
            }

            _socketOptions = socketOptions ?? new MsWebSocketOptions();

            BinaryProtocol = parameters.UseBinaryProtocol;
            WebSocketUri = parameters.GetUri();
            Logger = parameters.Logger ?? DefaultLogger.LoggerInstance;
        }

        /// <inheritdoc cref="ITransport" />
        public bool BinaryProtocol { get; }

        /// <inheritdoc cref="ITransport" />
        public Uri WebSocketUri { get; }

        /// <inheritdoc cref="ITransport" />
        public Guid Id { get; } = Guid.NewGuid();

        /// <inheritdoc cref="ITransport" />
        public TransportState State { get; set; } = TransportState.Initialized;

        /// <inheritdoc cref="ITransport" />
        public ITransportListener Listener { get; set; }

        private ILogger Logger { get; set; }

        /// <inheritdoc cref="ITransport" />
        public void Connect()
        {
            if (_socket == null)
            {
                _socket = CreateSocket(WebSocketUri);
                AttachEvents();
            }

            StartReaderBackgroundThread();
        }

        private void StartReaderBackgroundThread()
        {
            _ = Task.Factory.StartNew(_ => ConnectAndStartListening(), TaskCreationOptions.LongRunning, _readerThreadSource.Token).ConfigureAwait(false);
        }

        private async Task ConnectAndStartListening()
        {
            try
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug("Connecting socket");
                }

                await _socket.StartConnectionAsync();

                if (Logger.IsDebug)
                {
                    Logger.Debug("Socket connected");
                }

                if (_socket == null)
                {
                    HandleError(new NullReferenceException($"'{nameof(_socket)}' is null"));
                }
                else
                {
                    await _socket.Receive(HandleMessageReceived);
                }
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }

            void HandleError(Exception e)
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug($"Socket couldn't connect. Error: {e.Message}");
                }

                Listener?.OnTransportEvent(Id, TransportState.Closed, e);
            }
        }

        private void HandleMessageReceived(RealtimeTransportData data)
        {
            if (data.IsBinary)
            {
                if (Logger.IsDebug)
                {
                    try
                    {
#if MSGPACK
                        var message = MsgPackHelper.DeserialiseMsgPackObject(data.Data).ToString();
                        Logger.Debug("Websocket data message received. Raw: " + message);
#endif
                    }
                    catch (Exception)
                    {
                        Logger.Debug("Error parsing message as MsgPack.");
                    }
                }

                Listener?.OnTransportDataReceived(data);
            }
            else
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug("Websocket message received. Raw: " + data.Text);
                }

                Listener?.OnTransportDataReceived(data);
            }
        }

        /// <inheritdoc/>
        public void Close(bool suppressClosedEvent = true)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug("Closing socket. Current socket is " + (_socket == null ? "null" : "not null"));
            }

            if (_socket != null)
            {
                if (suppressClosedEvent)
                {
                    DetachEvents();
                }

                try
                {
                    _ = Task.Run(_socket.StopConnectionAsync);
                }
                catch (Exception e)
                {
                    Logger.Warning(e.Message);
                }
            }
        }

        /// <inheritdoc/>
        public Result Send(RealtimeTransportData data)
        {
            if (_socket is null)
            {
                return Result.Fail($"Cannot send message. Socket instance is null. Transport state is: {State}");
            }

            if (BinaryProtocol)
            {
                _socket?.SendData(data.Data);
            }
            else
            {
                if (Logger.IsDebug)
                {
                    Logger.Debug("Sending Text: " + data.Text);
                }

                _socket?.SendText(data.Text);
            }

            return Result.Ok();
        }

        private MsWebSocketConnection CreateSocket(Uri uri)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug("Connecting to web socket on url: " + uri);
            }

            return new MsWebSocketConnection(uri, _socketOptions) { Logger = Logger };
        }

        private void AttachEvents()
        {
            _socket?.SubscribeToEvents(HandleStateChange);
        }

        private void HandleStateChange(MsWebSocketConnection.ConnectionState state, Exception error)
        {
            if (Logger.IsDebug)
            {
                Logger.Debug($"Transport State: {state}. Error is {error?.Message ?? "empty"}. {error?.StackTrace}");
            }

            switch (state)
            {
                case MsWebSocketConnection.ConnectionState.Connecting:
                    Listener?.OnTransportEvent(Id, TransportState.Connecting, error);
                    State = TransportState.Connecting;
                    break;
                case MsWebSocketConnection.ConnectionState.Connected:
                    Listener?.OnTransportEvent(Id, TransportState.Connected, error);
                    State = TransportState.Connected;
                    break;
                case MsWebSocketConnection.ConnectionState.Error:
                    if (State != TransportState.Closing && State != TransportState.Closed)
                    {
                        Listener?.OnTransportEvent(Id, TransportState.Closing, error);
                    }

                    DisposeSocketConnection();
                    break;
                case MsWebSocketConnection.ConnectionState.Closing:
                    State = TransportState.Closing;
                    Listener?.OnTransportEvent(Id, TransportState.Closing, error);
                    break;
                case MsWebSocketConnection.ConnectionState.Closed:
                    State = TransportState.Closed;
                    Listener?.OnTransportEvent(Id, TransportState.Closed, error);
                    DisposeSocketConnection();
                    break;
                default:
                    throw new Exception("Unrecognised HandleState");
            }
        }

        private void DisposeSocketConnection()
        {
            DetachEvents();
            try
            {
                _socket?.Dispose();
            }
            catch (Exception e)
            {
                Logger.Warning("Error while disposing socket. Nothing to worry about. Message: " + e.Message);
            }
            finally
            {
                _socket = null;
            }
        }

        private void DetachEvents()
        {
            if (_socket != null)
            {
                try
                {
                    _socket.ClearStateHandler();
                }
                catch (Exception ex)
                {
                    Logger.Warning("Error while detaching events handlers. Error: {0}", ex.Message);
                }
            }
        }

        /// <inheritdoc cref="ITransport" />
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _readerThreadSource.Cancel();
                _readerThreadSource.Dispose();
                DisposeSocketConnection();
                Listener = null;
            }

            _disposed = true;
        }

#pragma warning disable CA1063  // Implement IDisposable correctly.
        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);

            // Typically we would call GC.SuppressFinalize(this)
            // at this point in the Dispose pattern
            // to suppress an expensive GC cycle
            // But disposing of the Transport should not be frequent
            // and based on profiling this speeds up the release of objects
            // and reduces memory bloat considerably
        }
#pragma warning default CA1063  // Implement IDisposable correctly.

        /// <summary>
        /// Finalizes an instance of the <see cref="MsWebSocketTransport"/> class.
        /// Calls Dispose(false)
        /// </summary>
        ~MsWebSocketTransport()
        {
            Dispose(false);
        }

        internal void ReleaseClientWebSocket()
        {
            _socket.ClientWebSocket = null;
        }
    }
}
