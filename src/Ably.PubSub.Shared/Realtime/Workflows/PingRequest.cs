using System;
using System.Net;

namespace IO.Ably.Realtime.Workflow
{
    internal class PingRequest
    {
        public static readonly ErrorInfo DefaultError = new ErrorInfo("Unable to ping service; not connected", ErrorCodes.BadRequest, HttpStatusCode.BadRequest);

        public static readonly ErrorInfo TimeOutError = new ErrorInfo("Unable to ping service; Request timed out", 40800, HttpStatusCode.RequestTimeout);

        public string Id { get; }

        public Action<TimeSpan?, ErrorInfo> Callback { get; }

        public DateTimeOffset Created { get; }

        public PingRequest(Action<TimeSpan?, ErrorInfo> callback, Func<DateTimeOffset> now)
        {
            Id = "ping-" + Guid.NewGuid().ToString("D");
            Callback = callback;
            Created = now();
        }
    }
}
