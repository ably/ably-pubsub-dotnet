using System;
using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Types
{
    public class PublishResultTests
    {
        [Fact]
        [Trait("spec", "PBR2a")]
        public void Constructor_WithoutSerials_ShouldHaveEmptyNonNullSerials()
        {
            new PublishResult().Serials.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("spec", "PBR2a")]
        public void Constructor_WithNullList_ShouldHaveEmptyNonNullSerials()
        {
            new PublishResult(null).Serials.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("spec", "PBR2a")]
        public void Constructor_WithSerials_ShouldKeepThemIncludingNullElements()
        {
            new PublishResult(new[] { "a", null, "c" }).Serials.Should().Equal("a", null, "c");
        }

        [Fact]
        [Trait("spec", "PBR2")]
        public void Deserialise_ShouldReadSerials()
        {
            var result = JsonHelper.Deserialize<PublishResult>(@"{""serials"":[""s1"",""s2""]}");

            result.Serials.Should().Equal("s1", "s2");
        }

        [Fact]
        [Trait("spec", "PBR2a")]
        public void Deserialise_ShouldPreserveNullSerialsPositionally()
        {
            var result = JsonHelper.Deserialize<PublishResult>(@"{""serials"":[null,""s2"",null]}");

            result.Serials.Should().Equal(null, "s2", null);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData(@"{""serials"":null}")]
        [InlineData(@"{""serials"":[]}")]
        [Trait("spec", "PBR2a")]
        public void Deserialise_WithAbsentNullOrEmptySerials_ShouldGiveEmptyNonNullSerials(string json)
        {
            JsonHelper.Deserialize<PublishResult>(json).Serials.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSL1n")]
        public void Deserialise_ShouldIgnoreTheOtherFieldsOfTheResponseBody()
        {
            var result = JsonHelper.Deserialize<PublishResult>(@"{""channel"":""test"",""serials"":[""s1""],""other"":{""a"":1}}");

            result.Serials.Should().Equal("s1");
        }

        [Fact]
        [Trait("spec", "PBR2a")]
        public void Serialise_ShouldKeepNullElementsDespiteIgnoringNullProperties()
        {
            var json = JsonHelper.Serialize(new PublishResult(new[] { "a", null }));

            json.Should().Be(@"{""serials"":[""a"",null]}");
        }
    }
}
