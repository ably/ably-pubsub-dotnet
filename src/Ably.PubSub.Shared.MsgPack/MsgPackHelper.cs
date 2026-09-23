using System;
using System.IO;
using IO.Ably.CustomSerialisers;
using MsgPack;
using MsgPack.Serialization;

namespace IO.Ably
{
    internal static class MsgPackHelper
    {
        private static readonly SerializationContext Context;

        static MsgPackHelper()
        {
            Context = GetContext();
        }

        private static SerializationContext GetContext()
        {
            var context = new SerializationContext() { SerializationMethod = SerializationMethod.Map };
            context.Serializers.Register(new DateTimeOffsetMessagePackSerializer(context));
            context.Serializers.Register(new TimespanMessagePackSerializer(context));
            context.Serializers.Register(new IO_Ably_CapabilitySerializer(context));
            context.Serializers.Register(new IO_Ably_TokenRequestSerializer(context));
            context.Serializers.Register(new IO_Ably_Auth_TokenDetailsSerializer(context));
            context.Serializers.Register(new IO_Ably_ConnectionDetailsMessageSerializer(context));
            context.Serializers.Register(new IO_Ably_ErrorInfoSerializer(context));
            context.Serializers.Register(new IO_Ably_MessageCountSerializer(context));
            context.Serializers.Register(new IO_Ably_MessageTypesSerializer(context));
            context.Serializers.Register(new IO_Ably_RequestCountSerializer(context));
            context.Serializers.Register(new IO_Ably_ResourceCountSerializer(context));
            context.Serializers.Register(new IO_Ably_ConnectionTypesSerializer(context));
            context.Serializers.Register(new IO_Ably_OutboundMessageTrafficSerializer(context));
            context.Serializers.Register(new IO_Ably_InboundMessageTrafficSerializer(context));
            context.Serializers.Register(new IO_Ably_MessageSerializer(context));
            context.Serializers.Register(new IO_Ably_PresenceMessageSerializer(context));
            context.Serializers.Register(new IO_Ably_PresenceMessage_ActionTypeSerializer(context));
            context.Serializers.Register(new IO_Ably_StatsSerializer(context));
            context.Serializers.Register(new IO_Ably_Types_ProtocolMessageSerializer(context));
            context.Serializers.Register(new IO_Ably_Types_ProtocolMessage_MessageActionSerializer(context));
            context.Serializers.Register(new System_Net_HttpStatusCodeSerializer(context));

            return context;
        }

        public static byte[] Serialise(object obj)
        {
            var serialiser = Context.GetSerializer(obj.GetType());
            using (var ms = new MemoryStream())
            {
                serialiser.Pack(ms, obj, PackerCompatibilityOptions.None);
                return ms.ToArray();
            }
        }

        public static object Deserialise(byte[] byteArray, Type objectType)
        {
            if (byteArray == null || byteArray.Length == 0)
            {
                return null;
            }

            using (var ms = new MemoryStream(byteArray))
            {
                var serialiser = Context.GetSerializer(objectType);
                return serialiser.Unpack(ms);
            }
        }

        public static object DeserialiseMsgPackObject(byte[] byteArray)
        {
            return Deserialise(byteArray, typeof(MessagePackObject));
        }

        public static T Deserialise<T>(byte[] byteArray)
        {
            return (T)Deserialise(byteArray, typeof(T));
        }
    }
}
