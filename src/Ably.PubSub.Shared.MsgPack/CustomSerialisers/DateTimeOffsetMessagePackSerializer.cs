using System;
using MsgPack;
using MsgPack.Serialization;

namespace IO.Ably.CustomSerialisers
{
#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class DateTimeOffsetMessagePackSerializer : MessagePackSerializer<DateTimeOffset>
    {
        public DateTimeOffsetMessagePackSerializer(SerializationContext ownerContext)
            : base(ownerContext) { }

        protected override void PackToCore(Packer packer, DateTimeOffset objectTree)
        {
            packer.Pack((long)objectTree.ToUnixTimeInMilliseconds());
        }

        protected override DateTimeOffset UnpackFromCore(Unpacker unpacker)
        {
            var data = unpacker.LastReadData;
            return data.AsInt64().FromUnixTimeInMilliseconds();
        }
    }
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
