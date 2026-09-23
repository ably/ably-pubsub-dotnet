using System.Linq;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class CapabilityTests
    {
        [Fact]
        public void Capability_WithOutAnyResources_ReturnsJsonString()
        {
            var capability = new Capability();
            capability.ToJson().Should().Be(string.Empty);
        }

        [Fact]
        public void Capability_WithResource_ReturnsCorrectJsonString()
        {
            var capability = new Capability();

            capability
                .AddResource("name").AllowPublish();

            capability.ToJson().Should().Be("{\"name\":[\"publish\"]}");
        }

        [Fact]
        public void Capability_WithResourceThatHasPublishSubscribeAndPresence_ReturnsJsonStringWithCorrectResourceOrder()
        {
            var capability = new Capability();

            capability
                .AddResource("name").AllowSubscribe().AllowPublish().AllowPresence();

            capability.ToJson().Should().Be("{\"name\":[\"presence\",\"publish\",\"subscribe\"]}");
        }

        [Fact]
        public void Capability_WithResourceThatHasPublishAndThenAll_ReturnsJsonWithResourceEqualToStarWithoutShowingPublish()
        {
            var capability = new Capability();
            capability.AddResource("name").AllowPublish().AllowAll();

            capability.ToJson().Should().Be("{\"name\":[\"*\"]}");
        }

        [Fact]
        public void Capability_WithResourceThatHasNoAllowedOperations_DoesNotIncludeResourceInJson()
        {
            var capability = new Capability();
            capability.AddResource("name");

            capability.ToJson().Should().Be(string.Empty);
        }

        [Fact]
        public void Capability_WithMultipleResources_OrdersThemInAlphabeticalOrder()
        {
            var capability = new Capability();

            capability.AddResource("second").AllowPublish();
            capability.AddResource("first").AllowAll();

            capability.ToJson().Should().Be("{\"first\":[\"*\"],\"second\":[\"publish\"]}");
        }

        [Fact]
        public void Capability_InitializedWith2Resources_AddsThemCorrectlyToAllowedResourced()
        {
            const string capabilityString = "{\"first\":[\"*\"],\"second\":[\"publish\"]}";
            var capability = new Capability(capabilityString);

            capability.Resources.Count.Should().Be(2);
            capability.Resources.First().Name.Should().Be("first");
            capability.Resources.First().AllowedOperations.First().Should().Be("*");
        }

        [Fact]
        public void Capability_WithDefaultAllAll_AddsThemCorrectly()
        {
            const string capabilityString = "{\"*\":[\"*\"]}";
            var capability = new Capability(capabilityString);

            capability.ToJson().Should().Be(capabilityString);
        }
    }
}
