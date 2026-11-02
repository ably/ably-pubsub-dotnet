using System;
using System.Collections.Generic;
using System.Linq;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Realtime;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Ably.PubSub.Tests.CustomSerializers
{
    /// <summary>
    /// An app can set the process-wide <see cref="JsonConvert.DefaultSettings"/> for its own json.
    /// The SDK must not pick those settings up, otherwise they change the wire format (for example a global
    /// <see cref="StringEnumConverter"/> turns <c>"action":10</c> into <c>"action":"Attach"</c>).
    /// Each test computes the expected output first, then repeats the SDK call with app settings installed and
    /// checks that the output is unchanged.
    /// <para>
    /// The app settings are only visible on the test's own thread, and only while <see cref="WithAppDefaultSettings{T}"/>
    /// runs. The test host serialises its own messages with Newtonsoft on other threads; if it saw these settings
    /// it would drop test results. The original <see cref="JsonConvert.DefaultSettings"/> is restored after every test.
    /// </para>
    /// </summary>
    [Collection(JsonDefaultSettingsTestCollection.Name)]
    public sealed class JsonDefaultSettingsIsolationTests : IDisposable
    {
        private const string ValidKey = "1iZPfA.BjcI_g:wpNhw5RCw6rDjisl";

        [ThreadStatic]
        private static JsonSerializerSettings _appSettingsOnThisThread;

        private readonly Func<JsonSerializerSettings> _originalDefaultSettings;

        public JsonDefaultSettingsIsolationTests()
        {
            _originalDefaultSettings = JsonConvert.DefaultSettings;
            var original = _originalDefaultSettings;
            JsonConvert.DefaultSettings = () => _appSettingsOnThisThread ?? original?.Invoke();
        }

        public enum SampleKind
        {
            First,
            Second,
        }

        public static IEnumerable<object[]> AppSettings => new[]
        {
            new object[] { "StringEnum" },
            new object[] { "StringEnumCamelCase" },
            new object[] { "SnakeCaseStrict" },
        };

        public void Dispose()
        {
            _appSettingsOnThisThread = null;
            JsonConvert.DefaultSettings = _originalDefaultSettings;
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void ProtocolMessage_WithAppDefaultSettings_SerializesToTheSameWireFormat(string appSettings)
        {
            var handler = CreateHandler();
            var expected = handler.GetTransportData(CreateAttachMessage()).Text;

            var actual = WithAppDefaultSettings(appSettings, () => handler.GetTransportData(CreateAttachMessage()).Text);

            actual.Should().Be(expected);
            var json = JObject.Parse(actual);
            json["action"].Type.Should().Be(JTokenType.Integer);
            ((int)json["action"]).Should().Be((int)ProtocolMessage.MessageAction.Attach);
            ((string)json["channelSerial"]).Should().Be("serial-1");
            ((long)json["msgSerial"]).Should().Be(3);
            ((string)json["messages"][0]["clientId"]).Should().Be("client-1");
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void ProtocolMessage_WithAppDefaultSettings_DeserializesFromTheWire(string appSettings)
        {
            const string wire = "{\"action\":11,\"channel\":\"test\",\"channelSerial\":\"serial-1\",\"connectionId\":\"conn-1\","
                + "\"flags\":1,\"timestamp\":1700000000000,\"messages\":[{\"id\":\"msg-1\",\"name\":\"greeting\","
                + "\"clientId\":\"client-1\",\"data\":\"{\\\"text\\\":\\\"hello\\\"}\",\"encoding\":\"json\"}]}";
            var handler = CreateHandler();

            var protocolMessage = WithAppDefaultSettings(appSettings, () =>
            {
                var parsed = handler.ParseRealtimeData(new RealtimeTransportData(wire));
                handler.DecodeMessages(parsed, parsed.Messages, new DecodingContext()).IsSuccess.Should().BeTrue();
                return parsed;
            });

            protocolMessage.Action.Should().Be(ProtocolMessage.MessageAction.Attached);
            protocolMessage.Channel.Should().Be("test");
            protocolMessage.ChannelSerial.Should().Be("serial-1");
            protocolMessage.ConnectionId.Should().Be("conn-1");
            protocolMessage.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            var message = protocolMessage.Messages.Single();
            message.ClientId.Should().Be("client-1");
            message.Encoding.Should().BeNullOrEmpty();
            JToken.DeepEquals((JToken)message.Data, JObject.Parse("{\"text\":\"hello\"}")).Should().BeTrue();
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void TokenDetails_WithAppDefaultSettings_DeserializesFromAuthUrlResponse(string appSettings)
        {
            const string response = "{\"token\":\"token-1\",\"keyName\":\"app.key\",\"issued\":1700000000000,"
                + "\"expires\":1700003600000,\"capability\":\"{\\\"*\\\":[\\\"*\\\"]}\",\"clientId\":\"client-1\"}";

            var tokenDetails = WithAppDefaultSettings(
                appSettings,
                () => JsonHelper.DeserializeObject<TokenDetails>(JObject.Parse(response)));

            tokenDetails.Token.Should().Be("token-1");
            tokenDetails.KeyName.Should().Be("app.key");
            tokenDetails.ClientId.Should().Be("client-1");
            tokenDetails.Issued.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
            tokenDetails.Expires.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700003600000));
            tokenDetails.Capability.Should().Be(Capability.AllowAll);
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void TokenRequest_WithAppDefaultSettings_KeepsWireFormatAndRoundTrips(string appSettings)
        {
            var expected = JsonHelper.Serialize(CreateTokenRequest());

            var actual = WithAppDefaultSettings(appSettings, () => JsonHelper.Serialize(CreateTokenRequest()));
            var parsed = WithAppDefaultSettings(appSettings, () => JsonHelper.Deserialize<TokenRequest>(actual));

            actual.Should().Be(expected);
            var original = CreateTokenRequest();
            parsed.KeyName.Should().Be(original.KeyName);
            parsed.ClientId.Should().Be(original.ClientId);
            parsed.Ttl.Should().Be(original.Ttl);
            parsed.Timestamp.Should().Be(original.Timestamp);
            parsed.Capability.Should().Be(original.Capability);
            parsed.Nonce.Should().Be(original.Nonce);
            parsed.Mac.Should().Be(original.Mac);
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void MessageWithJsonData_WithAppDefaultSettings_EncodesAndDecodesTheSame(string appSettings)
        {
            var expected = EncodeToJson(CreateJsonDataMessage());

            var actual = WithAppDefaultSettings(appSettings, () => EncodeToJson(CreateJsonDataMessage()));
            var decoded = WithAppDefaultSettings(appSettings, () => Message.FromEncoded(actual));

            actual.Should().Be(expected);
            decoded.Name.Should().Be("greeting");
            decoded.ClientId.Should().Be("client-1");
            decoded.Encoding.Should().BeNullOrEmpty();
            JToken.DeepEquals((JToken)decoded.Data, CreateJsonDataMessage().Data as JToken).Should().BeTrue();
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void MessageWithObjectData_WithAppDefaultSettings_IsEncodedWithTheSdkSettingsOnly(string appSettings)
        {
            var expected = EncodeObjectData();

            var actual = WithAppDefaultSettings(appSettings, EncodeObjectData);

            actual.Should().Be(expected);
            actual.Should().Be("{\"DisplayName\":\"name\",\"Kind\":1}");
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void AnnotationSummary_WithAppDefaultSettings_ConvertsTheSame(string appSettings)
        {
            var entry = JObject.Parse(
                "{\"like\":{\"total\":3,\"clientIds\":{\"client-1\":2,\"client-2\":1},\"totalUnidentified\":1,\"clipped\":true,\"totalClientIds\":2}}");

            var summary = WithAppDefaultSettings(appSettings, () => Summary.AsSummaryMultipleV1(entry));

            summary.Should().ContainKey("like");
            var like = summary["like"];
            like.Total.Should().Be(3);
            like.ClientIds.Should().BeEquivalentTo(new Dictionary<string, int> { ["client-1"] = 2, ["client-2"] = 1 });
            like.TotalUnidentified.Should().Be(1);
            like.Clipped.Should().BeTrue();
            like.TotalClientIds.Should().Be(2);
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void DeltaExtras_WithAppDefaultSettings_ConvertsTheSame(string appSettings)
        {
            // A field the SDK doesn't know about, as a newer server could send.
            var data = JObject.Parse("{\"delta\":{\"from\":\"msg-1\",\"format\":\"vcdiff\",\"unknownField\":true}}");

            var extras = WithAppDefaultSettings(appSettings, () => new MessageExtras(data));

            extras.Delta.Should().NotBeNull();
            extras.Delta.From.Should().Be("msg-1");
            extras.Delta.Format.Should().Be("vcdiff");
        }

        [Theory]
        [MemberData(nameof(AppSettings))]
        public void ChannelCurrentState_WithAppDefaultSettings_IsTheSame(string appSettings)
        {
            using (var client = new PubSubRealtimeClient(new ClientOptions(ValidKey) { AutoConnect = false }))
            {
                var channel = (RealtimeChannel)client.Channels.Get("test");
                var expected = channel.GetCurrentState().ToString();

                var actual = WithAppDefaultSettings(appSettings, () => channel.GetCurrentState());

                actual.ToString().Should().Be(expected);
                actual["state"].Type.Should().Be(JTokenType.Integer);
            }
        }

        private static T WithAppDefaultSettings<T>(string name, Func<T> action)
        {
            _appSettingsOnThisThread = CreateAppSettings(name);
            try
            {
                return action();
            }
            finally
            {
                _appSettingsOnThisThread = null;
            }
        }

        private static JsonSerializerSettings CreateAppSettings(string name)
        {
            switch (name)
            {
                case "StringEnum":
                    return new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter> { new StringEnumConverter() },
                    };
                case "StringEnumCamelCase":
                    return new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter> { new StringEnumConverter() },
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    };
                case "SnakeCaseStrict":
                    return new JsonSerializerSettings
                    {
                        ContractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new SnakeCaseNamingStrategy(processDictionaryKeys: true, overrideSpecifiedNames: true),
                        },
                        MissingMemberHandling = MissingMemberHandling.Error,
                        NullValueHandling = NullValueHandling.Include,
                        Formatting = Formatting.Indented,
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown app settings");
            }
        }

        private static MessageHandler CreateHandler() => new MessageHandler(DefaultLogger.LoggerInstance, Protocol.Json);

        private static ProtocolMessage CreateAttachMessage()
        {
            return new ProtocolMessage(ProtocolMessage.MessageAction.Attach, "test")
            {
                ChannelSerial = "serial-1",
                MsgSerial = 3,
                Flags = 1,
                Params = new ChannelParams { ["rewind"] = "1" },
                Messages = new[] { new Message("greeting", "hello") { ClientId = "client-1" } },
            };
        }

        private static TokenRequest CreateTokenRequest()
        {
            return new TokenRequest
            {
                KeyName = "app.key",
                ClientId = "client-1",
                Ttl = TimeSpan.FromHours(1),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000),
                Capability = Capability.AllowAll,
                Nonce = "0123456789abcdef",
                Mac = "mac-1",
            };
        }

        private static Message CreateJsonDataMessage()
        {
            return new Message("greeting", new JObject { ["text"] = "hello", ["Count"] = 2 })
            {
                ClientId = "client-1",
            };
        }

        private static string EncodeToJson(Message message)
        {
            MessageHandler.EncodePayload(message, new DecodingContext()).IsSuccess.Should().BeTrue();
            return JsonHelper.Serialize(message);
        }

        private static string EncodeObjectData()
        {
            var message = new Message("greeting", new SamplePayload { DisplayName = "name", Kind = SampleKind.Second });
            MessageHandler.EncodePayload(message, new DecodingContext()).IsSuccess.Should().BeTrue();
            return (string)message.Data;
        }

        public class SamplePayload
        {
            public string DisplayName { get; set; }

            public SampleKind Kind { get; set; }
        }
    }
}
