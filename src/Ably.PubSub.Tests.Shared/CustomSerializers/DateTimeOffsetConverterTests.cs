using System;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IO.Ably.Tests.Shared.CustomSerializers
{
    public class DateTimeOffsetConverterTests
    {
        internal class TestClass
        {
            public TestClass(DateTimeOffset data)
            {
                DateTimeOffset = data;
            }

            public DateTimeOffset DateTimeOffset { get; set; }
        }

        [Fact]
        public void ShouldConvertDateTimeOffsetToMilliseconds()
        {
            var originalDateTimeOffset = new TestClass(new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var serializedJToken = JToken.Parse(serializedDateTimeOffset);
            serializedJToken["DateTimeOffset"].Should().NotBeNull(); // check that json property/key exists
            serializedJToken["DateTimeOffset"].Type.Should().Be(JTokenType.Integer); // check type of the property
            serializedJToken["DateTimeOffset"].Value<long>().Should().Be(1388534400000);

            var message = new Message
            {
                Id = "my-id",
                Data = new TestClass(new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero))
            };

            serializedDateTimeOffset = JsonHelper.Serialize(message);
            serializedJToken = JToken.Parse(serializedDateTimeOffset);
            serializedJToken["data"]["DateTimeOffset"].Should().NotBeNull(); // check that json property/key exists
            serializedJToken["data"]["DateTimeOffset"].Type.Should().Be(JTokenType.Integer); // check type of the property
            serializedJToken["data"]["DateTimeOffset"].Value<long>().Should().Be(1388534400000);
        }

        [Fact]
        public void ShouldPreserveTimezoneInformation()
        {
            var originalDateTimeOffset = new TestClass(new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var deserializedDateTimeOffset = JsonHelper.Deserialize<TestClass>(serializedDateTimeOffset);
            deserializedDateTimeOffset.DateTimeOffset.Should().Be(originalDateTimeOffset.DateTimeOffset);
        }

        [Fact]
        public void ShouldSetMinDateTimeOffsetForExcludedProperty()
        {
            var originalDateTimeOffset = new NullableTestClass();
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var serializedJToken = JToken.Parse(serializedDateTimeOffset);
            serializedJToken["DateTimeOffset"].Should().BeNull();
            var deserializedDateTimeOffset = JsonHelper.Deserialize<TestClass>(serializedDateTimeOffset);
            deserializedDateTimeOffset.DateTimeOffset.Should().Be(DateTimeOffset.MinValue);
        }

        public class NullableTestClass
        {
            public NullableTestClass(DateTimeOffset? data = null)
            {
                DateTimeOffset = data;
            }

            public DateTimeOffset? DateTimeOffset { get; set; }
        }

        [Fact]
        public void ShouldExcludeNullDateTimeOffset()
        {
            var originalDateTimeOffset = new NullableTestClass();
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var serializedJToken = JToken.Parse(serializedDateTimeOffset);
            serializedJToken["DateTimeOffset"].Should().BeNull();
        }

        [Fact]
        public void ShouldSetNullForExcludedProperty()
        {
            var originalDateTimeOffset = new NullableTestClass();
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var serializedJToken = JToken.Parse(serializedDateTimeOffset);
            serializedJToken["DateTimeOffset"].Should().BeNull();
            var deserializedDateTimeOffset = JsonHelper.Deserialize<NullableTestClass>(serializedDateTimeOffset);
            deserializedDateTimeOffset.DateTimeOffset.Should().BeNull();
        }

        [Fact]
        public void ShouldPreserveTimezoneInformationForNullableDateTime()
        {
            var originalDateTimeOffset = new NullableTestClass(new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var serializedDateTimeOffset = JsonHelper.Serialize(originalDateTimeOffset);
            var deserializedDateTimeOffset = JsonHelper.Deserialize<NullableTestClass>(serializedDateTimeOffset);
            deserializedDateTimeOffset.DateTimeOffset.Should().Be(originalDateTimeOffset.DateTimeOffset);
        }
    }
}
