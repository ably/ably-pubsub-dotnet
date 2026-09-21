using System;
using System.Collections.Generic;
using System.Net.Sockets;
using IO.Ably.MessageEncoders;
using IO.Ably.Realtime;
using IO.Ably.Transport;
using IO.Ably.Types;

namespace IO.Ably.Tests.Infrastructure
{
    internal class TestTransportWrapper : ITransport
    {
        private class TransportListenerWrapper : ITransportListener
        {
            private readonly ITransportListener _wrappedListener;
            private readonly TestTransportWrapper _wrappedTransport;
            private readonly MessageHandler _handler;

            public List<ProtocolMessage> ProtocolMessagesReceived { get; set; } = new List<ProtocolMessage>();

            public TransportListenerWrapper(TestTransportWrapper wrappedTransport, ITransportListener wrappedListener, MessageHandler handler)
            {
                _wrappedTransport = wrappedTransport;
                _wrappedListener = wrappedListener;
                _handler = handler;
            }

            public void OnTransportDataReceived(RealtimeTransportData data)
            {
                ProtocolMessage msg = null;
                try
                {
                    msg = _handler.ParseRealtimeData(data);
                    ProtocolMessagesReceived.Add(msg);

                    if (_wrappedTransport.BlockReceiveActions.Contains(msg.Action))
                    {
                        return;
                    }

                    if (_wrappedTransport.BeforeDataProcessed != null)
                    {
                        _wrappedTransport.BeforeDataProcessed?.Invoke(msg);
                        data = _handler.GetTransportData(msg);
                    }
                }
                catch (Exception ex)
                {
                    DefaultLogger.Error("Error handling beforeMessage helper.", ex);
                }

                try
                {
                    _wrappedListener?.OnTransportDataReceived(data);
                }
                catch (Exception e)
                {
                    DefaultLogger.Error("Test transport factory on receive error ", e);
                }

                try
                {
                    _wrappedTransport.AfterDataReceived?.Invoke(msg);
                }
                catch (Exception ex)
                {
                    DefaultLogger.Error("Error handling afterMessage helper.", ex);
                }
            }

            public void OnTransportEvent(Guid transportId, TransportState state, Exception exception = null)
            {
                _wrappedListener?.OnTransportEvent(transportId, state, exception);
            }
        }

        internal ITransport WrappedTransport { get; }

        private readonly MessageHandler _handler;

        /// <summary>
        /// A list of all protocol messages that have been received from the ably service since the transport was created
        /// </summary>
        public List<ProtocolMessage> ProtocolMessagesReceived => (Listener as TransportListenerWrapper)?.ProtocolMessagesReceived;

        public List<ProtocolMessage> ProtocolMessagesSent { get; set; } = new List<ProtocolMessage>();

        public List<ProtocolMessage.MessageAction> BlockSendActions { get; set; } = new List<ProtocolMessage.MessageAction>();

        public List<ProtocolMessage.MessageAction> BlockReceiveActions { get; set; } = new List<ProtocolMessage.MessageAction>();

        public Action<ProtocolMessage> BeforeMessageSend = delegate { };
        public Action<ProtocolMessage> AfterMessageSent = delegate { };

        public Action<ProtocolMessage> BeforeDataProcessed;
        public Action<ProtocolMessage> AfterDataReceived;

        public bool KeepInConnectingState { get; set; }

        public TestTransportWrapper(ITransport wrappedTransport, Protocol protocol)
        {
            WrappedTransport = wrappedTransport;
            _handler = new MessageHandler(DefaultLogger.LoggerInstance, protocol);
        }

        public bool ThrowOnConnect { get; set; }

        public Guid Id => WrappedTransport.Id;

        public TransportState State => WrappedTransport.State;

        public ITransportListener Listener
        {
            get => WrappedTransport.Listener;
            set => WrappedTransport.Listener = new TransportListenerWrapper(this, value, _handler);
        }

        public void FakeReceivedMessage(ProtocolMessage message)
        {
            var data = _handler.GetTransportData(message);
            Listener?.OnTransportDataReceived(data);
        }

        public void Connect()
        {
            if (ThrowOnConnect)
            {
                throw new SocketException();
            }

            if (KeepInConnectingState)
            {
                return;
            }

            WrappedTransport.Connect();
        }

        public void Close(bool suppressClosedEvent = true)
        {
            DefaultLogger.Debug("Closing test transport!");
            WrappedTransport.Close(suppressClosedEvent);
        }

        public Result Send(RealtimeTransportData data)
        {
            BeforeMessageSend(data.Original);

            if (BlockSendActions.Contains(data.Original.Action))
            {
                return Result.Ok();
            }

            ProtocolMessagesSent.Add(data.Original);
            WrappedTransport.Send(data);
            AfterMessageSent(data.Original);
            return Result.Ok();
        }

        public void Dispose()
        {
        }
    }
}
