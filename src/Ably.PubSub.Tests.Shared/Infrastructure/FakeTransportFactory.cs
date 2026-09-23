using System;
using System.Collections.Generic;
using IO.Ably.Transport;

namespace IO.Ably.Tests.Realtime
{
    public class FakeTransportFactory : ITransportFactory
    {
        public FakeTransport LastCreatedTransport { get; set; }

        /// <summary>
        /// Every transport created, oldest first. LastCreatedTransport alone cannot answer questions
        /// about a sequence of attempts - how many were made, or whether each carried a resume.
        /// </summary>
        public List<FakeTransport> CreatedTransports { get; } = new List<FakeTransport>();

        public Action<FakeTransport> InitialiseFakeTransport = obj => { };

        public ITransport CreateTransport(TransportParams parameters)
        {
            LastCreatedTransport = new FakeTransport(parameters);
            CreatedTransports.Add(LastCreatedTransport);
            InitialiseFakeTransport(LastCreatedTransport);
            return LastCreatedTransport;
        }
    }
}
