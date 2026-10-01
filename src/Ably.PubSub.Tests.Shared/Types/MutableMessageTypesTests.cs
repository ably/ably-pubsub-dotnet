using System.Collections.Generic;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Ably.PubSub.Tests.Types
{
    public class MutableMessageTypesTests
    {
        [Fact]
        [Trait("spec", "MOP2a")]
        [Trait("spec", "MOP2b")]
        [Trait("spec", "MOP2c")]
        public void MessageOperation_ShouldHoldClientIdDescriptionAndMetadata()
        {
            var operation = new MessageOperation
            {
                ClientId = "user1",
                Description = "fixed typo",
                Metadata = new Dictionary<string, string> { { "reason", "typo" } },
            };

            operation.ClientId.Should().Be("user1");
            operation.Description.Should().Be("fixed typo");
            operation.Metadata.Should().Contain("reason", "typo");
        }

        [Fact]
        [Trait("spec", "UDR2a")]
        public void UpdateDeleteResult_ShouldHoldTheVersionSerial()
        {
            new UpdateDeleteResult("vs1").VersionSerial.Should().Be("vs1");
            new UpdateDeleteResult(null).VersionSerial.Should().BeNull();
            new UpdateDeleteResult().VersionSerial.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "UDR2a")]
        public void UpdateDeleteResult_ShouldDeserializeVersionSerialFromJson()
        {
            JsonConvert.DeserializeObject<UpdateDeleteResult>("{\"versionSerial\":\"vs1\",\"other\":1}").VersionSerial.Should().Be("vs1");
            JsonConvert.DeserializeObject<UpdateDeleteResult>("{\"versionSerial\":null}").VersionSerial.Should().BeNull();
        }

        [Theory]
        [InlineData(MessageAction.MessageUpdate)]
        [InlineData(MessageAction.MessageDelete)]
        [InlineData(MessageAction.MessageAppend)]
        [Trait("spec", "RSL15c")]
        [Trait("spec", "RTL32c")]
        public void CreateEdit_ShouldReturnAFreshCopyWithoutMutatingTheOriginal(MessageAction action)
        {
            var extras = new MessageExtras();
            var original = new Message("name", "data", "client", extras) { Serial = "s1", Id = "id1" };

            var edit = Message.CreateEdit(original, null, action);

            edit.Should().NotBeSameAs(original);
            edit.Serial.Should().Be("s1");
            edit.Action.Should().Be(action);
            edit.Name.Should().Be("name");
            edit.Data.Should().Be("data");
            edit.ClientId.Should().Be("client");
            edit.Id.Should().Be("id1");
            edit.Extras.Should().BeSameAs(extras);
            edit.Version.Should().BeNull();

            original.Action.Should().BeNull();
            original.Version.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RSL15b7")]
        [Trait("spec", "RTL32b2")]
        public void CreateEdit_WithOperation_ShouldSetTheVersionFromTheOperation()
        {
            var operation = new MessageOperation
            {
                ClientId = "user1",
                Description = "desc",
                Metadata = new Dictionary<string, string> { { "k", "v" } },
            };

            var edit = Message.CreateEdit(new Message { Serial = "s1", Data = "x" }, operation, MessageAction.MessageUpdate);

            edit.Version.ClientId.Should().Be("user1");
            edit.Version.Description.Should().Be("desc");
            edit.Version.Metadata.Should().Contain("k", "v");
            edit.Version.Metadata.Should().NotBeSameAs(operation.Metadata);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [Trait("spec", "RSL15a")]
        [Trait("spec", "RTL32a")]
        public void CreateEdit_WithoutSerial_ShouldThrow40003(string serial)
        {
            var ex = Assert.Throws<AblyException>(() => Message.CreateEdit(new Message { Serial = serial }, null, MessageAction.MessageDelete));
            ex.ErrorInfo.Code.Should().Be(40003);
            ex.ErrorInfo.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
    }
}
