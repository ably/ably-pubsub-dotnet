using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Ably.PubSub;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;

namespace Assets.Tests.AblySandbox
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
                    if (_wrappedTransport.BeforeDataProcessed != null)
                    {
                        _wrappedTransport.BeforeDataProcessed?.Invoke(msg);
                        data = _handler.GetTransportData(msg);
                    }
                }
                catch (Exception e)
                {
                    DefaultLogger.Error("Error handling beforeMessage helper.", e);
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
                catch (Exception e)
                {
                    DefaultLogger.Error("Error handling afterMessage helper.", e);
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

        public Action<ProtocolMessage> BeforeDataProcessed;
        public Action<ProtocolMessage> AfterDataReceived;
        public Action<ProtocolMessage> MessageSent = delegate { };

        public TestTransportWrapper(ITransport wrappedTransport, Protocol protocol)
        {
            WrappedTransport = wrappedTransport;
            _handler = new MessageHandler(DefaultLogger.LoggerInstance, protocol);
        }

        public bool ThrowOnConnect { get; set; } = false;

        public Guid Id => WrappedTransport.Id;

        public TransportState State => WrappedTransport.State;

        public ITransportListener Listener
        {
            get => WrappedTransport.Listener;
            set => WrappedTransport.Listener = new TransportListenerWrapper(this, value, _handler);
        }

        public void FakeTransportState(TransportState state, Exception ex = null)
        {
            Listener?.OnTransportEvent(Id, state, ex);
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

            WrappedTransport.Connect();
        }

        public void Close(bool suppressClosedEvent = true)
        {
            DefaultLogger.Debug("Closing test transport!");
            WrappedTransport.Close(suppressClosedEvent);
        }

        public Result Send(RealtimeTransportData data)
        {
            if (BlockSendActions.Contains(data.Original.Action))
            {
                return Result.Ok();
            }

            ProtocolMessagesSent.Add(data.Original);
            MessageSent(data.Original);
            WrappedTransport.Send(data);

            return Result.Ok();
        }

        public void Dispose()
        {
        }
    }
}
