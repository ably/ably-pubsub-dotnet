using FluentAssertions;
using IO.Ably.MessageEncoders;
using Xunit;

namespace IO.Ably.Tests.MessageEncodes
{
    public class Utf8EncoderTests
    {
        private readonly string _stringData;
        private readonly byte[] _byteData;
        private readonly Utf8Encoder _encoder;

        protected Utf8EncoderTests()
        {
            _stringData = "random_string";
            _byteData = _stringData.GetBytes();
            _encoder = new Utf8Encoder();
        }

        private IPayload DecodePayload(object data, string encoding = "")
        {
            var payload = new Message { Data = data, Encoding = encoding };
            return _encoder.Decode(payload, new DecodingContext()).Value;
        }

        public class Decode : Utf8EncoderTests
        {
            [Fact]
            public void WithUtf8Payload_ConvertsDataAndStripsEncoding()
            {
                var payload = DecodePayload(_byteData, "utf-8");

                payload.Data.Should().Be(_stringData);
                payload.Encoding.Should().BeEmpty();
            }

            [Fact]
            public void WithUtf8PayloadBeforeOthers_StringEncodingCorrectly()
            {
                var payload = DecodePayload(_byteData, "json/utf-8");

                payload.Data.Should().Be(_stringData);
                payload.Encoding.Should().Be("json");
            }

            [Fact]
            public void WithAnotherPayload_LeavesDataAndEncoding()
            {
                var payload = DecodePayload("test", "json");

                payload.Data.Should().Be("test");
                payload.Encoding.Should().Be("json");
            }
        }
    }
}
