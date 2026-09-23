namespace IO.Ably.MessageEncoders
{
    internal class Utf8Encoder : MessageEncoder
    {
        private const string EncodingNameStr = "utf-8";

        public override string EncodingName => EncodingNameStr;

        public override bool CanProcess(string currentEncoding)
        {
            return currentEncoding.EqualsTo(EncodingNameStr);
        }

        public override Result<ProcessedPayload> Encode(IPayload payload, DecodingContext context)
        {
            return Result.Ok(new ProcessedPayload(payload));
        }

        public override Result<ProcessedPayload> Decode(IPayload payload, DecodingContext context)
        {
            // Assume all the other steps will always work with Utf8
            if (CurrentEncodingIs(payload, EncodingName))
            {
                return Result.Ok(new ProcessedPayload(
                    (payload.Data as byte[]).GetText(),
                    RemoveCurrentEncodingPart(payload)));
            }

            return Result.Ok(new ProcessedPayload(payload));
        }
    }
}
