using System;
using IO.Ably.Transport;

namespace IO.Ably.Tests.Realtime
{
    public class FakeTransportFactory : ITransportFactory
    {
        public FakeTransport LastCreatedTransport { get; set; }

        public Action<FakeTransport> InitialiseFakeTransport = obj => { };

        public ITransport CreateTransport(TransportParams parameters)
        {
            LastCreatedTransport = new FakeTransport(parameters);
            InitialiseFakeTransport(LastCreatedTransport);
            return LastCreatedTransport;
        }
    }
}
