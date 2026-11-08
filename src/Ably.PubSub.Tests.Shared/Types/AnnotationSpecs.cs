using System.Collections.Generic;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ably.PubSub.Tests.Types
{
    public class AnnotationSpecs
    {
        [Fact]
        [Trait("spec", "TAN2")]
        public void ShouldDeserializeEveryAttribute()
        {
            const string json = @"{
                ""id"": ""ann:1"",
                ""action"": 1,
                ""clientId"": ""client"",
                ""connectionId"": ""conn"",
                ""name"": ""like"",
                ""count"": 3,
                ""data"": ""payload"",
                ""encoding"": ""utf-8"",
                ""timestamp"": 1700000000000,
                ""serial"": ""ann-serial"",
                ""messageSerial"": ""msg-serial"",
                ""type"": ""reaction:distinct.v1"",
                ""extras"": { ""k"": ""v"" }
            }";

            var annotation = JsonHelper.Deserialize<Annotation>(json);

            annotation.Id.Should().Be("ann:1");
            annotation.Action.Should().Be(AnnotationAction.Delete);
            annotation.ClientId.Should().Be("client");
            annotation.ConnectionId.Should().Be("conn");
            annotation.Name.Should().Be("like");
            annotation.Count.Should().Be(3);
            annotation.Data.Should().Be("payload");
            annotation.Encoding.Should().Be("utf-8");
            annotation.Timestamp.Should().Be(System.DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            annotation.Serial.Should().Be("ann-serial");
            annotation.MessageSerial.Should().Be("msg-serial");
            annotation.Type.Should().Be("reaction:distinct.v1");
            annotation.Extras.Should().NotBeNull();
        }

        [Theory]
        [InlineData(0, AnnotationAction.Create)]
        [InlineData(1, AnnotationAction.Delete)]
        [Trait("spec", "TAN2b")]
        public void ShouldMapTheActionWireValues(int wire, AnnotationAction expected)
        {
            JsonHelper.Deserialize<Annotation>($"{{\"action\":{wire}}}").Action.Should().Be(expected);
            JObject.Parse(JsonHelper.Serialize(new Annotation { Action = expected }))["action"].Value<int>().Should().Be(wire);
        }

        [Theory]
        [InlineData("{\"action\":42}")]
        [InlineData("{\"action\":-1}")]
        [InlineData("{\"action\":\"create\"}")]
        [InlineData("{}")]
        [Trait("spec", "RSF1")]
        public void UnknownOrAbsentAction_ShouldBeNull(string json)
        {
            JsonHelper.Deserialize<Annotation>(json).Action.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "TAN2")]
        public void ShouldOnlySerializeTheFieldsWhichAreSet()
        {
            var json = JsonHelper.Serialize(new Annotation { Type = "total.v1" });

            json.Should().Be("{\"type\":\"total.v1\"}");
        }

        [Fact]
        [Trait("spec", "RTN7a")]
        public void ProtocolMessage_ShouldRequireAckOnlyForMessagePresenceAndAnnotation()
        {
            new ProtocolMessage(ProtocolMessage.MessageAction.Annotation).AckRequired.Should().BeTrue();
            new ProtocolMessage(ProtocolMessage.MessageAction.Message).AckRequired.Should().BeTrue();
            new ProtocolMessage(ProtocolMessage.MessageAction.Presence).AckRequired.Should().BeTrue();
            new ProtocolMessage(ProtocolMessage.MessageAction.Attach).AckRequired.Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "TR3w")]
        [Trait("spec", "TR3x")]
        public void AnnotationModes_ShouldMapToTheFlagsAndBack()
        {
            ((int)ProtocolMessage.Flag.AnnotationPublish).Should().Be(1 << 21);
            ((int)ProtocolMessage.Flag.AnnotationSubscribe).Should().Be(1 << 22);

            var message = new ProtocolMessage();
            message.SetModesAsFlags(new[] { ChannelMode.AnnotationPublish, ChannelMode.AnnotationSubscribe, ChannelMode.Publish });

            message.HasFlag(ProtocolMessage.Flag.AnnotationPublish).Should().BeTrue();
            message.HasFlag(ProtocolMessage.Flag.AnnotationSubscribe).Should().BeTrue();
            ((ProtocolMessage.Flag)message.Flags.Value).FromFlag().Should().BeEquivalentTo(
                new List<ChannelMode> { ChannelMode.Publish, ChannelMode.AnnotationPublish, ChannelMode.AnnotationSubscribe });
        }
    }
}
