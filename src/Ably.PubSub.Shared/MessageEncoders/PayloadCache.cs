using System;
using System.Diagnostics;

namespace IO.Ably.MessageEncoders
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    internal class PayloadCache : IEquatable<PayloadCache>
    {
        public bool Equals(PayloadCache other)
        {
            if (other is null)
            {
                return false;
            }

            return Equals(ByteData, other.ByteData) && StringData == other.StringData && Encoding == other.Encoding;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((PayloadCache)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ByteData != null ? ByteData.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (StringData != null ? StringData.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Encoding != null ? Encoding.GetHashCode() : 0);
                return hashCode;
            }
        }

        public byte[] ByteData { get; }

        public string StringData { get; }

        public string Encoding { get; }

        public byte[] GetBytes()
        {
            if (ByteData != null)
            {
                return ByteData;
            }

            if (StringData.IsNotEmpty())
            {
                return StringData.GetBytes();
            }

            return new byte[]
            {
            };
        }

        private string DebuggerDisplay =>
            $"Bytes: '{ByteData?.Length}'. String: {StringData?.Length}. Encoding: {Encoding}";

        public PayloadCache(string data, string encoding)
        {
            StringData = data;
            Encoding = encoding;
        }

        public PayloadCache(byte[] data, string encoding)
        {
            ByteData = data;
            Encoding = encoding;
        }
    }
}
