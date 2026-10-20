// Message Types Tests (TM1, TM2, TM3, TM4, TM2a, TM2b, TM2c, TM2d, TM2e, TM2f, TM2g, TM2h, TM2i)
// Derived from uts/rest/unit/types/message_types.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class MessageTypesTests : UtsUnitTestBase
    {
        public MessageTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TM2a/message-attributes-0
        [Fact]
        [Trait("spec", "TM2a")]
        public void TM2a_MessageAttributes()
        {
            // Test Steps
            // TM2a - id attribute
            var message = new Message { Id = "unique-id" };
            message.Id.Should().Be("unique-id");

            // TM2b - name attribute
            message = new Message { Name = "event-name" };
            message.Name.Should().Be("event-name");

            // TM2c - data attribute
            message = new Message { Data = "string-data" };
            message.Data.Should().Be("string-data");

            message = new Message { Data = new JObject { ["key"] = "value" } };
            JToken.DeepEquals(message.Data as JToken, new JObject { ["key"] = "value" }).Should().BeTrue();

            message = new Message { Data = new byte[] { 0x01, 0x02 } };
            ((byte[])message.Data).Should().Equal(new byte[] { 0x01, 0x02 });

            // TM2d - clientId attribute
            message = new Message { ClientId = "message-client" };
            message.ClientId.Should().Be("message-client");

            // TM2e - connectionId attribute
            message = new Message { ConnectionId = "conn-id" };
            message.ConnectionId.Should().Be("conn-id");

            // TM2f - timestamp attribute
            message = new Message { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1234567890000) };
            message.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));

            // TM2g - encoding attribute
            message = new Message { Encoding = "json/base64" };
            message.Encoding.Should().Be("json/base64");

            // TM2h - extras attribute
            message = new Message
            {
                Extras = new MessageExtras(new JObject
                {
                    ["push"] = new JObject
                    {
                        ["notification"] = new JObject { ["title"] = "Hello" },
                    },
                }),
            };
            ((string)message.Extras.ToJson()["push"]["notification"]["title"]).Should().Be("Hello");

            // TM2i - serial attribute (server-assigned)
            // Serial is typically read-only from server responses
        }

        // UTS: rest/unit/TM3/from-encoded-deserialization-0
        [Fact]
        [Trait("spec", "TM3")]
        public void TM3_FromEncodedDeserialization()
        {
            // Test Steps
            var jsonData = new JObject
            {
                ["id"] = "msg-123",
                ["name"] = "test-event",
                ["data"] = "hello world",
                ["clientId"] = "sender-client",
                ["connectionId"] = "conn-456",
                ["timestamp"] = 1234567890000,
                ["encoding"] = JValue.CreateNull(),
                ["extras"] = new JObject
                {
                    ["headers"] = new JObject { ["x-custom"] = "value" },
                },
            };

            var message = Message.FromEncoded(jsonData.ToString(Formatting.None));

            // Assertions
            message.Id.Should().Be("msg-123");
            message.Name.Should().Be("test-event");
            message.Data.Should().Be("hello world");
            message.ClientId.Should().Be("sender-client");
            message.ConnectionId.Should().Be("conn-456");
            message.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1234567890000));
            ((string)message.Extras.ToJson()["headers"]["x-custom"]).Should().Be("value");
        }

        // UTS: rest/unit/TM3/from-encoded-decodes-encoding-1
        // DEVIATION: see deviations.md
        [DeviationFact("TM3", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TM3")]
        public void TM3_FromEncodedDecodesEncoding()
        {
            // Test Cases
            var testCases = new[]
            {
                new { Encoding = (string)null, WireData = "plain text", ExpectedData = (object)"plain text" },
                new { Encoding = "json", WireData = "{\"key\":\"value\"}", ExpectedData = (object)new JObject { ["key"] = "value" } },
                new { Encoding = "base64", WireData = "SGVsbG8=", ExpectedData = (object)new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F } },
                new { Encoding = "json/base64", WireData = "eyJrIjoidiJ9", ExpectedData = (object)new JObject { ["k"] = "v" } },
            };

            // Test Steps
            foreach (var testCase in testCases)
            {
                var jsonData = new JObject
                {
                    ["id"] = "msg",
                    ["name"] = "event",
                    ["data"] = testCase.WireData,
                    ["encoding"] = testCase.Encoding,
                };

                var message = Message.FromEncoded(jsonData.ToString(Formatting.None));

                if (testCase.ExpectedData is byte[] expectedBytes)
                {
                    ((byte[])message.Data).Should().Equal(expectedBytes);
                }
                else if (testCase.ExpectedData is JToken expectedToken)
                {
                    JToken.DeepEquals(message.Data as JToken, expectedToken).Should().BeTrue();
                }
                else
                {
                    message.Data.Should().Be(testCase.ExpectedData);
                }

                // Encoding consumed
                if (testCase.Encoding == null)
                {
                    message.Encoding.Should().BeNull();
                }
                else
                {
                    // DEVIATION: the SDK leaves an empty string, not null, once every encoding is consumed - see deviations.md
                    message.Encoding.Should().BeEmpty();
                }
            }
        }

        // UTS: rest/unit/TM4/message-constructors-0
        [Fact]
        [Trait("spec", "TM4")]
        public void TM4_MessageConstructors()
        {
            // Test Steps
            // constructor(name, data)
            var message = new Message("event-name", "payload");
            message.Name.Should().Be("event-name");
            message.Data.Should().Be("payload");
            message.ClientId.Should().BeNull();

            // constructor(name, data, clientId)
            message = new Message("event-name", "payload", "client-1");
            message.Name.Should().Be("event-name");
            message.Data.Should().Be("payload");
            message.ClientId.Should().Be("client-1");

            // Both name and data are nullable
            message = new Message(name: null, data: null);
            message.Name.Should().BeNull();
            message.Data.Should().BeNull();
        }

        // UTS: rest/unit/TM/null-missing-attributes-0
        [Fact]
        [Trait("spec", "TM")]
        public void TM_NullMissingAttributes()
        {
            // Test Steps
            // Minimal message
            var message = new Message();

            // All optional attributes should be null/undefined
            message.Id.Should().BeNull();
            message.Name.Should().BeNull();
            message.Data.Should().BeNull();
            message.ClientId.Should().BeNull();
            message.Timestamp.Should().NotHaveValue();
        }

        // UTS: rest/unit/TM/message-with-extras-1
        [Fact]
        [Trait("spec", "TM")]
        public void TM_MessageWithExtras()
        {
            // Test Steps
            // Push notification extras
            var message = new Message(
                name: "push-event",
                data: "payload",
                extras: new MessageExtras(new JObject
                {
                    ["push"] = new JObject
                    {
                        ["notification"] = new JObject
                        {
                            ["title"] = "New Message",
                            ["body"] = "You have a new notification",
                        },
                        ["data"] = new JObject
                        {
                            ["customKey"] = "customValue",
                        },
                    },
                }));

            // Assertions
            ((string)message.Extras.ToJson()["push"]["notification"]["title"]).Should().Be("New Message");
            ((string)message.Extras.ToJson()["push"]["data"]["customKey"]).Should().Be("customValue");
        }
    }
}
