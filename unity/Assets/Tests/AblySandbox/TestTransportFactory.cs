using System;
using Ably.PubSub;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;

namespace Assets.Tests.AblySandbox
{
    public class TestTransportFactory : ITransportFactory
    {
        private readonly Action<TestTransportWrapper> _onWrappedTransportCreated;

        internal Action<TestTransportWrapper> OnTransportCreated = delegate { };

        internal Action<ProtocolMessage> OnMessageSent = delegate { };

        internal Action<ProtocolMessage> BeforeDataProcessed;

        public TestTransportFactory()
        {
        }

        internal TestTransportFactory(Action<TestTransportWrapper> onWrappedTransportCreated)
        {
            _onWrappedTransportCreated = onWrappedTransportCreated;
        }

        public ITransport CreateTransport(TransportParams parameters)
        {
            var factory = IoC.TransportFactory;
            var transport
                = new TestTransportWrapper(factory.CreateTransport(parameters), parameters.UseBinaryProtocol ? Defaults.Protocol : Protocol.Json);

            transport.BeforeDataProcessed = BeforeDataProcessed;
            OnTransportCreated(transport);
            transport.MessageSent = OnMessageSent;
            _onWrappedTransportCreated?.Invoke(transport);
            return transport;
        }
    }
}
