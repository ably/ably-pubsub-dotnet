using System;
using System.Linq;

namespace IO.Ably.MessageEncoders
{
    internal abstract class MessageEncoder
    {
        public abstract string EncodingName { get; }

        public abstract bool CanProcess(string currentEncoding);

        public abstract Result<ProcessedPayload> Encode(IPayload payload, DecodingContext context);

        public abstract Result<ProcessedPayload> Decode(IPayload payload, DecodingContext context);

        public static bool CurrentEncodingIs(IPayload payload, string encoding)
        {
            return payload.Encoding.IsNotEmpty() && payload.Encoding.EndsWith(encoding, StringComparison.CurrentCultureIgnoreCase);
        }

        public static string GetCurrentEncoding(IPayload payload)
        {
            if (payload.Encoding.IsEmpty())
            {
                return string.Empty;
            }

            return payload.Encoding.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last();
        }

        public static string RemoveCurrentEncodingPart(IPayload payload)
        {
            if (payload.Encoding.IsEmpty())
            {
                return string.Empty;
            }

            var encodings = payload.Encoding.Split('/');
            return string.Join("/", encodings.Take(encodings.Length - 1));
        }

        protected static bool IsEmpty(object data)
        {
            return data == null || (data is string s && s.IsEmpty());
        }

        protected static string AddEncoding(IPayload payload, string encoding)
        {
            return payload.Encoding.IsEmpty() ? encoding : $"{payload.Encoding}/{encoding}";
        }
    }
}
