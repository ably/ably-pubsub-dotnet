using System;
using IO.Ably.DeltaCodec;

namespace IO.Ably.MessageEncoders
{
    internal class VcDiffEncoder : MessageEncoder
    {
        private const string EncodingNameStr = "vcdiff";

        public override string EncodingName => EncodingNameStr;

        public override bool CanProcess(string currentEncoding)
        {
            return currentEncoding.IsNotEmpty() && currentEncoding.StartsWith("vcdiff");
        }

        public override Result<ProcessedPayload> Encode(IPayload payload, DecodingContext context)
        {
            return Result.Ok(new ProcessedPayload(payload));
        }

        public override Result<ProcessedPayload> Decode(IPayload payload, DecodingContext context)
        {
            var logger = context.ChannelOptions?.Logger ?? DefaultLogger.LoggerInstance;
            if (payload == null)
            {
                return Result.Ok(new ProcessedPayload());
            }

            try
            {
                var payloadBytes = DataHelpers.ConvertToByteArray(payload.Data);

                var previousPayload = context.PreviousPayload?.GetBytes();
                if (previousPayload is null)
                {
                    return Result.Fail<ProcessedPayload>(new VcDiffErrorInfo("Missing previous payload"));
                }

                var result = DeltaDecoder.ApplyDelta(previousPayload, payloadBytes);
                var nextEncoding = RemoveCurrentEncodingPart(payload);

                context.PreviousPayload = new PayloadCache(result.AsByteArray(), nextEncoding);
                return Result.Ok(new ProcessedPayload(
                    result.AsByteArray(),
                    RemoveCurrentEncodingPart(payload)));
            }
            catch (Exception ex)
            {
                var error =
                    $"Payload Encoding: {payload.Encoding}. Payload data: {GetPayloadString()}";
                logger.Error("Error decoding vcdiff message: " + error, ex);

                return Result.Fail<ProcessedPayload>(new VcDiffErrorInfo("Failed to decode vcdiff message", ex));
            }

            string GetPayloadString()
            {
                try
                {
                    if (payload.Data == null)
                    {
                        return "null";
                    }

                    if (payload.Data is byte[])
                    {
                        return (payload.Data as byte[]).ToBase64();
                    }

                    if (payload.Data is string)
                    {
                        return payload.Data as string;
                    }

                    return string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }
}
