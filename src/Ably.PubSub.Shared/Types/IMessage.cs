using System;

namespace IO.Ably
{
    // No need to document an internal interface.
#pragma warning disable SA1600 // Elements should be documented

    internal interface IPayload
    {
        object Data { get; set; }

        string Encoding { get; set; }
    }

    internal interface IMessage : IPayload
    {
        string Id { get; set; }

        string ConnectionId { get; set; }

        string ConnectionKey { get; set; }

        string ClientId { get; set; }

        DateTimeOffset? Timestamp { get; set; }
    }

#pragma warning restore SA1600 // Elements should be documented
}
