using MsgPack;
using MsgPack.Serialization;

namespace IO.Ably.CustomSerialisers
{
#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class CapabilityMessagePackSerializer : MessagePackSerializer<Capability>
    {
        public CapabilityMessagePackSerializer(SerializationContext ownerContext)
            : base(ownerContext)
        {
        }

        protected override void PackToCore(Packer packer, Capability objectTree)
        {
            packer.Pack(objectTree.ToJson());
        }

        protected override Capability UnpackFromCore(Unpacker unpacker)
        {
            MessagePackObject obj = string.Empty;
            if (unpacker.ReadObject(out obj))
            {
                return new Capability(obj.ToString());
            }

            return new Capability();
        }
    }
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
