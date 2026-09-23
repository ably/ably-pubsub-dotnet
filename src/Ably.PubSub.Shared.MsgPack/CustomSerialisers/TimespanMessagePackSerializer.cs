using System;
using MsgPack;
using MsgPack.Serialization;

namespace IO.Ably.CustomSerialisers
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements should be documented
    public class TimespanMessagePackSerializer : MessagePackSerializer<TimeSpan>
    {
        public TimespanMessagePackSerializer(SerializationContext ownerContext)
            : base(ownerContext) { }

        protected override void PackToCore(Packer packer, TimeSpan objectTree)
        {
            packer.Pack((long)objectTree.TotalMilliseconds);
        }

        protected override TimeSpan UnpackFromCore(Unpacker unpacker)
        {
            var data = unpacker.LastReadData;
            return TimeSpan.FromMilliseconds(data.AsInt64());
        }
    }
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
