using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ably.PubSub.Tests.Types
{
    public class MessageTm2Specs
    {
        private const long TimestampMs = 1700000000000;
        private const long VersionTimestampMs = 1700000005000;

        private static readonly DateTimeOffset MessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs);
        private static readonly DateTimeOffset VersionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(VersionTimestampMs);

        private static MessageHandler CreateHandler() => new MessageHandler(DefaultLogger.LoggerInstance, Protocol.Json);

        private static IList<Message> RealtimeDecode(string messagesJson)
        {
            var messages = JsonHelper.Deserialize<List<Message>>(messagesJson);
            var protocolMessage = new ProtocolMessage { Id = "pm:1", ConnectionId = "conn", Timestamp = MessageTimestamp };
            var result = CreateHandler().DecodeMessages(protocolMessage, messages.Cast<IMessage>(), new DecodingContext());
            result.IsSuccess.Should().BeTrue();
            return messages;
        }

        private static IList<Message> RestDecode(string messagesJson)
        {
            var request = new AblyRequest("/channels/test/messages", HttpMethod.Get);
            var response = new AblyResponse("utf-8", "application/json", Encoding.UTF8.GetBytes(messagesJson));
            var result = CreateHandler().ParsePaginatedResponse<Message>(
                request,
                response,
                _ => Task.FromResult<PaginatedResult<Message>>(null));
            return result.Items;
        }

        public static IEnumerable<object[]> DecodePaths()
        {
            yield return new object[] { "realtime", (Func<string, IList<Message>>)RealtimeDecode };
            yield return new object[] { "rest", (Func<string, IList<Message>>)RestDecode };
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM2s")]
        public void VersionPopulatedFromWire(string path, Func<string, IList<Message>> decode)
        {
            var json = $@"[{{""serial"":""s1"",""timestamp"":{TimestampMs},""version"":{{""serial"":""v1"",""timestamp"":{VersionTimestampMs},""clientId"":""editor"",""description"":""fix typo"",""metadata"":{{""k"":""v""}}}}}}]";

            var message = decode(json).Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Version.Serial.Should().Be("v1");
            message.Version.Timestamp.Should().Be(VersionTimestamp);
            message.Version.ClientId.Should().Be("editor");
            message.Version.Description.Should().Be("fix typo");
            message.Version.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["k"] = "v" });
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM2s1")]
        [Trait("spec", "TM2s2")]
        public void VersionDefaultsFromMessageSerialAndTimestamp(string path, Func<string, IList<Message>> decode)
        {
            var json = $@"[{{""serial"":""s1"",""timestamp"":{TimestampMs},""name"":""n""}}]";

            var message = decode(json).Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Version.Should().NotBeNull();
            message.Version.Serial.Should().Be("s1");
            message.Version.Timestamp.Should().Be(MessageTimestamp);
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM2s1")]
        public void PartialWireVersion_KeepsWireValuesAndDefaultsTheRest(string path, Func<string, IList<Message>> decode)
        {
            var json = $@"[{{""serial"":""s1"",""timestamp"":{TimestampMs},""version"":{{""serial"":""v9""}}}}]";

            var message = decode(json).Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Version.Serial.Should().Be("v9");
            message.Version.Timestamp.Should().Be(MessageTimestamp);
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM2u")]
        [Trait("spec", "TM8a")]
        public void AnnotationsDefaultToEmptyWithEmptySummary(string path, Func<string, IList<Message>> decode)
        {
            var message = decode(@"[{""name"":""n""}]").Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Annotations.Should().NotBeNull();
            message.Annotations.Summary.Should().NotBeNull().And.BeEmpty();
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM8a")]
        public void SummaryFromWireIsKeptLooseAndAccessibleThroughTypedAccessors(string path, Func<string, IList<Message>> decode)
        {
            var json = @"[{""action"":4,""serial"":""s1"",""annotations"":{""summary"":{
                ""reaction:distinct.v1"":{""like"":{""total"":2,""clientIds"":[""a"",""b""],""clipped"":false}},
                ""reaction:multiple.v1"":{""thumbs"":{""total"":5,""clientIds"":{""a"":3,""b"":2},""totalUnidentified"":0,""clipped"":false,""totalClientIds"":2}},
                ""reaction:total.v1"":{""total"":7}}}}]";

            var message = decode(json).Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Action.Should().Be(MessageAction.MessageSummary);
            var summary = message.Annotations.Summary;
            summary.Should().HaveCount(3);
            summary["reaction:distinct.v1"].Should().BeOfType<JObject>();

            var distinct = Summary.AsSummaryDistinctV1(summary["reaction:distinct.v1"]);
            distinct.Should().ContainKey("like");
            distinct["like"].Total.Should().Be(2);
            distinct["like"].ClientIds.Should().Equal("a", "b");

            var multiple = Summary.AsSummaryMultipleV1(summary["reaction:multiple.v1"]);
            multiple["thumbs"].Total.Should().Be(5);
            multiple["thumbs"].ClientIds["a"].Should().Be(3);
            multiple["thumbs"].TotalClientIds.Should().Be(2);

            Summary.AsSummaryTotalV1(summary["reaction:total.v1"]).Total.Should().Be(7);
            Summary.AsSummaryUniqueV1(JObject.Parse(@"{""x"":{""total"":1,""clientIds"":[""c""]}}"))["x"].ClientIds.Should().Equal("c");
            Summary.AsSummaryFlagV1(JObject.Parse(@"{""total"":1,""clientIds"":[""c""]}")).Total.Should().Be(1);
            Summary.AsSummaryTotalV1(null).Should().BeNull();
            Summary.AsSummaryDistinctV1(null).Should().BeNull();
            Summary.AsSummaryMultipleV1(JObject.Parse(@"{""x"":3}")).Should().BeNull();
        }

        [Theory]
        [MemberData(nameof(DecodePaths))]
        [Trait("spec", "TM2r")]
        [Trait("spec", "TM2j")]
        public void SerialAndActionFieldsDecoded(string path, Func<string, IList<Message>> decode)
        {
            var message = decode(@"[{""serial"":""01826232498871-001@abcdefghij"",""action"":1}]").Should().ContainSingle("the {0} path should yield one message", path).Subject;

            message.Serial.Should().Be("01826232498871-001@abcdefghij");
            message.Action.Should().Be(MessageAction.MessageUpdate);
        }

        [Fact]
        [Trait("spec", "TM5")]
        public void MessageActionEnumValuesMatchWireValues()
        {
            ((int)MessageAction.MessageCreate).Should().Be(0);
            ((int)MessageAction.MessageUpdate).Should().Be(1);
            ((int)MessageAction.MessageDelete).Should().Be(2);
            ((int)MessageAction.Meta).Should().Be(3);
            ((int)MessageAction.MessageSummary).Should().Be(4);
            ((int)MessageAction.MessageAppend).Should().Be(5);
        }

        [Theory]
        [InlineData(0, MessageAction.MessageCreate)]
        [InlineData(1, MessageAction.MessageUpdate)]
        [InlineData(2, MessageAction.MessageDelete)]
        [InlineData(3, MessageAction.Meta)]
        [InlineData(4, MessageAction.MessageSummary)]
        [InlineData(5, MessageAction.MessageAppend)]
        [Trait("spec", "TM5")]
        public void ActionWireValuesRoundTrip(int wire, MessageAction expected)
        {
            var message = JsonHelper.Deserialize<Message>($@"{{""action"":{wire}}}");
            message.Action.Should().Be(expected);

            JObject.Parse(JsonHelper.Serialize(message))["action"].Value<int>().Should().Be(wire);
        }

        [Theory]
        [InlineData("42")]
        [InlineData("-1")]
        [InlineData("6")]
        [InlineData("null")]
        [InlineData("\"x\"")]
        [Trait("spec", "TM5")]
        public void UnknownActionDecodesToNull(string wire)
        {
            var message = JsonHelper.Deserialize<Message>($@"{{""name"":""n"",""action"":{wire}}}");

            message.Action.Should().BeNull();
            message.Name.Should().Be("n");
        }

        [Fact]
        [Trait("spec", "TM2j")]
        public void PlainPublishDoesNotSerializeNewFields()
        {
            var json = JObject.Parse(JsonHelper.Serialize(new Message("name", "data")));

            json.Properties().Select(p => p.Name).Should().BeEquivalentTo("name", "data");
        }

        [Fact]
        [Trait("spec", "TM2s")]
        public void VersionSerializesWireKeys()
        {
            var version = new MessageVersion
            {
                Serial = "v1",
                Timestamp = VersionTimestamp,
                ClientId = "c",
                Description = "d",
                Metadata = new Dictionary<string, string> { ["k"] = "v" },
            };

            var json = JObject.Parse(JsonHelper.Serialize(version));

            json["serial"].Value<string>().Should().Be("v1");
            json["timestamp"].Value<long>().Should().Be(VersionTimestampMs);
            json["clientId"].Value<string>().Should().Be("c");
            json["description"].Value<string>().Should().Be("d");
            json["metadata"]["k"].Value<string>().Should().Be("v");
        }

        [Fact]
        [Trait("spec", "TM2r")]
        public void IsEmpty_IsNotChangedByNewFieldsOnPlainMessages()
        {
            new Message().IsEmpty.Should().BeTrue();
            new Message("name").IsEmpty.Should().BeFalse();
            new Message { Action = null, Serial = null, Version = null, Annotations = null }.IsEmpty.Should().BeTrue();
        }

        [Fact]
        [Trait("spec", "TM2r")]
        public void IsEmpty_IsFalseWhenAnyNewFieldIsSet()
        {
            new Message { Serial = "s" }.IsEmpty.Should().BeFalse();
            new Message { Action = MessageAction.MessageCreate }.IsEmpty.Should().BeFalse();
            new Message { Version = new MessageVersion() }.IsEmpty.Should().BeFalse();
            new Message { Annotations = new MessageAnnotations() }.IsEmpty.Should().BeFalse();
        }

        [Fact]
        [Trait("spec", "TM2s")]
        public void Equals_AccountsForNewFields()
        {
            Message Make() => new Message("n", "d")
            {
                Serial = "s",
                Action = MessageAction.MessageUpdate,
                Version = new MessageVersion { Serial = "v", Timestamp = VersionTimestamp, Metadata = new Dictionary<string, string> { ["k"] = "v" } },
                Annotations = new MessageAnnotations { Summary = new Dictionary<string, JToken> { ["t"] = JToken.Parse(@"{""total"":1}") } },
            };

            Make().Should().Be(Make());
            Make().GetHashCode().Should().Be(Make().GetHashCode());

            var differentSerial = Make();
            differentSerial.Serial = "other";
            differentSerial.Should().NotBe(Make());

            var differentAction = Make();
            differentAction.Action = MessageAction.MessageDelete;
            differentAction.Should().NotBe(Make());

            var differentVersion = Make();
            differentVersion.Version.Metadata["k"] = "changed";
            differentVersion.Should().NotBe(Make());

            var differentAnnotations = Make();
            differentAnnotations.Annotations.Summary["t"] = JToken.Parse(@"{""total"":2}");
            differentAnnotations.Should().NotBe(Make());
        }
    }
}
