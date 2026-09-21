using FluentAssertions;
using IO.Ably.MessageEncoders;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IO.Ably.Tests.MessageEncodes
{
    public class JsonEncoderTests
    {
        private const string JsonArrayData = "[1,2,3]";

        private readonly object _objectData;
        private readonly string _jsonData;
        private readonly int[] _arrayData = { 1, 2, 3 };
        private readonly JsonEncoder _encoder;

        protected JsonEncoderTests()
        {
            _objectData = new { Test = "test", Best = "best" };
            _jsonData = JsonHelper.Serialize(_objectData);
            _encoder = new JsonEncoder();
        }

        private IPayload EncodePayload(object data, string encoding = "")
        {
            var payload = new Message { Data = data, Encoding = encoding };
            return _encoder.Encode(payload, new DecodingContext()).Value;
        }

        private IPayload DecodePayload(object data, string encoding = "")
        {
            var payload = new Message { Data = data, Encoding = encoding };
            return _encoder.Decode(payload, new DecodingContext()).Value;
        }

        public class Decode : JsonEncoderTests
        {
            [Fact]
            public void WithJsonPayload_ConvertsDataToJObjectAndStripsEncoding()
            {
                var payload = DecodePayload(_jsonData, "json");

                payload.Data.Should().BeOfType<JObject>();

                var obj = (payload.Data as JObject).ToObject(_objectData.GetType());
                obj.Should().Be(_objectData);

                payload.Encoding.Should().BeEmpty();
            }

            [Fact]
            public void WithJsonPayloadBeforeOtherPayload_ConvertsDataToJObjectAndStripsEncoding()
            {
                var payload = DecodePayload(_jsonData, "utf-8/json");

                payload.Data.Should().BeOfType<JObject>();
                payload.Encoding.Should().Be("utf-8");
            }

            [Fact]
            public void WithAnotherPayload_LeavesDataAndEncoding()
            {
                var payload = DecodePayload("test", "utf-8");

                payload.Data.Should().Be("test");
                payload.Encoding.Should().Be("utf-8");
            }

            [Fact]
            public void WithInvalidJsonPayload_ShouldReturnFailedResult()
            {
                var result = _encoder.Decode(new Message { Data = "test", Encoding = "json" }, new DecodingContext());
                result.IsFailure.Should().BeTrue();
                result.Error.Message.Should().Be("Invalid Json data: 'test'");
            }
        }

        public class Encode : JsonEncoderTests
        {
            [Fact]
            public void WithObject_ConvertDataToJsonStringAndSetsCorrectEncoding()
            {
                var payload = EncodePayload(_objectData);

                payload.Data.Should().Be(_jsonData);
                payload.Encoding.Should().Be("json");
            }

            [Fact]
            public void WithObjectWithExistingEncoding_ConvertsDataAndAppendsEncoding()
            {
                var payload = EncodePayload(_objectData, "utf-8");

                payload.Data.Should().Be(_jsonData);
                payload.Encoding.Should().Be("utf-8/json");
            }

            [Fact]
            public void WithArray_ConvertsDataCorrectly()
            {
                var payload = EncodePayload(_arrayData);

                payload.Data.Should().Be(JsonArrayData);
                payload.Encoding.Should().Be("json");
            }

            [Fact]
            public void WithStringData_LeavesEncodingAndDataIntact()
            {
                var payload = EncodePayload("test");

                payload.Data.Should().Be("test");
                payload.Encoding.Should().BeEmpty();
            }

            [Fact]
            public void WithNullData_LeavesEncodingAndDataIntact()
            {
                var payload = EncodePayload(null);

                payload.Data.Should().BeNull();
                payload.Encoding.Should().BeEmpty();
            }
        }
    }
}
