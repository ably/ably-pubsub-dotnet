using System;
using System.Collections.Generic;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Types
{
    /// <summary>
    /// Parsing of the wire attributes introduced for object messages (TR3, TR4r, CD2i, CD2j).
    /// </summary>
    public class ObjectsWireTypesSpecs
    {
        [Fact]
        [Trait("spec", "CD2i")]
        [Trait("spec", "CD2j")]
        public void ConnectedMessage_ShouldParseObjectsGcGracePeriodAndSiteCode()
        {
            const string connected =
                "{\"action\":4,\"connectionId\":\"c1\",\"connectionDetails\":{\"connectionKey\":\"k\",\"maxFrameSize\":524288," +
                "\"objectsGCGracePeriod\":86400000,\"siteCode\":\"eu-west-1-A\"}}";

            var message = JsonHelper.Deserialize<ProtocolMessage>(connected);

            message.ConnectionDetails.ObjectsGCGracePeriod.Should().Be(TimeSpan.FromHours(24));
            message.ConnectionDetails.SiteCode.Should().Be("eu-west-1-A");
        }

        [Fact]
        [Trait("spec", "CD2i")]
        [Trait("spec", "CD2j")]
        public void ConnectedMessage_WithoutObjectsAttributes_ShouldLeaveThemUnset()
        {
            var message = JsonHelper.Deserialize<ProtocolMessage>("{\"action\":4,\"connectionDetails\":{\"connectionKey\":\"k\"}}");

            message.ConnectionDetails.ObjectsGCGracePeriod.Should().BeNull();
            message.ConnectionDetails.SiteCode.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "TR3h")]
        [Trait("spec", "TR3y")]
        [Trait("spec", "TR3z")]
        public void ObjectFlagBits_ShouldMatchTheSpecifiedPositions()
        {
            ((int)ProtocolMessage.Flag.HasObjects).Should().Be(1 << 7);
            ((int)ProtocolMessage.Flag.ObjectSubscribe).Should().Be(1 << 24);
            ((int)ProtocolMessage.Flag.ObjectPublish).Should().Be(1 << 25);
        }

        [Fact]
        [Trait("spec", "TR3y")]
        [Trait("spec", "TR3z")]
        public void ObjectModes_ShouldMapToTheFlagsAndBack()
        {
            ChannelMode.ObjectSubscribe.ToFlag().Should().Be(ProtocolMessage.Flag.ObjectSubscribe);
            ChannelMode.ObjectPublish.ToFlag().Should().Be(ProtocolMessage.Flag.ObjectPublish);

            var message = new ProtocolMessage();
            message.SetModesAsFlags(new[] { ChannelMode.ObjectSubscribe, ChannelMode.ObjectPublish, ChannelMode.Subscribe });
            message.HasFlag(ProtocolMessage.Flag.ObjectSubscribe).Should().BeTrue();
            message.HasFlag(ProtocolMessage.Flag.ObjectPublish).Should().BeTrue();

            ((ProtocolMessage.Flag)message.Flags.Value).FromFlag().Should().BeEquivalentTo(
                new List<ChannelMode> { ChannelMode.Subscribe, ChannelMode.ObjectSubscribe, ChannelMode.ObjectPublish });
        }

        [Fact]
        [Trait("spec", "TR3")]
        public void HasObjectsFlag_ShouldNotBeReportedAsAChannelMode()
        {
            ProtocolMessage.Flag.HasObjects.FromFlag().Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "TR3")]
        public void ExistingModeMappings_ShouldBeUndisturbed()
        {
            var expected = new Dictionary<ChannelMode, int>
            {
                { ChannelMode.Presence, 1 << 16 },
                { ChannelMode.Publish, 1 << 17 },
                { ChannelMode.Subscribe, 1 << 18 },
                { ChannelMode.PresenceSubscribe, 1 << 19 },
                { ChannelMode.AnnotationPublish, 1 << 21 },
                { ChannelMode.AnnotationSubscribe, 1 << 22 },
            };

            foreach (var pair in expected)
            {
                ((int)pair.Key.ToFlag().Value).Should().Be(pair.Value, pair.Key.ToString());
                ((ProtocolMessage.Flag)pair.Value).FromFlag().Should().BeEquivalentTo(new[] { pair.Key });
            }
        }

        [Fact]
        [Trait("spec", "TR4r")]
        public void ProtocolMessage_ShouldKeepTheStateArrayOpaque()
        {
            var message = JsonHelper.Deserialize<ProtocolMessage>("{\"action\":19,\"state\":[{\"a\":1},{\"b\":[2]}]}");

            message.State.Should().HaveCount(2);
        }
    }
}
