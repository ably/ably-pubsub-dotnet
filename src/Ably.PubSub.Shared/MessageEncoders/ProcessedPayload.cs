namespace IO.Ably.MessageEncoders
{
    internal class ProcessedPayload : IPayload
    {
        public object Data { get; set; }

        public string Encoding { get; set; }

        public ProcessedPayload()
        {
        }

        public ProcessedPayload(object data, string encoding)
        {
            Data = data;
            Encoding = encoding;
        }

        public ProcessedPayload(IPayload payload)
        {
            if (payload != null)
            {
                Data = payload.Data;
                Encoding = payload.Encoding;
            }
        }
    }
}
