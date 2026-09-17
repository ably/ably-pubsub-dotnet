using FluentAssertions;
using IO.Ably.MessageEncoders;
using Xunit;

namespace IO.Ably.Tests.MessageEncodes
{
    public class Base64EncoderTests
    {
        private readonly string _stringData;
        private readonly byte[] _binaryData;
        private readonly string _base64Data;
        private readonly Base64Encoder _encoder;

        protected Base64EncoderTests()
        {
            _stringData = "random-string";
            _binaryData = _stringData.GetBytes();
            _base64Data = _binaryData.ToBase64();
            _encoder = new Base64Encoder();
        }

        public class Decode : Base64EncoderTests
        {
            [Fact]
            public void WithBase64EncodedPayload_ConvertsItBackToBinaryData()
            {
                IPayload payload = new Message { Data = _base64Data, Encoding = "base64" };

                payload = _encoder.Decode(payload, new DecodingContext()).Value;

                ((byte[])payload.Data).Should().BeEquivalentTo(_binaryData);
                payload.Encoding.Should().BeEmpty();
            }

            [Fact]
            public void WithBase64EncodingBeforeOtherEncodings_ConvertsDataAndStripsEncodingCorrectly()
            {
                IPayload payload = new Message { Data = _base64Data, Encoding = "utf-8/base64" };

                payload = _encoder.Decode(payload, new DecodingContext()).Value;

                ((byte[])payload.Data).Should().BeEquivalentTo(_binaryData);
                payload.Encoding.Should().Be("utf-8");
            }

            [Fact]
            public void WithMessageAnotherEncoding_LeavesDataAndEncodingIntact()
            {
                IPayload payload = new Message { Data = _stringData, Encoding = "utf-8" };

                payload = _encoder.Decode(payload, new DecodingContext()).Value;

                payload.Data.Should().Be(_stringData);
                payload.Encoding.Should().Be("utf-8");
            }
        }

        public class EncodeWithBinaryProtocol : Base64EncoderTests
        {
            [Fact]
            public void WithBinaryData_LeavesDataAndEncodingIntact()
            {
                if (!Defaults.MsgPackEnabled)
                {
                    return;
                }

#pragma warning disable 162
                IPayload payload = new Message { Data = _binaryData };

                payload = _encoder.Encode(payload, new DecodingContext()).Value;

                payload.Data.Should().Be(_binaryData);
                payload.Encoding.Should().BeNull();
#pragma warning restore 162
            }
        }

        public class EncodeWithTextProtocol : Base64EncoderTests
        {
            [Fact]
            public void WithBinaryPayloadWithoutPriorEncoding_ConvertsDataToBase64StringAndSetsEncoding()
            {
                IPayload payload = new Message { Data = _binaryData };

                payload = _encoder.Encode(payload, new DecodingContext()).Value;

                payload.Data.Should().Be(_base64Data);
                payload.Encoding.Should().Be(_encoder.EncodingName);
            }

            [Fact]
            public void WithBinaryPayloadAndExistingEncoding_ConvertsDataToBase64StringAndAddsBase64Encoding()
            {
                IPayload payload = new Message { Data = _binaryData, Encoding = "cipher" };

                payload = _encoder.Encode(payload, new DecodingContext()).Value;

                payload.Data.Should().Be(_base64Data);
                payload.Encoding.Should().Be("cipher/" + _encoder.EncodingName);
            }

            [Fact]
            public void WithStringPayload_LeavesDataAndEncodingIntact()
            {
                IPayload payload = new Message { Data = _stringData };

                payload = _encoder.Encode(payload, new DecodingContext()).Value;

                payload.Data.Should().Be(_stringData);
                payload.Encoding.Should().BeNull();
            }

            [Fact]
            public void WithEmptyPayload_LeavesDataAndEncodingIntact()
            {
                IPayload payload = new Message();

                payload = _encoder.Encode(payload, new DecodingContext()).Value;

                payload.Data.Should().BeNull();
                payload.Encoding.Should().BeNull();
            }
        }
    }
}
