using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;

namespace Ably.PubSub.Tests
{
    public sealed class FakeTransport : ITransport
    {
        private Action<RealtimeTransportData> _sendAction = obj => { };

        public TransportParams Parameters { get; }

        public FakeTransport(TransportState? state = null)
        {
            if (state.HasValue)
            {
                State = state.Value;
            }
        }

        public FakeTransport(TransportParams parameters)
        {
            Parameters = parameters;
        }

        public bool ConnectCalled { get; private set; }

        public bool CloseCalled { get; set; }

        public ProtocolMessage LastMessageSend => LastTransportData?.Original;

        public List<RealtimeTransportData> SentMessages { get; set; } = new List<RealtimeTransportData>();

        public RealtimeTransportData LastTransportData => SentMessages.LastOrDefault();

        public Guid Id { get; } = Guid.NewGuid();

        public TransportState State { get; set; }

        public ITransportListener Listener { get; set; }

        public bool OnConnectChangeStateToConnected { get; set; } = true;

        /// <summary>
        /// Makes Connect throw, as a transport does when the endpoint cannot be reached at all.
        /// Mirrors TestTransportWrapper.ThrowOnConnect, for tests that need the failure to happen
        /// inside the command that created the transport rather than as a later event.
        /// </summary>
        public bool ThrowOnConnect { get; set; }

        public void Connect()
        {
            DefaultLogger.Debug($"Connecting using: {Parameters.GetUri()}");

            if (ThrowOnConnect)
            {
                throw new Exception("Test transport failing on connect");
            }

            ConnectCalled = true;
            if (OnConnectChangeStateToConnected)
            {
                Listener?.OnTransportEvent(Id, TransportState.Connected);
                State = TransportState.Connected;
            }
        }

        public void Close(bool suppressClosedEvent = true)
        {
            CloseCalled = true;
            if (suppressClosedEvent == false)
            {
                Listener?.OnTransportEvent(Id, TransportState.Closed);
            }
        }

        public Result Send(RealtimeTransportData data)
        {
            _sendAction(data);
            SentMessages.Add(data);
            return Result.Ok();
        }

        public void SetSendAction(Action<RealtimeTransportData> action)
        {
            _sendAction = action;
        }

        public void SetSendAction(Func<RealtimeTransportData, Task> action)
        {
            SetSendAction(data => { _ = action(data); });
        }

        public void Dispose()
        {
            // No op. Note: ITransport derives from IDisposable
        }
    }
}
